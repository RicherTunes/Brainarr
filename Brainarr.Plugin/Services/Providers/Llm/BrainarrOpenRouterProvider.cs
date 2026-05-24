using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.Streaming.Decoders;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Lidarr.Plugin.Common.Observability;

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
    /// 3. JSON-mode is gated by the upstream model OpenRouter routes to. The capability
    ///    flag is set, and Phase 5b honors <see cref="LlmRequest.JsonMode"/> by emitting
    ///    <c>response_format = {"type":"json_object"}</c>. OpenRouter forwards the parameter
    ///    to compatible upstream models and silently ignores it for incompatible routes;
    ///    callers that target very old routes can leave <see cref="LlmRequest.JsonMode"/>
    ///    at its default (false) to avoid 422 on the rare strict route.
    /// </para>
    /// </summary>
    public sealed class BrainarrOpenRouterProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "openrouter";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly StreamingHttpExecutor _streamingExecutor;
        private string _model;

        public BrainarrOpenRouterProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null)
        {
        }

        public BrainarrOpenRouterProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("OpenRouter API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = ModelIdMapper.ToRawId("openrouter", model ?? BrainarrConstants.DefaultOpenRouterModel);
            _streamingExecutor = streamingExecutor ?? StreamingHttpExecutor.Shared;
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

            using var _scope = PluginLogContext.Push("Brainarr", "LlmComplete", provider: ProviderIdConst);

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
            if (request == null) throw new ArgumentNullException(nameof(request));
            return StreamAsyncCore(request, cancellationToken);
        }

        private async IAsyncEnumerable<LlmStreamChunk> StreamAsyncCore(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var body = BuildStreamingRequestBody(request);
            var headers = new[]
            {
                new KeyValuePair<string, string>("Authorization", $"Bearer {_apiKey}"),
                new KeyValuePair<string, string>("Accept", "text/event-stream"),
                new KeyValuePair<string, string>("HTTP-Referer", BrainarrConstants.ProjectReferer),
                new KeyValuePair<string, string>("X-Title", BrainarrConstants.OpenRouterTitle),
            };

            var stream = await _streamingExecutor.SendForStreamingAsync(
                ProviderIdConst,
                HttpMethod.Post,
                BrainarrConstants.OpenRouterChatCompletionsUrl,
                headers,
                JsonConvert.SerializeObject(body),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await using (stream)
            {
                var decoder = new OpenAiStreamDecoder();
                await foreach (var chunk in decoder.DecodeAsync(stream, cancellationToken).ConfigureAwait(false))
                {
                    yield return chunk;
                }
            }
        }

        private object BuildStreamingRequestBody(LlmRequest request)
        {
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model)
                ? ModelIdMapper.ToRawId("openrouter", request.Model)
                : _model;
            object[] messages = string.IsNullOrEmpty(request.SystemPrompt)
                ? new object[] { new { role = "user", content = request.Prompt } }
                : new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.Prompt },
                };
            var dict = new Dictionary<string, object?>
            {
                ["model"] = modelRaw,
                ["messages"] = messages,
                ["temperature"] = (double?)request.Temperature ?? 0.8,
                ["max_tokens"] = request.MaxTokens ?? 2000,
                ["stream"] = true,
            };
            if (request.JsonMode) dict["response_format"] = new { type = "json_object" };
            return dict;
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

            // Phase 5b: honor LlmRequest.JsonMode by emitting OpenAI-compat
            // response_format={"type":"json_object"}. OpenRouter brokers the flag to
            // compatible upstream models and ignores it on incompatible routes.
            object? responseFormat = request.JsonMode ? new { type = "json_object" } : null;

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                if (responseFormat != null)
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
                        response_format = responseFormat,
                    };
                }

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

            if (responseFormat != null)
            {
                return new
                {
                    model = modelRaw,
                    messages = new[] { new { role = "user", content = request.Prompt } },
                    temperature = temp,
                    max_tokens = maxTokens,
                    stream = false,
                    response_format = responseFormat,
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
