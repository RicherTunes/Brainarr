using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Common.Streaming.Decoders;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// Shared template for the API-key-authenticated cloud providers that speak the
    /// OpenAI Chat Completions wire format (B-201 / #46 provider dedup).
    ///
    /// <para>
    /// The base owns the plumbing that was previously copy-pasted across
    /// OpenAI / DeepSeek / Groq / OpenRouter / Perplexity / Z.AI GLM: request-body
    /// shaping, Bearer-auth dispatch, auth-circuit bookkeeping, HTTP→exception mapping,
    /// health probing, SSE streaming, and the default choices/message/usage response
    /// parse. Provider quirks live in the template hooks so they stay visible in the
    /// provider file that owns them:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><see cref="ChatCompletionsUrl"/> — the provider endpoint.</item>
    /// <item><see cref="DefaultTemperature"/> — fallback temperature (0.8 OpenAI/OpenRouter, 0.7 others).</item>
    /// <item><see cref="SendsTemperature"/> — temperature policy. All current subclasses send it;
    ///   the hook exists because sibling endpoints (Z.AI Coding, Anthropic-format) reject the
    ///   parameter outright, and any future OpenAI-format provider with that quirk must be able
    ///   to omit it without forking the body builder. Pinned by
    ///   <c>OpenAiChatProviderBaseContractTests.Temperature_OmittedWhenPolicySaysNo</c>.</item>
    /// <item><see cref="SupportsJsonResponseFormat"/> — whether <c>response_format={"type":"json_object"}</c>
    ///   may be emitted for <see cref="LlmRequest.JsonMode"/> (Perplexity: no).</item>
    /// <item><see cref="AddCompletionRequestHeaders"/> / <see cref="BuildStreamingHeaders"/> —
    ///   extra identifying headers (OpenRouter's HTTP-Referer/X-Title, Perplexity's Accept).</item>
    /// <item><see cref="BuildHealthProbeBody"/> — the CheckHealth probe body.</item>
    /// <item><see cref="MapHttpError"/> — error mapping (Z.AI GLM's 429 code-1113 → QuotaExceeded).</item>
    /// <item><see cref="ParseCompletion"/> — response parsing (DeepSeek reasoning_content,
    ///   OpenRouter routed_model metadata, Perplexity citations).</item>
    /// <item><see cref="TransformStreamChunk"/> — per-chunk stream post-processing
    ///   (Perplexity citation-marker stripping).</item>
    /// </list>
    ///
    /// <para>
    /// NOT built on this base by design: <c>BrainarrZaiCodingProvider</c> (Anthropic wire
    /// format on a raw HttpClient) and <c>BrainarrOpenAiCompatibleProvider</c> (optional
    /// auth with a conditional circuit, GET /v1/models health check with Degraded
    /// semantics, opt-in JSON mode via ProviderOptions, no streaming).
    /// </para>
    /// </summary>
    public abstract class BrainarrOpenAiChatProviderBase : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly StreamingHttpExecutor _streamingExecutor;
        private readonly LlmAuthCircuit _authCircuit;
        private readonly string _providerId;
        private string _model;

        protected BrainarrOpenAiChatProviderBase(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            string? model,
            StreamingHttpExecutor? streamingExecutor,
            LlmAuthCircuit? authCircuit,
            string providerId,
            string defaultModel,
            string keyOwnerName)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException($"{keyOwnerName} API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _providerId = providerId;
            _model = ModelIdMapper.ToRawId(providerId, model ?? defaultModel);
            _streamingExecutor = streamingExecutor ?? StreamingHttpExecutor.Shared;
            _authCircuit = authCircuit ?? new LlmAuthCircuit(logger);
        }

        /// <inheritdoc />
        public string ProviderId => _providerId;

        /// <inheritdoc />
        public abstract string DisplayName { get; }

        /// <inheritdoc />
        public abstract LlmProviderCapabilities Capabilities { get; }

        /// <summary>Logger for provider subclasses (request-start debug lines, etc.).</summary>
        protected Logger ProviderLogger => _logger;

        /// <summary>The currently configured raw model id.</summary>
        protected string CurrentModel => _model;

        /// <summary>The chat-completions endpoint this provider posts to.</summary>
        protected abstract string ChatCompletionsUrl { get; }

        /// <summary>Fallback temperature when the request does not carry one.</summary>
        protected virtual double DefaultTemperature => 0.7;

        /// <summary>
        /// Temperature policy hook. When false the body NEVER carries <c>temperature</c>,
        /// even if the request sets one (for endpoints that reject the parameter).
        /// </summary>
        protected virtual bool SendsTemperature => true;

        /// <summary>
        /// Whether <c>response_format = {"type":"json_object"}</c> may be emitted when the
        /// caller requests JSON mode. Providers whose routes reject the parameter return false.
        /// </summary>
        protected virtual bool SupportsJsonResponseFormat => true;

        /// <summary>
        /// Whether the request opts into JSON mode. Default honors <see cref="LlmRequest.JsonMode"/>;
        /// providers with legacy opt-in surfaces (ProviderOptions) can widen this.
        /// </summary>
        protected virtual bool IsJsonModeRequested(LlmRequest request) => request.JsonMode;

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            _model = ModelIdMapper.ToRawId(_providerId, modelName);
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var probe = BuildHealthProbeBody();
                var response = await SendAsync(probe, useTestTimeout: true, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return ProviderHealthResult.Healthy(sw.Elapsed, _providerId, "apiKey", _model);
                }

                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)response.StatusCode}",
                    sw.Elapsed,
                    _providerId,
                    "apiKey",
                    _model,
                    errorCode: ((int)response.StatusCode).ToString());
            }
            catch (LlmProviderException lpe)
            {
                return ProviderHealthResult.Unhealthy(
                    lpe.Message,
                    sw.Elapsed,
                    _providerId,
                    "apiKey",
                    _model,
                    errorCode: lpe.ErrorCode.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ProviderHealthResult.Unhealthy(
                    ex.Message,
                    sw.Elapsed,
                    _providerId,
                    "apiKey",
                    _model);
            }
        }

        /// <summary>
        /// Body of the cheap CheckHealth probe. Default is the shared
        /// "Reply with OK" / max_tokens=5 shape; providers with probe quirks
        /// (Groq's temperature=0, OpenRouter's fixed cheap test model,
        /// Perplexity's larger budget) override it.
        /// </summary>
        protected virtual object BuildHealthProbeBody()
        {
            return new
            {
                model = _model,
                messages = new[] { new { role = "user", content = "Reply with OK" } },
                max_tokens = 5,
            };
        }

        /// <inheritdoc />
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            using var _scope = PluginLogContext.Push("Brainarr", "LlmComplete", provider: _providerId);
            OnCompletionRequestStarting();

            // Auth circuit pre-flight: reject immediately if this key is known-bad.
            if (_authCircuit.IsOpen(_providerId, _apiKey, out var circuitReason))
            {
                throw new AuthenticationException(_providerId, LlmErrorCode.AuthenticationFailed,
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
                    var ex = MapHttpError(
                        (int)response.StatusCode,
                        Truncate(response.Content),
                        BrainarrHttpResponseHelpers.ParseRetryAfter(response),
                        inner: null);

                    if (ex.ErrorCode == LlmErrorCode.AuthenticationFailed || ex.ErrorCode == LlmErrorCode.AuthorizationFailed)
                    {
                        _authCircuit.RecordAuthFailure(_providerId, _apiKey, ex);
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
                // through the status-code branch above.
                _authCircuit.RecordAuthFailure(_providerId, _apiKey, lpe);
                throw;
            }

            _authCircuit.RecordSuccess(_providerId, _apiKey);
            return result;
        }

        /// <summary>
        /// Called at the start of <see cref="CompleteAsync"/>, inside the log scope and
        /// before the auth-circuit pre-flight. Default no-op; OpenAI logs its
        /// REQUEST_START debug line here.
        /// </summary>
        protected virtual void OnCompletionRequestStarting()
        {
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
            var headers = BuildStreamingHeaders();

            var stream = await _streamingExecutor.SendForStreamingAsync(
                _providerId,
                HttpMethod.Post,
                ChatCompletionsUrl,
                headers,
                JsonConvert.SerializeObject(body),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await using (stream)
            {
                var decoder = new OpenAiStreamDecoder();
                await foreach (var chunk in decoder.DecodeAsync(stream, cancellationToken).ConfigureAwait(false))
                {
                    yield return TransformStreamChunk(chunk);
                }
            }
        }

        /// <summary>
        /// Headers for the SSE streaming request. Default is Bearer auth + SSE Accept;
        /// providers with extra identifying headers append to the base list.
        /// </summary>
        protected virtual IReadOnlyList<KeyValuePair<string, string>> BuildStreamingHeaders()
        {
            return new[]
            {
                new KeyValuePair<string, string>("Authorization", $"Bearer {_apiKey}"),
                new KeyValuePair<string, string>("Accept", "text/event-stream"),
            };
        }

        /// <summary>
        /// Per-chunk stream post-processing hook (Perplexity strips inline citation
        /// markers). Default: pass-through.
        /// </summary>
        protected virtual LlmStreamChunk TransformStreamChunk(LlmStreamChunk chunk) => chunk;

        // ---------------------------------------------------------------------
        // Request-body shaping
        // ---------------------------------------------------------------------

        /// <summary>Non-streaming request body (<c>stream=false</c>).</summary>
        protected virtual object BuildRequestBody(LlmRequest request) => BuildChatBody(request, stream: false);

        /// <summary>Streaming request body (<c>stream=true</c>).</summary>
        protected virtual object BuildStreamingRequestBody(LlmRequest request) => BuildChatBody(request, stream: true);

        private Dictionary<string, object?> BuildChatBody(LlmRequest request, bool stream)
        {
            // Insertion order is the serialized field order (Newtonsoft preserves it) and
            // is pinned by OpenAiChatProviderBaseContractTests.RequestBody_PinsWireShape:
            // model, messages, temperature, max_tokens, stream, response_format.
            var body = new Dictionary<string, object?>
            {
                ["model"] = ResolveRequestModel(request),
                ["messages"] = BuildMessages(request),
            };

            if (SendsTemperature)
            {
                body["temperature"] = (double?)request.Temperature ?? DefaultTemperature;
            }

            body["max_tokens"] = request.MaxTokens ?? 2000;
            body["stream"] = stream;

            if (SupportsJsonResponseFormat && IsJsonModeRequested(request))
            {
                body["response_format"] = new { type = "json_object" };
            }

            return body;
        }

        /// <summary>Raw model id for this request: the request override (mapped) or the configured model.</summary>
        protected string ResolveRequestModel(LlmRequest request)
            => !string.IsNullOrWhiteSpace(request.Model) ? ModelIdMapper.ToRawId(_providerId, request.Model) : _model;

        private static object[] BuildMessages(LlmRequest request)
        {
            return string.IsNullOrEmpty(request.SystemPrompt)
                ? new object[] { new { role = "user", content = request.Prompt } }
                : new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.Prompt },
                };
        }

        // ---------------------------------------------------------------------
        // Dispatch + error mapping
        // ---------------------------------------------------------------------

        private async Task<HttpResponse> SendAsync(object body, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var builder = new HttpRequestBuilder(ChatCompletionsUrl)
                .SetHeader("Authorization", $"Bearer {_apiKey}")
                .SetHeader("Content-Type", "application/json");
            AddCompletionRequestHeaders(builder);

            var request = builder.Build();
            request.Method = HttpMethod.Post;
            request.SetContent(JsonConvert.SerializeObject(body));

            var seconds = useTestTimeout
                ? BrainarrConstants.TestConnectionTimeout
                : TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
            request.RequestTimeout = TimeSpan.FromSeconds(seconds);

            try
            {
                return await HttpProviderClient.ExecuteWithCt(_httpClient, request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpException hex) when (hex.Response != null)
            {
                // Phase 5f: plumb Retry-After response header through to LlmProviderException.RetryAfter.
                throw MapHttpError(
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
                throw LlmErrorMapper.MapException(_providerId, ex);
            }
        }

        /// <summary>
        /// Extra headers for the (non-streaming) completion/health requests, applied after
        /// Authorization + Content-Type. Default no-op.
        /// </summary>
        protected virtual void AddCompletionRequestHeaders(HttpRequestBuilder builder)
        {
        }

        /// <summary>
        /// Maps a non-OK HTTP status to the provider exception. Default is common's
        /// <see cref="LlmErrorMapper"/>; providers with vendor error envelopes override.
        /// </summary>
        protected virtual LlmProviderException MapHttpError(int statusCode, string? body, TimeSpan? retryAfter, Exception? inner)
            => LlmErrorMapper.MapHttpError(_providerId, statusCode, body, retryAfter, inner);

        // ---------------------------------------------------------------------
        // Response parsing
        // ---------------------------------------------------------------------

        /// <summary>
        /// Parses the completion response. Default handles the plain OpenAI
        /// choices/message/usage shape; on any parse failure the raw body is surfaced as
        /// <see cref="LlmResponse.Content"/> so RecommendationJsonParser can salvage
        /// truncated payloads.
        /// </summary>
        protected virtual LlmResponse ParseCompletion(string content)
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

        /// <summary>Truncates error bodies before they ride on exceptions/logs.</summary>
        protected static string? Truncate(string? body, int max = 500)
        {
            if (string.IsNullOrEmpty(body)) return body;
            return body.Length <= max ? body : body.Substring(0, max);
        }

        BrainarrLlmHint? IBrainarrLlmHintSource.GetUserHint(LlmProviderException exception) => GetUserHint(exception);

        /// <summary>Per-provider user-facing hint mapping for <see cref="IBrainarrLlmHintSource"/>.</summary>
        protected abstract BrainarrLlmHint? GetUserHint(LlmProviderException exception);

        // -- Shared plain-OpenAI DTOs ------------------------------------------
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
