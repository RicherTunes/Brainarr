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

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for Ollama's local model server
    /// (default: <c>http://localhost:11434</c>).
    ///
    /// <para>
    /// Wave-4c local provider. Ollama exposes both a native API
    /// (<c>/api/generate</c>, <c>/api/chat</c>) and an OpenAI-compatible surface at
    /// <c>/v1/chat/completions</c>. We use the OpenAI-compatible path so we can share the
    /// <c>OpenAiStreamDecoder</c> with the rest of the wave-4 fleet.
    /// </para>
    ///
    /// <para>
    /// Provider-specific quirks captured here:
    /// 1. Health check: <c>GET /v1/models</c> first (recent Ollama versions); fallback to
    ///    <c>GET /api/tags</c> on 404 for older builds. Both return JSON with a model list,
    ///    just different shapes — we only care about the 200 status for liveness.
    /// 2. Auth: Ollama runs unauthenticated by default, but a reverse-proxy deployment may
    ///    require <c>Authorization: Bearer</c>. The constructor accepts an optional API key.
    /// 3. <c>keep_alive</c> parameter: surfaces via <c>LlmRequest.ProviderOptions["keep_alive"]</c>
    ///    if present. Controls how long the model stays loaded after a request (e.g. <c>"5m"</c>,
    ///    <c>"-1"</c> for forever, <c>"0"</c> to unload immediately). Ollama-specific — the
    ///    OpenAI-compatible body accepts it as a top-level field.
    /// 4. Connection-refused detection: when <c>localhost:11434</c> isn't running, the host's
    ///    <c>IHttpClient</c> raises a transport exception that <see cref="LlmErrorMapper"/>
    ///    normalizes to a <c>NetworkException</c> with <c>ConnectionFailed</c>. The provider's
    ///    <see cref="CheckHealthAsync"/> catches this and reports
    ///    <see cref="ProviderHealthResult.Degraded(string, System.TimeSpan?, string?, string?, string?, string?)"/>
    ///    — Phase 5b adopted common's Degraded factory so the UI distinguishes
    ///    "service-not-running" (recoverable: <c>ollama serve</c>) from "service-returned-500"
    ///    (truly unhealthy). The plugin retains its vendor-specific hint source for the
    ///    "Ollama not running" learn-more URL.
    /// </para>
    /// </summary>
    public sealed class BrainarrOllamaProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "ollama";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _baseUrl;
        private readonly string? _apiKey;
        private string _model;
        private readonly BackendHealthCache _healthCache;

        public BrainarrOllamaProvider(
            IHttpClient httpClient,
            Logger logger,
            string? baseUrl = null,
            string? model = null,
            string? apiKey = null)
            : this(httpClient, logger, baseUrl, model, apiKey, BackendHealthCache.Shared) { }

        /// <summary>
        /// Constructor with an explicit health cache — used by tests to inject an isolated instance.
        /// </summary>
        internal BrainarrOllamaProvider(
            IHttpClient httpClient,
            Logger logger,
            string? baseUrl,
            string? model,
            string? apiKey,
            BackendHealthCache healthCache)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _baseUrl = (baseUrl ?? BrainarrConstants.DefaultOllamaUrl).TrimEnd('/');
            _model = string.IsNullOrWhiteSpace(model) ? BrainarrConstants.DefaultOllamaModel : model;
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            _healthCache = healthCache ?? throw new ArgumentNullException(nameof(healthCache));
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "Ollama";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            // JsonMode is supported via the OpenAI-compat `response_format` parameter and the
            // native API's `format: "json"`. We expose the flag and let the system prompt drive
            // JSON shape (matching legacy and the rest of the wave-4 fleet).
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
                // Try /v1/models first (newer Ollama). Fall back to /api/tags on 404.
                var ok = await ProbeAsync($"{_baseUrl}/v1/models", cancellationToken).ConfigureAwait(false);
                if (ok.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    sw.Stop();
                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, _apiKey != null ? "apiKey" : "none", _model);
                }

                if (ok.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var fallback = await ProbeAsync($"{_baseUrl}/api/tags", cancellationToken).ConfigureAwait(false);
                    sw.Stop();
                    if (fallback.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, _apiKey != null ? "apiKey" : "none", _model);
                    }
                    return ProviderHealthResult.Unhealthy(
                        $"HTTP {(int)fallback.StatusCode}",
                        sw.Elapsed,
                        ProviderIdConst,
                        _apiKey != null ? "apiKey" : "none",
                        _model,
                        errorCode: ((int)fallback.StatusCode).ToString());
                }

                sw.Stop();
                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)ok.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    _apiKey != null ? "apiKey" : "none",
                    _model,
                    errorCode: ((int)ok.StatusCode).ToString());
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
                    _apiKey != null ? "apiKey" : "none",
                    _model,
                    errorCode: ((int)hex.Response.StatusCode).ToString());
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Phase 5b: Connection-refused is semantically Degraded, not Unhealthy —
                // the user just hasn't run `ollama serve` yet. Reporting Degraded keeps
                // IsHealthy=true so transient failover doesn't blacklist the provider, and
                // the [Degraded] StatusMessage prefix surfaces in the UI for diagnostics.
                return ProviderHealthResult.Degraded(
                    $"Ollama not running at {SafeAuthority(_baseUrl)}: {ex.Message}",
                    sw.Elapsed,
                    ProviderIdConst,
                    _apiKey != null ? "apiKey" : "none",
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

        private async Task<HttpResponse> ProbeAsync(string url, CancellationToken cancellationToken)
        {
            var builder = new HttpRequestBuilder(url);
            if (!string.IsNullOrEmpty(_apiKey))
            {
                builder.SetHeader("Authorization", $"Bearer {_apiKey}");
            }

            var request = builder.Build();
            request.Method = HttpMethod.Get;
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);
            cancellationToken.ThrowIfCancellationRequested();
            return await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
        }

        private object BuildRequestBody(LlmRequest request)
        {
            var temp = (double?)request.Temperature ?? 0.7;
            var maxTokens = request.MaxTokens ?? 2000;
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model) ? request.Model : _model;

            // Honor `keep_alive` from ProviderOptions when present. The OpenAI-compat path
            // accepts it as a top-level field on the request body — Ollama ignores unknown
            // fields gracefully on standard chat models.
            object? keepAlive = null;
            if (request.ProviderOptions != null && request.ProviderOptions.TryGetValue("keep_alive", out var ka))
            {
                keepAlive = ka;
            }

            // Phase 5b: honor LlmRequest.JsonMode by emitting response_format
            // {"type":"json_object"}. Ollama's OpenAI-compat surface accepts this, and
            // recent Ollama builds also map it to the native API's `format: "json"` parameter
            // internally — either way the model is constrained to valid JSON output.
            object? responseFormat = request.JsonMode ? new { type = "json_object" } : null;

            return BuildOllamaPayload(modelRaw, request, temp, maxTokens, keepAlive, responseFormat);
        }

        private static object BuildOllamaPayload(
            string modelRaw,
            LlmRequest request,
            double temp,
            int maxTokens,
            object? keepAlive,
            object? responseFormat)
        {
            // Build a dictionary so we only include optional fields when present —
            // anonymous types can't be conditionally extended.
            var body = new Dictionary<string, object>
            {
                ["model"] = modelRaw,
                ["temperature"] = temp,
                ["max_tokens"] = maxTokens,
                ["stream"] = false,
            };

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                body["messages"] = new[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.Prompt },
                };
            }
            else
            {
                body["messages"] = new[] { new { role = "user", content = request.Prompt } };
            }

            if (keepAlive != null)
            {
                body["keep_alive"] = keepAlive;
            }

            if (responseFormat != null)
            {
                body["response_format"] = responseFormat;
            }

            return body;
        }

        private async Task<HttpResponse> SendAsync(object body, bool useTestTimeout, CancellationToken cancellationToken)
        {
            // Fast-fail: if the backend is known-down from a recent connection failure, skip the
            // HTTP round-trip and its retry budget entirely. Same grace window as ModelDetection.
            if (_healthCache.IsKnownDown(ProviderIdConst, _baseUrl, out var downReason))
            {
                _logger.Debug($"[HealthCache] Skipping Ollama completion — {downReason}");
                throw new NetworkException(
                    ProviderIdConst,
                    LlmErrorCode.ConnectionFailed,
                    $"Ollama backend is unreachable: {downReason}");
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
                        $"Cannot reach Ollama at {SafeAuthority(_baseUrl)}. Ensure Ollama is installed and running ('ollama serve').",
                        BrainarrConstants.DocsOllamaSection),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        $"Ollama could not find model '{_model}'. Pull it first with 'ollama pull {_model}'.",
                        BrainarrConstants.DocsOllamaSection),
                LlmErrorCode.ProviderUnavailable =>
                    new BrainarrLlmHint(
                        "Ollama reported an internal error. Check the Ollama server logs (try 'ollama logs').",
                        BrainarrConstants.DocsOllamaSection),
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
