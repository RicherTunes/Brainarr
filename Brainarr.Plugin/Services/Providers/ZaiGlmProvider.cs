using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// AI provider implementation for Z.AI GLM models.
    /// Supports GLM-4.7, GLM-4.6, GLM-4.5 series with OpenAI-compatible API.
    /// </summary>
    public class ZaiGlmProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience? _httpExec;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = BrainarrConstants.ZaiGlmChatCompletionsUrl;
        private readonly bool _preferStructured;

        /// <summary>
        /// Gets the display name of the provider.
        /// </summary>
        public string ProviderName => "Z.AI GLM";

        /// <summary>
        /// Initializes a new instance of the <see cref="ZaiGlmProvider"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client for making API requests.</param>
        /// <param name="logger">The logger for diagnostic output.</param>
        /// <param name="apiKey">The Z.AI API key for authentication.</param>
        /// <param name="model">The model to use (defaults to glm-4.7-flash).</param>
        /// <param name="preferStructured">Whether to prefer structured JSON output.</param>
        /// <param name="httpExec">Optional HTTP resilience handler.</param>
        public ZaiGlmProvider(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            string model = null,
            bool preferStructured = true,
            NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience? httpExec = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpExec = httpExec;

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Z.AI API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model ?? BrainarrConstants.DefaultZaiGlmModel;
            _preferStructured = preferStructured;

            _logger.Info($"Initialized Z.AI GLM provider with model: {_model}");
            if (_httpExec == null)
            {
                try { _logger.Warn("ZaiGlmProvider: IHttpResilience not injected; using static resilience fallback"); } catch (Exception) { /* Non-critical */ }
            }
        }

        private async Task<List<Recommendation>> GetRecommendationsInternalAsync(string prompt, CancellationToken cancellationToken)
        {
            try
            {
                var artistOnly = PromptShapeHelper.IsArtistOnly(prompt);
                var systemContent = artistOnly
                    ? "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, genre, confidence (0-1), and reason. Focus on diverse, high-quality artist recommendations. Do not include album or year fields."
                    : "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, album, genre, confidence (0-1), and reason. Focus on diverse, high-quality album recommendations that match the user's taste.";

                // GLM-4.7/4.6 default temperature is 1.0
                var temp = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.TemperaturePolicy.FromPrompt(prompt, 1.0);

                var modelRaw = ModelIdMapper.ToRawId("zai-glm", _model);
                var cacheKey = $"ZaiGlm:{modelRaw}";
                var preferStructuredNow = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.FormatPreferenceCache.GetPreferStructuredOrDefault(cacheKey, _preferStructured);
                var attempts = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.ChatRequestFactory.BuildBodies(
                    AIProvider.ZaiGlm,
                    modelRaw,
                    systemContent,
                    prompt,
                    temp,
                    2000,
                    preferStructured: preferStructuredNow);

                async Task<HttpResponse> SendAsync(object body, CancellationToken ct)
                {
                    var request = new HttpRequestBuilder(API_URL)
                        .SetHeader("Authorization", $"Bearer {_apiKey}")
                        .SetHeader("Content-Type", "application/json")
                        .Build();

                    request.Method = HttpMethod.Post;
                    var json = SecureJsonSerializer.Serialize(body);
                    request.SetContent(json);
                    var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
                    request.RequestTimeout = TimeSpan.FromSeconds(seconds);

                    var response = _httpExec != null
                        ? await _httpExec.SendAsync(
                            templateRequest: request,
                            send: (req, token) => _httpClient.ExecuteAsync(req),
                            origin: $"zai-glm:{modelRaw}",
                            logger: _logger,
                            cancellationToken: ct,
                            maxRetries: 3,
                            maxConcurrencyPerHost: 2,
                            retryBudget: null,
                            perRequestTimeout: TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)))
                        : await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithHttpResilienceAsync(
                            request,
                            (req, token) => _httpClient.ExecuteAsync(req),
                            origin: $"zai-glm:{modelRaw}",
                            logger: _logger,
                            cancellationToken: ct,
                            maxRetries: 3,
                            shouldRetry: resp => ShouldRetry(resp));

                    if (DebugFlags.ProviderPayload)
                    {
                        try
                        {
                            var snippet = json?.Length > 4000 ? (json.Substring(0, 4000) + "... [truncated]") : json;
                            _logger.Info($"[Brainarr Debug] Z.AI GLM endpoint: {API_URL}");
                            _logger.Info($"[Brainarr Debug] Z.AI GLM request JSON: {snippet}");
                        }
                        catch (Exception) { /* Non-critical */ }
                    }

                    return response;
                }

                HttpResponse response = null;
                var idx = 0;
                var usedIndex = -1;
                foreach (var body in attempts)
                {
                    response = await SendAsync(body, cancellationToken);
                    if (response == null) { idx++; continue; }
                    var code = (int)response.StatusCode;
                    if (response.StatusCode == HttpStatusCode.BadRequest || code == 422) { idx++; continue; }
                    usedIndex = idx;
                    break;
                }

                if (response == null)
                {
                    _logger.Error("Z.AI GLM request failed with no HTTP response");
                    return new List<Recommendation>();
                }

                NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.FormatPreferenceCache.SetPreferStructured(cacheKey, usedIndex == 0 && preferStructuredNow);

                // Check for error in response body (even on HTTP 200)
                if (HasErrorInBody(response.Content))
                {
                    var exception = MapZaiError((int)response.StatusCode, response.Content);
                    _logger.Error($"Z.AI GLM API error: {exception.Message}");
                    return new List<Recommendation>();
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Error($"Z.AI GLM API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<ZaiGlmResponse>(response.Content);

                // Check finish_reason for content filtering
                var finishReason = responseData?.Choices?.FirstOrDefault()?.FinishReason;
                if (finishReason == "sensitive")
                {
                    _logger.Warn("Z.AI GLM response was filtered due to content policy");
                    return new List<Recommendation>();
                }

                var content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;

                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from Z.AI GLM");
                    return new List<Recommendation>();
                }

                // Log token usage for cost tracking
                if (responseData?.Usage != null)
                {
                    _logger.Debug($"Z.AI GLM token usage - Prompt: {responseData.Usage.PromptTokens}, Completion: {responseData.Usage.CompletionTokens}, Total: {responseData.Usage.TotalTokens}");
                }

                return RecommendationJsonParser.Parse(content, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Z.AI GLM");
                return new List<Recommendation>();
            }
        }

        /// <summary>
        /// Gets music recommendations based on the provided prompt.
        /// </summary>
        /// <param name="prompt">The prompt describing the user's music library and preferences.</param>
        /// <returns>A list of recommended albums with metadata.</returns>
        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
            return await GetRecommendationsInternalAsync(prompt, cts.Token);
        }

        /// <summary>
        /// Gets music recommendations based on the provided prompt with cancellation support.
        /// </summary>
        /// <param name="prompt">Prompt text</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Recommendations</returns>
        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken)
            => await GetRecommendationsInternalAsync(prompt, cancellationToken);

        /// <summary>
        /// Tests the connection to the Z.AI GLM API.
        /// </summary>
        /// <returns>True if the connection is successful; otherwise, false.</returns>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with OK" }
                    },
                    max_tokens = 5
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "zai-glm",
                    logger: _logger,
                    cancellationToken: cts.Token,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);

                // Check for error in response body (even on HTTP 200)
                if (HasErrorInBody(response.Content))
                {
                    _logger.Info($"Z.AI GLM connection test: Failed with API error");
                    return false;
                }

                var success = response.StatusCode == HttpStatusCode.OK;
                _logger.Info($"Z.AI GLM connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Z.AI GLM connection test failed");
                return false;
            }
        }

        /// <summary>
        /// Tests the connection to the Z.AI GLM API with cancellation support.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the connection is successful; otherwise, false.</returns>
        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[] { new { role = "user", content = "Reply with OK" } },
                    max_tokens = 5
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "zai-glm",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);

                // Check for error in response body (even on HTTP 200)
                if (HasErrorInBody(response.Content))
                {
                    return false;
                }

                return response.StatusCode == HttpStatusCode.OK;
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        /// <summary>
        /// Updates the model used by the provider.
        /// </summary>
        /// <param name="modelName">The new model name to use.</param>
        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"Z.AI GLM model updated to: {modelName}");
            }
        }

        /// <summary>
        /// Determines if a failed response should be retried.
        /// </summary>
        private bool ShouldRetry(HttpResponse resp)
        {
            var code = (int)resp.StatusCode;

            // Don't retry auth errors
            if (code == 401 || code == 403) return false;

            // Check for content filtered (don't retry)
            if (resp.Content?.Contains("\"sensitive\"") == true) return false;

            // Check for Z.AI business error codes that shouldn't retry
            var businessCode = ParseBusinessCode(resp.Content);
            if (businessCode.HasValue)
            {
                // Don't retry auth errors (1000-1004), account issues (1110-1121)
                if (businessCode >= 1000 && businessCode <= 1004) return false;
                if (businessCode >= 1110 && businessCode <= 1121) return false;
            }

            // Retry rate limits, timeouts, server errors
            return code == 429 || code == 408 || (code >= 500 && code <= 504);
        }

        /// <summary>
        /// Checks if the response body contains an error field.
        /// </summary>
        private static bool HasErrorInBody(string? body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            return body.Contains("\"error\"") && body.Contains("\"code\"");
        }

        /// <summary>
        /// Maps Z.AI-specific error codes to normalized exceptions.
        /// </summary>
        private Exception MapZaiError(int httpCode, string? body)
        {
            var businessCode = ParseBusinessCode(body);
            var message = ParseErrorMessage(body);

            // Authentication errors (1000-1004)
            if (businessCode >= 1000 && businessCode <= 1004)
            {
                return new InvalidOperationException($"Z.AI authentication error: {message ?? "Invalid API key or token expired"}");
            }

            // Account issues (1110-1121)
            if (businessCode >= 1110 && businessCode <= 1121)
            {
                if (businessCode == 1113)
                    return new InvalidOperationException("Z.AI account has insufficient balance (quota exceeded)");
                return new InvalidOperationException($"Z.AI account issue: {message ?? "Check Z.AI dashboard"}");
            }

            // Rate limiting (1300-1309)
            if (businessCode >= 1300 && businessCode <= 1309)
            {
                return new InvalidOperationException("Z.AI rate limit exceeded - too many requests");
            }

            // API errors (1210-1234)
            if (businessCode >= 1210 && businessCode <= 1234)
            {
                if (businessCode == 1211)
                    return new InvalidOperationException("Z.AI model not found");
                if (businessCode == 1214)
                    return new InvalidOperationException("Z.AI invalid request parameters");
                return new InvalidOperationException($"Z.AI API error: {message ?? "Unknown error"}");
            }

            // Fall back to HTTP status code
            return httpCode switch
            {
                401 => new InvalidOperationException("Z.AI authentication failed - check API key"),
                403 => new InvalidOperationException("Z.AI access forbidden - check permissions"),
                429 => new InvalidOperationException("Z.AI rate limit exceeded"),
                >= 500 and <= 504 => new InvalidOperationException($"Z.AI server error ({httpCode})"),
                _ => new InvalidOperationException($"Z.AI API error: HTTP {httpCode} - {message ?? body}")
            };
        }

        /// <summary>
        /// Parses the business error code from a Z.AI error response body.
        /// </summary>
        private static int? ParseBusinessCode(string? body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            // Z.AI returns: {"error": {"code": "1234", "message": "..."}}
            var match = Regex.Match(body, @"""code""\s*:\s*""?(\d+)""?");
            return match.Success && int.TryParse(match.Groups[1].Value, out var code) ? code : null;
        }

        /// <summary>
        /// Parses the error message from a Z.AI error response body.
        /// </summary>
        private static string? ParseErrorMessage(string? body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            var match = Regex.Match(body, @"""message""\s*:\s*""([^""]+)""");
            return match.Success ? match.Groups[1].Value : null;
        }

        #region Response Models

        /// <summary>
        /// Z.AI GLM API response structure.
        /// </summary>
        private class ZaiGlmResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("request_id")]
            public string RequestId { get; set; }

            [JsonProperty("created")]
            public long Created { get; set; }

            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("choices")]
            public List<ZaiGlmChoice> Choices { get; set; }

            [JsonProperty("usage")]
            public ZaiGlmUsage Usage { get; set; }
        }

        /// <summary>
        /// Choice structure in Z.AI GLM response.
        /// </summary>
        private class ZaiGlmChoice
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public ZaiGlmMessage Message { get; set; }

            /// <summary>
            /// Finish reason: "stop", "length", "sensitive", "tool_calls", "network_error"
            /// </summary>
            [JsonProperty("finish_reason")]
            public string FinishReason { get; set; }
        }

        /// <summary>
        /// Message structure in Z.AI GLM response.
        /// </summary>
        private class ZaiGlmMessage
        {
            [JsonProperty("role")]
            public string Role { get; set; }

            [JsonProperty("content")]
            public string Content { get; set; }

            /// <summary>
            /// Chain-of-thought output from thinking mode (optional).
            /// </summary>
            [JsonProperty("reasoning_content")]
            public string ReasoningContent { get; set; }
        }

        /// <summary>
        /// Token usage statistics from Z.AI GLM response.
        /// </summary>
        private class ZaiGlmUsage
        {
            [JsonProperty("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonProperty("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonProperty("total_tokens")]
            public int TotalTokens { get; set; }
        }

        /// <summary>
        /// Z.AI error response wrapper.
        /// </summary>
        private class ZaiErrorResponse
        {
            [JsonProperty("error")]
            public ZaiError Error { get; set; }
        }

        /// <summary>
        /// Z.AI error details.
        /// </summary>
        private class ZaiError
        {
            [JsonProperty("code")]
            public string Code { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }
        }

        #endregion
    }
}
