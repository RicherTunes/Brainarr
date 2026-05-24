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
    /// <see cref="ILlmProvider"/> implementation for OpenAI Chat Completions.
    ///
    /// <para>
    /// Wave-4a foundation provider. Speaks the OpenAI <c>/v1/chat/completions</c> wire format
    /// and uses common's <see cref="LlmErrorMapper"/> for HTTP-status → exception mapping.
    /// </para>
    ///
    /// <para>
    /// Streaming uses common's <c>OpenAiStreamDecoder</c>; non-streaming completions deserialize
    /// the choices/messages payload locally (it's the same shape as the legacy
    /// <c>OpenAIProvider</c> already used).
    /// </para>
    ///
    /// <para>
    /// Adapter wrapping: this class is registered as <see cref="ILlmProvider"/>; brainarr's
    /// existing <see cref="IAIProvider"/> seam is preserved by wrapping it in
    /// <see cref="LlmProviderAdapter"/>. See audit finding #1.
    /// </para>
    /// </summary>
    public sealed class BrainarrOpenAiProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "openai";
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly StreamingHttpExecutor _streamingExecutor;
        private readonly LlmAuthCircuit _authCircuit;
        private string _model;

        public BrainarrOpenAiProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrOpenAiProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrOpenAiProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("OpenAI API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = ModelIdMapper.ToRawId("openai", model ?? BrainarrConstants.DefaultOpenAIModel);
            _streamingExecutor = streamingExecutor ?? StreamingHttpExecutor.Shared;
            _authCircuit = authCircuit ?? new LlmAuthCircuit(logger);
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "OpenAI";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
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
            _model = ModelIdMapper.ToRawId("openai", modelName);
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
            _logger.Debug($"{PluginLogContext.Current?.LinePrefix()}[REQUEST_START] OpenAI completion url={Scrub.Url(BrainarrConstants.OpenAIChatCompletionsUrl)}");

            // Wave-7C auth circuit pre-flight: reject immediately if this key is known-bad.
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
                    // Phase 5f: plumb Retry-After response header through to LlmProviderException.RetryAfter.
                    var ex = LlmErrorMapper.MapHttpError(
                        ProviderIdConst,
                        (int)response.StatusCode,
                        Truncate(response.Content),
                        BrainarrHttpResponseHelpers.ParseRetryAfter(response),
                        inner: null);

                    // Record auth failure BEFORE throwing; the catch below only handles
                    // exceptions from SendAsync itself (HttpException path) that weren't
                    // already recorded here.
                    if (ex.ErrorCode == LlmErrorCode.AuthenticationFailed || ex.ErrorCode == LlmErrorCode.AuthorizationFailed)
                    {
                        _authCircuit.RecordAuthFailure(ProviderIdConst, _apiKey, ex);
                        throw ex;
                    }

                    throw ex;
                }

                result = ParseCompletion(response.Content ?? string.Empty);
            }
            catch (AuthenticationException)
            {
                // Already recorded in the status-code branch above — don't double-count.
                throw;
            }
            catch (LlmProviderException lpe) when (
                lpe.ErrorCode == LlmErrorCode.AuthenticationFailed ||
                lpe.ErrorCode == LlmErrorCode.AuthorizationFailed)
            {
                // Auth exceptions that bubble from SendAsync (HttpException path) without going
                // through the status-code branch above (e.g. HttpException thrown directly).
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
                BrainarrConstants.OpenAIChatCompletionsUrl,
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
            // Same shape as BuildRequestBody but with stream=true. Newtonsoft serializes
            // anonymous-type members in declaration order, so we build a Dictionary to keep
            // this concise across the JsonMode/SystemPrompt permutations.
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model) ? ModelIdMapper.ToRawId("openai", request.Model) : _model;
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
            if (request.JsonMode)
            {
                dict["response_format"] = new { type = "json_object" };
            }
            return dict;
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private object BuildRequestBody(LlmRequest request)
        {
            var temp = (double?)request.Temperature ?? 0.8;
            var maxTokens = request.MaxTokens ?? 2000;
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model) ? ModelIdMapper.ToRawId("openai", request.Model) : _model;

            // Phase 5b: honor LlmRequest.JsonMode by emitting OpenAI's response_format
            // {"type":"json_object"}. The capability flag is already set on this provider so
            // the adapter populates JsonMode on the request when callers want strict JSON.
            object? responseFormat = request.JsonMode ? new { type = "json_object" } : null;

            if (request.SystemPrompt != null)
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
            var request = new HttpRequestBuilder(BrainarrConstants.OpenAIChatCompletionsUrl)
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
                var parsed = JsonConvert.DeserializeObject<OpenAiChatCompletionDto>(content);
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
                // Fallback: surface raw body so RecommendationJsonParser can salvage it.
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
                        "Invalid OpenAI API key. Ensure it starts with 'sk-' and is active. Recreate at https://platform.openai.com/api-keys and verify billing if required.",
                        BrainarrConstants.DocsOpenAIInvalidKey),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "OpenAI rate limit exceeded. Wait 1–5 minutes, reduce request frequency, or switch to a cheaper model.",
                        BrainarrConstants.DocsOpenAIRateLimit),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "OpenAI quota/credits exhausted. Add payment method or reduce usage.",
                        BrainarrConstants.DocsOpenAIRateLimit),
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
