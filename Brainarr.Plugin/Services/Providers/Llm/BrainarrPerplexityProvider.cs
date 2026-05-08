using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for Perplexity
    /// (<c>https://api.perplexity.ai/chat/completions</c>).
    ///
    /// <para>
    /// Wave-4b cloud provider. Perplexity speaks the OpenAI Chat Completions wire format
    /// with a key extension: their online Sonar models surface a <c>citations</c> array
    /// (top-level or per-choice) listing source URLs that grounded the response.
    /// </para>
    ///
    /// <para>
    /// Provider-specific quirks:
    /// 1. Citations: surfaced via <see cref="LlmResponse.Metadata"/> under the key
    ///    <c>"citations"</c> as a <c>List&lt;string&gt;</c>. Brainarr's recommendation
    ///    pipeline ignores them today, but downstream observability/audit can read them.
    /// 2. Citation markers like <c>[1]</c>, <c>[12]</c> sometimes appear inline in the
    ///    response content. Stripping is left to <c>RecommendationJsonParser</c>
    ///    (music-domain), but we strip them defensively here so other consumers receive
    ///    clean output.
    /// 3. JSON-mode: not formally supported across all routes. Capability omitted.
    /// </para>
    /// </summary>
    public sealed class BrainarrPerplexityProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "perplexity";
        private const string ApiUrl = "https://api.perplexity.ai/chat/completions";

        private static readonly Regex CitationMarkerRegex =
            new("\\[\\d{1,3}\\]", RegexOptions.Compiled);

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;

        public BrainarrPerplexityProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Perplexity API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = ModelIdMapper.ToRawId("perplexity", model ?? BrainarrConstants.DefaultPerplexityModel);
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "Perplexity";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            // JsonMode intentionally omitted: Perplexity's response_format support varies
            // across Sonar variants; the legacy provider already handled this via its
            // multi-shape attempt loop. Streaming wire-format is OpenAI-compatible but
            // gated on the IHttpClient buffering issue (matches 4a).
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.SystemPrompt,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            _model = ModelIdMapper.ToRawId("perplexity", modelName);
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var probe = new
                {
                    model = _model,
                    messages = new[] { new { role = "user", content = "Reply with 'OK'" } },
                    max_tokens = 10,
                };

                var response = await SendAsync(probe, useTestTimeout: true, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, "apiKey", _model);
                }

                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)response.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    _model,
                    errorCode: ((int)response.StatusCode).ToString());
            }
            catch (LlmProviderException lpe)
            {
                return ProviderHealthResult.Unhealthy(
                    lpe.Message,
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    _model,
                    errorCode: lpe.ErrorCode.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ProviderHealthResult.Unhealthy(
                    ex.Message,
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    _model);
            }
        }

        /// <inheritdoc />
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var body = BuildRequestBody(request);
            var response = await SendAsync(body, useTestTimeout: false, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                // Phase 5f: plumb Retry-After response header through to LlmProviderException.RetryAfter.
                throw LlmErrorMapper.MapHttpError(
                    ProviderIdConst,
                    (int)response.StatusCode,
                    Truncate(response.Content),
                    BrainarrHttpResponseHelpers.ParseRetryAfter(response),
                    inner: null);
            }

            return ParseCompletion(response.Content ?? string.Empty);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<LlmStreamChunk>? StreamAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            // Streaming wire-format is OpenAI-compatible; not yet wired. See 4a notes.
            return null;
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private object BuildRequestBody(LlmRequest request)
        {
            var temp = (double?)request.Temperature ?? 0.7;
            var maxTokens = request.MaxTokens ?? 2000;
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model)
                ? ModelIdMapper.ToRawId("perplexity", request.Model)
                : _model;

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                return new
                {
                    model = modelRaw,
                    messages = new[]
                    {
                        new { role = "system", content = request.SystemPrompt },
                        new { role = "user", content = request.Prompt },
                    },
                    temperature = temp,
                    max_tokens = maxTokens,
                    stream = false,
                };
            }

            return new
            {
                model = modelRaw,
                messages = new[] { new { role = "user", content = request.Prompt } },
                temperature = temp,
                max_tokens = maxTokens,
                stream = false,
            };
        }

        private async Task<HttpResponse> SendAsync(object body, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var request = new HttpRequestBuilder(ApiUrl)
                .SetHeader("Authorization", $"Bearer {_apiKey}")
                .SetHeader("Content-Type", "application/json")
                .SetHeader("Accept", "application/json")
                .Build();

            request.Method = HttpMethod.Post;
            request.SetContent(JsonConvert.SerializeObject(body));

            var seconds = useTestTimeout
                ? BrainarrConstants.TestConnectionTimeout
                : TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
            request.RequestTimeout = TimeSpan.FromSeconds(seconds);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
            }
            catch (HttpException hex) when (hex.Response != null)
            {
                // Phase 5f: plumb Retry-After response header through to LlmProviderException.RetryAfter.
                throw LlmErrorMapper.MapHttpError(
                    ProviderIdConst,
                    (int)hex.Response.StatusCode,
                    Truncate(hex.Response.Content),
                    BrainarrHttpResponseHelpers.ParseRetryAfter(hex.Response),
                    hex);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not LlmProviderException)
            {
                throw LlmErrorMapper.MapException(ProviderIdConst, ex);
            }
        }

        private static LlmResponse ParseCompletion(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new LlmResponse { Content = string.Empty };
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<PerplexityChatCompletionDto>(content);
                var choice = parsed?.Choices?.FirstOrDefault();
                var rawText = choice?.Message?.Content ?? string.Empty;

                // Defensively strip inline citation markers like [1], [12]. RecommendationJsonParser
                // does this too, but other consumers (logging, future direct callers) benefit from
                // a clean Content field here.
                var text = string.IsNullOrEmpty(rawText)
                    ? rawText
                    : CitationMarkerRegex.Replace(rawText, string.Empty);

                // Surface citations via Metadata. Perplexity may report them at top-level
                // (older shape) or per-choice (newer shape) — we merge both, deduped.
                var citations = MergeCitations(parsed?.Citations, choice?.Citations);

                IReadOnlyDictionary<string, object>? metadata = null;
                if (citations.Count > 0)
                {
                    metadata = new Dictionary<string, object> { ["citations"] = citations };
                }

                return new LlmResponse
                {
                    Content = text,
                    FinishReason = choice?.FinishReason,
                    Usage = parsed?.Usage != null
                        ? new LlmUsage
                        {
                            InputTokens = parsed.Usage.PromptTokens,
                            OutputTokens = parsed.Usage.CompletionTokens,
                        }
                        : null,
                    Metadata = metadata,
                };
            }
            catch
            {
                return new LlmResponse { Content = content };
            }
        }

        private static List<string> MergeCitations(List<string>? top, List<string>? perChoice)
        {
            var merged = new List<string>();
            if (top != null) merged.AddRange(top.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (perChoice != null) merged.AddRange(perChoice.Where(s => !string.IsNullOrWhiteSpace(s)));
            return merged.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string? Truncate(string? body, int max = 500)
        {
            if (string.IsNullOrEmpty(body)) return body;
            return body.Length <= max ? body : body.Substring(0, max);
        }

        BrainarrLlmHint? IBrainarrLlmHintSource.GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid Perplexity API key. Verify your key at https://www.perplexity.ai/settings/api and ensure it is active.",
                        BrainarrConstants.DocsPerplexitySection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "Perplexity rate limit exceeded. Wait a few minutes or reduce request frequency.",
                        BrainarrConstants.DocsPerplexitySection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "Perplexity quota exhausted. Check your subscription at https://www.perplexity.ai/settings/api.",
                        BrainarrConstants.DocsPerplexitySection),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        "Perplexity model not found. Verify the model id (e.g., 'sonar-pro', 'llama-3.1-sonar-large-128k-online').",
                        BrainarrConstants.DocsPerplexitySection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class PerplexityChatCompletionDto
        {
            [JsonProperty("choices")]
            public List<PerplexityChoiceDto>? Choices { get; set; }

            [JsonProperty("usage")]
            public PerplexityUsageDto? Usage { get; set; }

            // Top-level citations (older Sonar response shape).
            [JsonProperty("citations")]
            public List<string>? Citations { get; set; }
        }

        private sealed class PerplexityChoiceDto
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public PerplexityMessageDto? Message { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }

            // Per-choice citations (newer shape).
            [JsonProperty("citations")]
            public List<string>? Citations { get; set; }
        }

        private sealed class PerplexityMessageDto
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }
        }

        private sealed class PerplexityUsageDto
        {
            [JsonProperty("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonProperty("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonProperty("total_tokens")]
            public int TotalTokens { get; set; }
        }
    }
}
