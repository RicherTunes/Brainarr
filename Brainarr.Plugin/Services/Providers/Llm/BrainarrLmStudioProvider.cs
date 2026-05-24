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
using Lidarr.Plugin.Common.Resilience;
using Lidarr.Plugin.Common.Observability;

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
    ///    <see cref="CheckHealthAsync"/> catches this and reports
    ///    <see cref="ProviderHealthResult.Degraded(string, System.TimeSpan?, string?, string?, string?, string?)"/>
    ///    — Phase 5b adopted common's Degraded factory so the UI distinguishes
    ///    "service-not-running" (recoverable: start LM Studio) from "service-returned-500"
    ///    (truly unhealthy). The plugin retains its vendor-specific hint source for the
    ///    "LM Studio not running" learn-more URL.
    /// 3. JsonMode is gated by the loaded model. Exposed as a capability flag, and
    ///    Phase 5b honors <see cref="LlmRequest.JsonMode"/> by emitting
    ///    <c>response_format = {"type":"json_object"}</c> when the caller asks for JSON.
    ///    LM Studio's OpenAI-compat surface accepts this; for non-compliant models the
    ///    server typically degrades to soft-shaping rather than rejecting outright.
    /// 4. BackendHealthCache: <see cref="SendAsync"/> consults <see cref="BackendHealthCache"/>
    ///    before issuing any HTTP request. When the backend is known-down from a previous
    ///    connection-class failure, the call short-circuits immediately with a
    ///    <see cref="NetworkException"/>/<see cref="LlmErrorCode.ConnectionFailed"/> rather than
    ///    burning the 120-second retry budget. On success the cache entry is cleared; on a new
    ///    connection failure it is renewed.
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
        private readonly BackendHealthCache _healthCache;

        public BrainarrLmStudioProvider(
            IHttpClient httpClient,
            Logger logger,
            string? baseUrl = null,
            string? model = null,
            string? apiKey = null)
            : this(httpClient, logger, baseUrl, model, apiKey, BackendHealthCache.Shared) { }

        /// <summary>
        /// Constructor with an explicit health cache — used by tests to inject an isolated instance.
        /// </summary>
        internal BrainarrLmStudioProvider(
            IHttpClient httpClient,
            Logger logger,
            string? baseUrl,
            string? model,
            string? apiKey,
            BackendHealthCache healthCache)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _baseUrl = (baseUrl ?? BrainarrConstants.DefaultLMStudioUrl).TrimEnd('/');
            _model = string.IsNullOrWhiteSpace(model) ? BrainarrConstants.DefaultLMStudioModel : model;
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            _healthCache = healthCache ?? throw new ArgumentNullException(nameof(healthCache));
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
                // Phase 5b: Connection-refused is semantically Degraded, not Unhealthy —
                // the user just hasn't started LM Studio yet. Reporting Degraded keeps
                // IsHealthy=true so transient failover doesn't blacklist the provider, and
                // the [Degraded] StatusMessage prefix surfaces in the UI for diagnostics.
                return ProviderHealthResult.Degraded(
                    $"LM Studio not running at {SafeAuthority(_baseUrl)}: {ex.Message}",
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

            using var _scope = PluginLogContext.Push("Brainarr", "LlmComplete", provider: ProviderIdConst);
            _logger.Debug($"{PluginLogContext.Current?.LinePrefix()}[REQUEST_START] LMStudio completion url={Scrub.Url($"{_baseUrl}/v1/chat/completions")}");

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

            // Phase 5b: honor LlmRequest.JsonMode by emitting OpenAI-compat
            // response_format={"type":"json_object"}. LM Studio routes this through the
            // server-loaded model; capable models constrain to valid JSON, others soft-shape.
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
            // Fast-fail: if the backend is known-down from a recent connection failure, skip the
            // HTTP round-trip and its retry budget entirely. Same grace window as ModelDetection.
            if (_healthCache.IsKnownDown(ProviderIdConst, _baseUrl, out var downReason))
            {
                _logger.Debug($"[HealthCache] Skipping LMStudio completion — {downReason}");
                throw new NetworkException(
                    ProviderIdConst,
                    LlmErrorCode.ConnectionFailed,
                    $"LM Studio backend is unreachable: {downReason}");
            }

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
                var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
                // On any successful HTTP exchange, clear the down-state so future calls go through.
                _healthCache.MarkUp(ProviderIdConst, _baseUrl);
                return response;
            }
            catch (HttpException hex) when (hex.Response != null)
            {
                // Phase 5f: plumb Retry-After response header through to LlmProviderException.RetryAfter.
                // HTTP-level errors (4xx/5xx) are not connection failures — don't MarkDown.
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
                // Connection-class failures (SocketException, HttpRequestException wrapping it, etc.)
                // are recorded so the next call can short-circuit immediately.
                _healthCache.MarkDown(ProviderIdConst, _baseUrl, ex);
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
