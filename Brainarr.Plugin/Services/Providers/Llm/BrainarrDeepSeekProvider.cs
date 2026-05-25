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
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Lidarr.Plugin.Common.Observability;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for DeepSeek
    /// (<c>https://api.deepseek.com/chat/completions</c>).
    ///
    /// <para>
    /// Wave-4b cloud provider. DeepSeek speaks the OpenAI Chat Completions wire format.
    /// </para>
    ///
    /// <para>
    /// Provider-specific quirks:
    /// 1. <c>deepseek-reasoner</c> emits separate "reasoning" content via the
    ///    <c>reasoning_content</c> field on the message — the OpenAI-style "thinking"
    ///    pattern. The provider surfaces it through <see cref="LlmResponse.ReasoningContent"/>
    ///    so callers can choose whether to display or persist it.
    /// 2. <c>JsonMode</c> is supported on all current models. Phase 5b honors
    ///    <see cref="LlmRequest.JsonMode"/> by emitting <c>response_format = {"type":"json_object"}</c>
    ///    in the request body when the caller requests strict JSON output.
    /// </para>
    /// </summary>
    public sealed class BrainarrDeepSeekProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "deepseek";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly StreamingHttpExecutor _streamingExecutor;
        private readonly LlmAuthCircuit _authCircuit;
        private string _model;

        public BrainarrDeepSeekProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrDeepSeekProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrDeepSeekProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("DeepSeek API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = ModelIdMapper.ToRawId("deepseek", model ?? BrainarrConstants.DefaultDeepSeekModel);
            _streamingExecutor = streamingExecutor ?? StreamingHttpExecutor.Shared;
            _authCircuit = authCircuit ?? new LlmAuthCircuit(logger);
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "DeepSeek";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            // ExtendedThinking flag set: deepseek-reasoner emits a separate reasoning field.
            // Other DeepSeek models (chat, coder) ignore it gracefully — the response simply
            // won't populate ReasoningContent.
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.JsonMode
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling
                  | LlmCapabilityFlags.ExtendedThinking,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            _model = ModelIdMapper.ToRawId("deepseek", modelName);
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

            if (_authCircuit.IsOpen(ProviderIdConst, _apiKey, out var circuitReason))
            {
                throw new AuthenticationException(ProviderIdConst, LlmErrorCode.AuthenticationFailed,
                    "Auth circuit open: " + circuitReason);
            }

            LlmResponse result;
            try
            {
                var body = BuildRequestBody(request);
                var response = await SendAsync(body, useTestTimeout: false, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    var ex = LlmErrorMapper.MapHttpError(
                        ProviderIdConst,
                        (int)response.StatusCode,
                        Truncate(response.Content),
                        BrainarrHttpResponseHelpers.ParseRetryAfter(response),
                        inner: null);

                    if (ex.ErrorCode == LlmErrorCode.AuthenticationFailed || ex.ErrorCode == LlmErrorCode.AuthorizationFailed)
                    {
                        _authCircuit.RecordAuthFailure(ProviderIdConst, _apiKey, ex);
                    }
                    throw ex;
                }

                result = ParseCompletion(response.Content ?? string.Empty);
            }
            catch (AuthenticationException)
            {
                throw;
            }
            catch (LlmProviderException lpe) when (
                lpe.ErrorCode == LlmErrorCode.AuthenticationFailed ||
                lpe.ErrorCode == LlmErrorCode.AuthorizationFailed)
            {
                _authCircuit.RecordAuthFailure(ProviderIdConst, _apiKey, lpe);
                throw;
            }

            _authCircuit.RecordSuccess(ProviderIdConst, _apiKey);
            return result;
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
            };

            var stream = await _streamingExecutor.SendForStreamingAsync(
                ProviderIdConst,
                HttpMethod.Post,
                BrainarrConstants.DeepSeekChatCompletionsUrl,
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
                ? ModelIdMapper.ToRawId("deepseek", request.Model)
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
                ["temperature"] = (double?)request.Temperature ?? 0.7,
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
            var temp = (double?)request.Temperature ?? 0.7;
            var maxTokens = request.MaxTokens ?? 2000;
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model)
                ? ModelIdMapper.ToRawId("deepseek", request.Model)
                : _model;

            // Phase 5b: honor LlmRequest.JsonMode by emitting OpenAI-compat
            // response_format={"type":"json_object"}. DeepSeek implements this on all
            // current chat/coder/reasoner models.
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
            var request = new HttpRequestBuilder(BrainarrConstants.DeepSeekChatCompletionsUrl)
                .SetHeader("Authorization", $"Bearer {_apiKey}")
                .SetHeader("Content-Type", "application/json")
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
                var parsed = JsonConvert.DeserializeObject<DeepSeekChatCompletionDto>(content);
                var choice = parsed?.Choices?.FirstOrDefault();
                var text = choice?.Message?.Content ?? string.Empty;
                var reasoning = choice?.Message?.ReasoningContent;

                return new LlmResponse
                {
                    Content = text,
                    ReasoningContent = string.IsNullOrEmpty(reasoning) ? null : reasoning,
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

        BrainarrLlmHint? IBrainarrLlmHintSource.GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid DeepSeek API key. Verify your key at https://platform.deepseek.com/api_keys and ensure it is active.",
                        BrainarrConstants.DocsDeepSeekSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "DeepSeek rate limit exceeded. Wait a few minutes or reduce request frequency.",
                        BrainarrConstants.DocsDeepSeekSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "DeepSeek credits exhausted. Top up your balance at https://platform.deepseek.com.",
                        BrainarrConstants.DocsDeepSeekSection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class DeepSeekChatCompletionDto
        {
            [JsonProperty("choices")]
            public List<DeepSeekChoiceDto>? Choices { get; set; }

            [JsonProperty("usage")]
            public DeepSeekUsageDto? Usage { get; set; }
        }

        private sealed class DeepSeekChoiceDto
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public DeepSeekMessageDto? Message { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private sealed class DeepSeekMessageDto
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }

            // deepseek-reasoner field. Maps to LlmResponse.ReasoningContent.
            [JsonProperty("reasoning_content")]
            public string? ReasoningContent { get; set; }
        }

        private sealed class DeepSeekUsageDto
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
