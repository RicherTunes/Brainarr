using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    /// <see cref="ILlmProvider"/> implementation for LM Studio's local OpenAI-compatible
    /// server (default: <c>http://localhost:1234/v1</c>).
    ///
    /// <para>
    /// Wave-4c local provider. LM Studio exposes an OpenAI Chat Completions-compatible API
    /// for whichever model the user has loaded in the LM Studio UI. Auth is typically none,
    /// but an optional <c>Authorization: Bearer</c> header is honored when an API key is
    /// supplied (LM Studio supports a configurable token in newer builds).
    /// </para>
    ///
    /// <para>
    /// Provider-specific quirks captured here:
    /// 1. Health check: <c>GET /v1/models</c> — returns 200 with a <c>data</c> array when the
    ///    server is up and a model is loaded. Used instead of a probe completion (faster and
    ///    avoids loading a model just to verify connectivity).
    /// 2. Connection-refused detection: when localhost:1234 isn't running, the host's
    ///    <c>IHttpClient</c> raises a transport exception that <see cref="LlmErrorMapper"/>
    ///    normalizes to a <c>NetworkException</c> with <c>ConnectionFailed</c>. The provider's
    ///    <see cref="CheckHealthAsync"/> catches this and surfaces a graceful "LM Studio not
    ///    running" hint via <see cref="IBrainarrLlmHintSource"/> instead of letting the
    ///    exception cascade. Flagged as a candidate for a common
    ///    <c>ProviderHealthResult.Degraded(reason)</c> shape.
    /// 3. JsonMode is gated by the loaded model — exposed as a capability flag (matching the
    ///    legacy implementation), but the request body does not force <c>response_format</c>;
    ///    the system prompt instructs JSON shape and we trust the model to comply.
    /// </para>
    /// </summary>
    public sealed class BrainarrLmStudioProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "lmstudio";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _baseUrl;
        private readonly string? _apiKey;
        private string _model;

        public BrainarrLmStudioProvider(
            IHttpClient httpClient,
            Logger logger,
            string? baseUrl = null,
            string? model = null,
            string? apiKey = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _baseUrl = (baseUrl ?? BrainarrConstants.DefaultLMStudioUrl).TrimEnd('/');
            _model = string.IsNullOrWhiteSpace(model) ? BrainarrConstants.DefaultLMStudioModel : model;
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "LM Studio";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            // Conservative flags: capabilities depend on the loaded model. JsonMode is exposed
            // (matching legacy) but not forced in the request body — the system prompt drives
            // JSON shape and we trust the model to comply.
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.JsonMode,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            _model = modelName;
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var builder = new HttpRequestBuilder($"{_baseUrl}/v1/models");
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    builder.SetHeader("Authorization", $"Bearer {_apiKey}");
                }

                var request = builder.Build();
                request.Method = HttpMethod.Get;
                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                cancellationToken.ThrowIfCancellationRequested();
                var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
                sw.Stop();

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, "none", _model);
                }

                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)response.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    "none",
                    _model,
                    errorCode: ((int)response.StatusCode).ToString());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpException hex) when (hex.Response != null)
            {
                sw.Stop();
                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)hex.Response.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    "none",
                    _model,
                    errorCode: ((int)hex.Response.StatusCode).ToString());
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Connection-refused / transport failure: degrade gracefully instead of
                // letting the exception cascade. The hint source surfaces a "LM Studio not
                // running" message to the UI.
                return ProviderHealthResult.Unhealthy(
                    $"Cannot reach LM Studio at {SafeAuthority(_baseUrl)}: {ex.Message}",
                    sw.Elapsed,
                    ProviderIdConst,
                    "none",
                    _model,
                    errorCode: "ConnectionFailed");
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
                throw LlmErrorMapper.MapHttpError(
                    ProviderIdConst,
                    (int)response.StatusCode,
                    Truncate(response.Content));
            }

            return ParseCompletion(response.Content ?? string.Empty);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<LlmStreamChunk>? StreamAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            // Streaming uses common's OpenAiStreamDecoder once a direct HttpClient pipeline
            // lands; today the host's IHttpClient buffers full responses. Match wave 4a/4b.
            return null;
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private object BuildRequestBody(LlmRequest request)
        {
            var temp = (double?)request.Temperature ?? 0.5;
            var maxTokens = request.MaxTokens ?? 1200;
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model) ? request.Model : _model;

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
            var builder = new HttpRequestBuilder($"{_baseUrl}/v1/chat/completions")
                .SetHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(_apiKey))
            {
                builder.SetHeader("Authorization", $"Bearer {_apiKey}");
            }

            var request = builder.Build();
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
                throw LlmErrorMapper.MapHttpError(
                    ProviderIdConst,
                    (int)hex.Response.StatusCode,
                    Truncate(hex.Response.Content),
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
                // LM Studio sometimes prefixes a BOM on its response.
                var trimmed = content.TrimStart('﻿');
                var parsed = JsonConvert.DeserializeObject<OpenAiChatCompletionDto>(trimmed);
                var choice = parsed?.Choices?.FirstOrDefault();
                var text = choice?.Message?.Content ?? string.Empty;

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
                };
            }
            catch
            {
                return new LlmResponse { Content = content };
            }
        }

        private static string? Truncate(string? body, int max = 500)
        {
            if (string.IsNullOrEmpty(body)) return body;
            return body.Length <= max ? body : body.Substring(0, max);
        }

        private static string SafeAuthority(string url)
        {
            try { return new Uri(url).Authority; }
            catch { return "(configured host)"; }
        }

        BrainarrLlmHint? IBrainarrLlmHintSource.GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.ConnectionFailed =>
                    new BrainarrLlmHint(
                        $"Cannot reach LM Studio at {SafeAuthority(_baseUrl)}. Ensure LM Studio is running with the local server started (Developer tab → 'Start Server') and a model loaded.",
                        BrainarrConstants.DocsLMStudioSection),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        "LM Studio could not find the requested model. Load a model in the LM Studio UI before running this provider.",
                        BrainarrConstants.DocsLMStudioSection),
                LlmErrorCode.ProviderUnavailable =>
                    new BrainarrLlmHint(
                        "LM Studio reported an internal error. Check the LM Studio server logs and confirm a model is fully loaded.",
                        BrainarrConstants.DocsLMStudioSection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class OpenAiChatCompletionDto
        {
            [JsonProperty("choices")]
            public List<OpenAiChoiceDto>? Choices { get; set; }

            [JsonProperty("usage")]
            public OpenAiUsageDto? Usage { get; set; }
        }

        private sealed class OpenAiChoiceDto
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public OpenAiMessageDto? Message { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private sealed class OpenAiMessageDto
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }
        }

        private sealed class OpenAiUsageDto
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
