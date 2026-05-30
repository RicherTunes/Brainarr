using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Lidarr.Plugin.Common.Observability;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for Google's Gemini
    /// <c>generativelanguage</c> generateContent API.
    ///
    /// <para>
    /// Wave-4a foundation provider. Uses common's <c>GeminiStreamDecoder</c> for streaming
    /// (when wired up via direct <see cref="HttpClient"/>); for the host's
    /// <c>NzbDrone.Common.Http.IHttpClient</c> path we deliver non-streaming completions in
    /// this wave.
    /// </para>
    ///
    /// <para>
    /// Gemini routes the API key as a query parameter rather than via Authorization header,
    /// and surfaces guidance for the common <c>SERVICE_DISABLED</c> permission error through
    /// <see cref="IBrainarrLlmHintSource"/>.
    /// </para>
    ///
    /// <para>
    /// JSON mode: Gemini's native JSON-mode field is <c>generationConfig.responseMimeType</c>
    /// = <c>application/json</c>. Phase 5b honors <see cref="LlmRequest.JsonMode"/> by setting
    /// the field only when the caller asks for JSON. Pre-Phase-5b Gemini always set it; the
    /// adapter now propagates JsonMode=true based on the capability flag, preserving that
    /// behavior end-to-end while allowing direct ILlmProvider callers to opt out of JSON.
    /// </para>
    /// </summary>
    public sealed class BrainarrGeminiProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "gemini";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly LlmAuthCircuit _authCircuit;
        private string _model;
        private string? _lastErrorActivationUrl;

        public BrainarrGeminiProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, authCircuit: null)
        {
        }

        public BrainarrGeminiProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, LlmAuthCircuit? authCircuit)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Google Gemini API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model ?? BrainarrConstants.DefaultGeminiModel;
            _authCircuit = authCircuit ?? new LlmAuthCircuit(logger);
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "Google Gemini";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.JsonMode
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.Vision
                  | LlmCapabilityFlags.ToolCalling,
            UsesOpenAiCompatibleApi = false,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName)) _model = modelName;
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var modelRaw = ModelIdMapper.ToRawId("gemini", _model);
            try
            {
                var probe = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[] { new { text = "Reply with OK" } },
                        },
                    },
                    generationConfig = new { maxOutputTokens = 10 },
                };

                var response = await SendAsync(probe, modelRaw, useTestTimeout: true, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, "apiKey", modelRaw);
                }

                CaptureGoogleErrorContext(response.Content);
                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)response.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    modelRaw,
                    errorCode: ((int)response.StatusCode).ToString());
            }
            catch (LlmProviderException lpe)
            {
                return ProviderHealthResult.Unhealthy(
                    lpe.Message,
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    modelRaw,
                    errorCode: lpe.ErrorCode.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ProviderHealthResult.Unhealthy(
                    ex.Message,
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    modelRaw);
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

            var modelRaw = ModelIdMapper.ToRawId("gemini",
                !string.IsNullOrWhiteSpace(request.Model) ? request.Model : _model);

            var generationConfig = new Dictionary<string, object>
            {
                ["temperature"] = (double?)request.Temperature ?? 0.8,
                ["topP"] = 0.95,
                ["topK"] = 40,
                ["maxOutputTokens"] = request.MaxTokens ?? 2048,
            };

            // Phase 5b: honor LlmRequest.JsonMode by setting Gemini's native JSON-mode
            // field only when the caller explicitly asks. The adapter sets JsonMode=true
            // based on the JsonMode capability flag, so brainarr's GetRecommendationsAsync
            // path preserves the historical "always JSON" behavior end-to-end.
            if (request.JsonMode)
            {
                generationConfig["responseMimeType"] = "application/json";
            }

            var promptText = !string.IsNullOrEmpty(request.SystemPrompt)
                ? request.SystemPrompt + "\n\n" + request.Prompt
                : request.Prompt;

            var body = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = promptText } },
                    },
                },
                generationConfig,
                safetySettings = new[]
                {
                    new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
                    new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
                    new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
                    new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" },
                },
            };

            LlmResponse result;
            try
            {
                var response = await SendAsync(body, modelRaw, useTestTimeout: false, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    CaptureGoogleErrorContext(response.Content);
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
            // Streaming requires a direct HttpClient pipeline. Common's GeminiStreamDecoder
            // is ready to consume the stream once the host-IHttpClient → HttpClient bridge
            // lands; until then the Streaming flag is omitted from Capabilities so callers
            // skip this path correctly.
            return null;
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private async Task<HttpResponse> SendAsync(object body, string modelRaw, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var url = $"{BrainarrConstants.GeminiModelsBaseUrl}/{modelRaw}:generateContent?key={_apiKey}";
            var request = new HttpRequestBuilder(url)
                .SetHeader("Content-Type", "application/json")
                .Build();

            request.Method = HttpMethod.Post;
            request.SetContent(JsonConvert.SerializeObject(body));

            // SECURITY (HIGH): Gemini carries the API key in the URL query (?key=...). The host
            // IHttpClient throws an HttpException on non-2xx whose Message embeds the full request
            // URL — and that exception would be chained as InnerException of the mapped
            // LlmProviderException and rendered UNREDACTED by NLog's exception renderer, leaking the
            // key into logs on every failed request. Suppressing the host throw routes all non-2xx
            // responses through the status-code branches in CompleteAsync/CheckHealthAsync, which map
            // with inner:null (no URL-bearing exception is ever constructed). Do NOT remove this.
            request.SuppressHttpError = true;

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
                CaptureGoogleErrorContext(hex.Response.Content);
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
                var parsed = JsonConvert.DeserializeObject<GeminiResponseDto>(content);
                var candidate = parsed?.Candidates?.FirstOrDefault();
                var part = candidate?.Content?.Parts?.FirstOrDefault();

                var text = part?.Text;
                if (string.IsNullOrWhiteSpace(text) && part?.Json != null && part.Json.Type != JTokenType.Null)
                {
                    text = part.Json.ToString(Formatting.None);
                }
                if (string.IsNullOrWhiteSpace(text) && part?.FunctionCall?.Arguments != null && part.FunctionCall.Arguments.Type != JTokenType.Null)
                {
                    text = part.FunctionCall.Arguments.ToString(Formatting.None);
                }

                return new LlmResponse
                {
                    Content = text ?? string.Empty,
                    FinishReason = candidate?.FinishReason,
                    Usage = parsed?.UsageMetadata != null
                        ? new LlmUsage
                        {
                            InputTokens = parsed.UsageMetadata.PromptTokenCount,
                            OutputTokens = parsed.UsageMetadata.CandidatesTokenCount,
                        }
                        : null,
                };
            }
            catch
            {
                return new LlmResponse { Content = content };
            }
        }

        private void CaptureGoogleErrorContext(string? errorContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(errorContent)) return;

                var root = JObject.Parse(errorContent);
                var err = root["error"] as JObject;
                if (err == null) return;

                var details = err["details"] as JArray;
                if (details == null) return;

                foreach (var d in details.OfType<JObject>())
                {
                    var meta = d["metadata"] as JObject;
                    if (meta != null)
                    {
                        var url = meta.Value<string>("activationUrl");
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            _lastErrorActivationUrl = url;
                            break;
                        }
                    }

                    var links = d["links"] as JArray;
                    if (links != null)
                    {
                        foreach (var l in links.OfType<JObject>())
                        {
                            var url = l.Value<string>("url");
                            if (!string.IsNullOrWhiteSpace(url) && url.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase))
                            {
                                _lastErrorActivationUrl = url;
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // best effort
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
                LlmErrorCode.AuthorizationFailed when !string.IsNullOrEmpty(_lastErrorActivationUrl) =>
                    new BrainarrLlmHint(
                        $"Gemini API disabled for this key's Google Cloud project. Enable the Generative Language API: {_lastErrorActivationUrl} then retry.",
                        BrainarrConstants.DocsGeminiServiceDisabled),
                LlmErrorCode.AuthorizationFailed =>
                    new BrainarrLlmHint(
                        "Gemini API disabled for this key. Enable the Generative Language API in your Google Cloud project, or create an AI Studio key at https://aistudio.google.com/apikey",
                        BrainarrConstants.DocsGeminiSection),
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid Google Gemini API key. Verify the key at https://aistudio.google.com/apikey.",
                        BrainarrConstants.DocsGeminiSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "Google Gemini rate limit exceeded. Wait a minute and retry, or switch to a Flash model.",
                        BrainarrConstants.DocsGeminiSection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class GeminiResponseDto
        {
            [JsonProperty("candidates")]
            public List<GeminiCandidateDto>? Candidates { get; set; }

            [JsonProperty("usageMetadata")]
            public GeminiUsageDto? UsageMetadata { get; set; }
        }

        private sealed class GeminiCandidateDto
        {
            [JsonProperty("content")]
            public GeminiContentDto? Content { get; set; }

            [JsonProperty("finishReason")]
            public string? FinishReason { get; set; }
        }

        private sealed class GeminiContentDto
        {
            [JsonProperty("parts")]
            public List<GeminiPartDto>? Parts { get; set; }
        }

        private sealed class GeminiPartDto
        {
            [JsonProperty("text")]
            public string? Text { get; set; }

            [JsonProperty("json")]
            public JToken? Json { get; set; }

            [JsonProperty("functionCall")]
            public GeminiFunctionCallDto? FunctionCall { get; set; }
        }

        private sealed class GeminiFunctionCallDto
        {
            [JsonProperty("args")]
            public JToken? Arguments { get; set; }
        }

        private sealed class GeminiUsageDto
        {
            [JsonProperty("promptTokenCount")]
            public int PromptTokenCount { get; set; }

            [JsonProperty("candidatesTokenCount")]
            public int CandidatesTokenCount { get; set; }

            [JsonProperty("totalTokenCount")]
            public int TotalTokenCount { get; set; }
        }
    }
}
