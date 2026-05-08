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
    /// <see cref="ILlmProvider"/> implementation for OpenRouter
    /// (<c>https://openrouter.ai/api/v1/chat/completions</c>).
    ///
    /// <para>
    /// Wave-4b cloud provider. OpenRouter speaks the OpenAI Chat Completions wire format
    /// behind a single endpoint that brokers requests to many upstream models
    /// (Anthropic, OpenAI, Google, Meta, DeepSeek, ...).
    /// </para>
    ///
    /// <para>
    /// Provider-specific quirks captured here:
    /// 1. <c>HTTP-Referer</c> and <c>X-Title</c> identifying headers — OpenRouter uses these
    ///    to attribute requests on its dashboard. Brainarr supplies its GitHub URL + project
    ///    name from <see cref="BrainarrConstants"/>.
    /// 2. Model ids are typically vendor-prefixed (<c>anthropic/claude-3.5-sonnet</c>); the
    ///    legacy mapper passes those through unchanged.
    /// 3. JSON-mode is gated by the upstream model OpenRouter routes to. We expose it as a
    ///    capability flag and let the model layer respond with the appropriate body shape;
    ///    we deliberately do NOT force <c>response_format: json_object</c> here because some
    ///    routes 422 when given that flag.
    /// </para>
    /// </summary>
    public sealed class BrainarrOpenRouterProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "openrouter";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;

        public BrainarrOpenRouterProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("OpenRouter API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = ModelIdMapper.ToRawId("openrouter", model ?? BrainarrConstants.DefaultOpenRouterModel);
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "OpenRouter";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            // OpenRouter exposes an OpenAI-compatible surface; the exact features available
            // depend on the upstream model OpenRouter routes the request to. JsonMode is
            // listed because the gateway accepts the flag for compatible models and silently
            // ignores it for others — matching legacy behavior. Streaming is wire-supported
            // (text/event-stream) and decoded by common's OpenAiStreamDecoder, but the host
            // IHttpClient buffers full responses, so StreamAsync currently returns null
            // (matches 4a's pattern — see BrainarrOpenAiProvider).
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.JsonMode
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling
                  | LlmCapabilityFlags.Vision,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            _model = ModelIdMapper.ToRawId("openrouter", modelName);
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var probe = new
                {
                    model = BrainarrConstants.DefaultOpenRouterTestModelRaw,
                    messages = new[] { new { role = "user", content = "Reply with OK" } },
                    max_tokens = 5,
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
            // Streaming requires direct HttpClient access; the host's IHttpClient buffers full
            // responses. Capability is exposed (flag set) so future wiring with common's
            // OpenAiStreamDecoder is a one-line change. See BrainarrOpenAiProvider for the
            // canonical pattern.
            return null;
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private object BuildRequestBody(LlmRequest request)
        {
            var temp = (double?)request.Temperature ?? 0.8;
            var maxTokens = request.MaxTokens ?? 2000;
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model)
                ? ModelIdMapper.ToRawId("openrouter", request.Model)
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
            var request = new HttpRequestBuilder(BrainarrConstants.OpenRouterChatCompletionsUrl)
                .SetHeader("Authorization", $"Bearer {_apiKey}")
                .SetHeader("Content-Type", "application/json")
                .SetHeader("HTTP-Referer", BrainarrConstants.ProjectReferer)
                .SetHeader("X-Title", BrainarrConstants.OpenRouterTitle)
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
                var parsed = JsonConvert.DeserializeObject<OpenAiChatCompletionDto>(content);
                var choice = parsed?.Choices?.FirstOrDefault();
                var text = choice?.Message?.Content ?? string.Empty;

                IReadOnlyDictionary<string, object>? metadata = null;
                // OpenRouter surfaces the actually-routed model in `model` — useful for
                // observability when the client requested `openrouter/auto` and got a
                // specific upstream.
                if (!string.IsNullOrWhiteSpace(parsed?.Model))
                {
                    metadata = new Dictionary<string, object> { ["routed_model"] = parsed!.Model! };
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
                        "Invalid OpenRouter API key. Ensure it starts with 'sk-or-' and is active: https://openrouter.ai/keys",
                        BrainarrConstants.DocsOpenRouterSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "OpenRouter rate limit exceeded. Wait, reduce request frequency, or choose a cheaper/faster route.",
                        BrainarrConstants.DocsOpenRouterSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "OpenRouter requires payment/credit. Add credit or resolve billing: https://openrouter.ai/settings/billing",
                        BrainarrConstants.DocsOpenRouterSection),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        "OpenRouter model not found. Verify the model id at https://openrouter.ai/models — many ids are vendor-prefixed (e.g., 'anthropic/claude-3.5-sonnet').",
                        BrainarrConstants.DocsOpenRouterSection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class OpenAiChatCompletionDto
        {
            [JsonProperty("model")]
            public string? Model { get; set; }

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
