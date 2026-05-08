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
    ///    <see cref="CheckHealthAsync"/> catches this and surfaces a graceful "Ollama not
    ///    running" hint via <see cref="IBrainarrLlmHintSource"/> instead of letting the
    ///    exception cascade. Flagged as a candidate for a common
    ///    <c>ProviderHealthResult.Degraded(reason)</c> shape.
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

        public BrainarrOllamaProvider(
            IHttpClient httpClient,
            Logger logger,
            string? baseUrl = null,
            string? model = null,
            string? apiKey = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _baseUrl = (baseUrl ?? BrainarrConstants.DefaultOllamaUrl).TrimEnd('/');
            _model = string.IsNullOrWhiteSpace(model) ? BrainarrConstants.DefaultOllamaModel : model;
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
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
                // Connection-refused / transport failure: degrade gracefully rather than
                // cascade. The hint source surfaces an "Ollama not running" message.
                return ProviderHealthResult.Unhealthy(
                    $"Cannot reach Ollama at {SafeAuthority(_baseUrl)}: {ex.Message}",
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

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                if (keepAlive != null)
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
                        keep_alive = keepAlive,
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

            if (keepAlive != null)
            {
                return new
                {
                    model = modelRaw,
                    messages = new[] { new { role = "user", content = request.Prompt } },
                    temperature = temp,
                    max_tokens = maxTokens,
                    stream = false,
                    keep_alive = keepAlive,
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
