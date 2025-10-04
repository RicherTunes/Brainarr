using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class GeminiProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_BASE_URL = BrainarrConstants.GeminiModelsBaseUrl;
        private string? _lastUserMessage;
        private string? _lastUserLearnMoreUrl;

        public string ProviderName => "Google Gemini";

        public GeminiProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Google Gemini API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model ?? BrainarrConstants.DefaultGeminiModel; // UI label; mapped on request

            _logger.Info($"Initialized Google Gemini provider with model: {_model}");
        }

        private async Task<List<Recommendation>> GetRecommendationsInternalAsync(string prompt, System.Threading.CancellationToken cancellationToken, string modelOverride = null, bool allowFallback = true)
        {
            try
            {
                var effectiveModel = modelOverride ?? _model ?? BrainarrConstants.DefaultGeminiModel;
                var artistOnly = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing.PromptShapeHelper.IsArtistOnly(prompt);
                var text = artistOnly
                    ? $@"You are a music recommendation expert. Based on the user's music library and preferences, provide artist recommendations.

IMPORTANT: Return ONLY a JSON array with NO additional text, markdown, or explanations.

Each recommendation must have these exact fields:
- artist: The artist name
- genre: The primary genre
- confidence: A number between 0 and 1
- reason: A brief reason for the recommendation

Example format:
[
  {{""artist"": ""Pink Floyd"", ""genre"": ""Progressive Rock"", ""confidence"": 0.95, ""reason"": ""Iconic progressive artists""}}
]

User request:
{prompt}"
                    : $@"You are a music recommendation expert. Based on the user's music library and preferences, provide album recommendations.

IMPORTANT: Return ONLY a JSON array with NO additional text, markdown, or explanations.

Each recommendation must have these exact fields:
- artist: The artist name
- album: The album name
- genre: The primary genre
- confidence: A number between 0 and 1
- reason: A brief reason for the recommendation

Example format:
[
  {{""artist"": ""Pink Floyd"", ""album"": ""The Dark Side of the Moon"", ""genre"": ""Progressive Rock"", ""confidence"": 0.95, ""reason"": ""Classic progressive rock masterpiece""}}
]

User request:
{prompt}";

                var temp = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.TemperaturePolicy.FromPrompt(prompt, 0.8);
                var maxOutputTokens = DetermineMaxOutputTokens(effectiveModel);

                object generationConfig;
                if (ShouldDisableThinking(effectiveModel))
                {
                    generationConfig = new
                    {
                        temperature = temp,
                        topP = 0.95,
                        topK = 40,
                        maxOutputTokens,
                        responseMimeType = "application/json",
                        thinkingConfig = new
                        {
                            thinkingBudget = 0
                        }
                    };
                }
                else
                {
                    generationConfig = new
                    {
                        temperature = temp,
                        topP = 0.95,
                        topK = 40,
                        maxOutputTokens,
                        responseMimeType = "application/json"
                    };
                }

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[] { new { text = text } }
                        }
                    },
                    generationConfig,
                    safetySettings = new[]
                    {
                        new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
                        new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
                        new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
                        new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
                    }
                };
                var modelRaw = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("gemini", effectiveModel);
                var url = $"{API_BASE_URL}/{modelRaw}:generateContent?key={_apiKey}";
                var request = new HttpRequestBuilder(url)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                var json = SecureJsonSerializer.Serialize(requestBody);
                request.SetContent(json);
                var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
                request.RequestTimeout = TimeSpan.FromSeconds(seconds);

                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var endpoint = $"{API_BASE_URL}/{modelRaw}:generateContent";
                        var snippet = json?.Length > 4000 ? (json.Substring(0, 4000) + "... [truncated]") : json;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini endpoint: {endpoint} (key hidden)");
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini request JSON: {snippet}");
                    }
                    catch { }
                }

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithHttpResilienceAsync(
                    request,
                    (req, token) => _httpClient.ExecuteAsync(req),
                    origin: $"gemini:{effectiveModel}",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    maxRetries: 2,
                    shouldRetry: null,
                    limiter: null,
                    retryBudget: null,
                    maxConcurrencyPerHost: 2,
                    perRequestTimeout: TimeSpan.FromSeconds(seconds));

                if (response == null)
                {
                    _logger.Warn($"No response from Google Gemini (model={effectiveModel})");
                    return new List<Recommendation>();
                }

                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = response.Content?.Length > 4000 ? (response.Content.Substring(0, 4000) + "... [truncated]") : response.Content;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini raw response: {snippet}");
                    }
                    catch { }
                }

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Google Gemini API error: {response.StatusCode} (model={effectiveModel})");
                    var body = response.Content ?? string.Empty;
                    if (!string.IsNullOrEmpty(body))
                    {
                        var snippet = body.Substring(0, Math.Min(body.Length, 500));
                        _logger.Debug($"Gemini API error body (truncated): {snippet}");
                        TryLogGoogleErrorGuidance(body);
                    }

                    try
                    {
                        var errorResponse = JsonConvert.DeserializeObject<GeminiError>(response.Content);
                        if (errorResponse?.Error != null)
                        {
                            _logger.Error($"Gemini error: {errorResponse.Error.Message} (Code: {errorResponse.Error.Code}, Status: {errorResponse.Error.Status})");
                        }
                    }
                    catch { }

                    if (allowFallback && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        var fallbackModel = GetFallbackModel(effectiveModel, "HTTP_404");
                        if (!string.IsNullOrWhiteSpace(fallbackModel))
                        {
                            _logger.Warn($"Gemini model {effectiveModel} unavailable on this endpoint. Falling back to {fallbackModel}.");
                            return await GetRecommendationsInternalAsync(prompt, cancellationToken, fallbackModel, allowFallback: false);
                        }
                    }

                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<GeminiResponse>(response.Content);
                var candidate = responseData?.Candidates?.FirstOrDefault();
                var part = candidate?.Content?.Parts?.FirstOrDefault();
                var content = part?.Text;

                if (string.IsNullOrWhiteSpace(content) && part?.Json != null && part.Json.Type != JTokenType.Null)
                {
                    content = part.Json.ToString(Formatting.None);
                }

                if (string.IsNullOrWhiteSpace(content) && part?.FunctionCall?.Arguments != null && part.FunctionCall.Arguments.Type != JTokenType.Null)
                {
                    content = part.FunctionCall.Arguments.ToString(Formatting.None);
                }

                var finishReason = candidate?.FinishReason ?? "UNKNOWN";
                var safetySummary = candidate?.SafetyRatings != null && candidate.SafetyRatings.Count > 0
                    ? string.Join(", ", candidate.SafetyRatings.Select(r => $"{r.Category}:{r.Probability}"))
                    : "none";

                if (DebugFlags.ProviderPayload && !string.IsNullOrEmpty(content))
                {
                    try
                    {
                        var snippet = content.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini response content: {snippet}");
                        if (responseData?.UsageMetadata != null)
                        {
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini usage: prompt={responseData.UsageMetadata.PromptTokenCount}, completion={responseData.UsageMetadata.CandidatesTokenCount}, total={responseData.UsageMetadata.TotalTokenCount}");
                        }
                    }
                    catch { }
                }

                if (DebugFlags.ProviderPayload)
                {
                    try { _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini finishReason={finishReason}, safety={safetySummary}"); } catch { }
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.Warn($"Empty response from Google Gemini (model={effectiveModel}, finishReason={finishReason}, safety={safetySummary})");

                    if (IsSafetyBlocked(finishReason, candidate?.SafetyRatings))
                    {
                        _lastUserMessage = "Google Gemini blocked the response due to safety filters.";
                        _lastUserLearnMoreUrl = BrainarrConstants.DocsGeminiSection;
                    }

                    if (allowFallback)
                    {
                        var fallbackModel = GetFallbackModel(effectiveModel, finishReason);
                        if (!string.IsNullOrWhiteSpace(fallbackModel))
                        {
                            _logger.Warn($"Retrying Gemini request with fallback model {fallbackModel}");
                            return await GetRecommendationsInternalAsync(prompt, cancellationToken, fallbackModel, allowFallback: false);
                        }
                    }

                    return new List<Recommendation>();
                }

                return RecommendationJsonParser.Parse(content, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Google Gemini");
                return new List<Recommendation>();
            }
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(NzbDrone.Core.ImportLists.Brainarr.Services.TimeoutContext.GetSecondsOrDefault(NzbDrone.Core.ImportLists.Brainarr.Configuration.BrainarrConstants.DefaultAITimeout)));
            return await GetRecommendationsInternalAsync(prompt, cts.Token);
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
            => await GetRecommendationsInternalAsync(prompt, cancellationToken);

        private static bool IsSafetyBlocked(string finishReason, List<SafetyRating> ratings)
        {
            if (!string.IsNullOrWhiteSpace(finishReason))
            {
                var trimmed = finishReason.Trim();
                if (trimmed.Equals("SAFETY", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("FINISH_REASON_SAFETY", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (ratings == null) return false;
            foreach (var rating in ratings)
            {
                if (rating == null) continue;
                var probability = rating.Probability ?? string.Empty;
                if (probability.Equals("VERY_LIKELY", StringComparison.OrdinalIgnoreCase) ||
                    probability.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string GetFallbackModel(string currentModel, string finishReason)
        {
            if (string.IsNullOrWhiteSpace(currentModel)) return null;

            var normalized = currentModel.Trim();
            var friendly = ProviderModelNormalizer.Normalize(AIProvider.Gemini, normalized);
            if (!string.IsNullOrWhiteSpace(friendly))
            {
                normalized = friendly;
            }
            var isLatestFamily = normalized.StartsWith("Gemini_25", StringComparison.OrdinalIgnoreCase) ||
                                 normalized.StartsWith("Gemini_20", StringComparison.OrdinalIgnoreCase);
            var reasonSuggestsRetry = string.IsNullOrWhiteSpace(finishReason) ||
                                       finishReason.IndexOf("safety", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       finishReason.IndexOf("blocked", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       finishReason.IndexOf("max_tokens", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       finishReason.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       finishReason.IndexOf("not_found", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isLatestFamily || !reasonSuggestsRetry)
            {
                return null;
            }

            if (!normalized.Equals("Gemini_15_Pro", StringComparison.OrdinalIgnoreCase))
            {
                return "Gemini_15_Pro";
            }

            if (!normalized.Equals("Gemini_15_Flash", StringComparison.OrdinalIgnoreCase))
            {
                return "Gemini_15_Flash";
            }

            return null;
        }

        private static bool ShouldDisableThinking(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            var normalized = ProviderModelNormalizer.Normalize(AIProvider.Gemini, modelId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = modelId.Trim();
            }

            var comparable = normalized.Replace('-', '_');

            if (comparable.StartsWith("Gemini_25_Flash", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (comparable.StartsWith("Gemini_25_Flash_Lite", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static int DetermineMaxOutputTokens(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return 2048;
            }

            var normalized = ProviderModelNormalizer.Normalize(AIProvider.Gemini, modelId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = modelId.Trim();
            }

            var comparable = normalized.Replace('-', '_');

            if (comparable.StartsWith("Gemini_25", StringComparison.OrdinalIgnoreCase))
            {
                return 4096;
            }

            if (comparable.StartsWith("Gemini_15", StringComparison.OrdinalIgnoreCase))
            {
                return 3072;
            }

            return 2048;
        }


        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _lastUserMessage = null;
                _lastUserLearnMoreUrl = null;
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = "Reply with OK" }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        maxOutputTokens = 10
                    }
                };

                // Map friendly model name to raw API id for consistency with main request path
                var modelRaw = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("gemini", _model);
                var url = $"{API_BASE_URL}/{modelRaw}:generateContent?key={_apiKey}";
                var request = new HttpRequestBuilder(url)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "gemini",
                    logger: _logger,
                    cancellationToken: cts.Token,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);

                if (response == null)
                {
                    _logger.Warn("Google Gemini connection test: No response returned");
                    return false;
                }

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                if (!success)
                {
                    // Attempt to parse a helpful Google error
                    TryLogGoogleErrorGuidance(response.Content);
                }
                _logger.Info($"Google Gemini connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");

                return success;
            }
            catch (NzbDrone.Common.Http.HttpException httpEx)
            {
                // Extract Google error details if present
                TryLogGoogleErrorGuidance(httpEx.Response?.Content);
                _logger.Error(httpEx, "Google Gemini connection test failed");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Google Gemini connection test failed");
                return false;
            }
        }

        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _lastUserMessage = null;
                _lastUserLearnMoreUrl = null;
                var endpoint = $"{BrainarrConstants.GeminiModelsBaseUrl}/{NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("gemini", _model)}:generateContent?key={_apiKey}";
                var request = new HttpRequestBuilder(endpoint)
                    .SetHeader("Content-Type", "application/json")
                    .Build();
                request.Method = HttpMethod.Post;
                var body = new { contents = new[] { new { parts = new[] { new { text = "Reply with OK" } } } } };
                request.SetContent(SecureJsonSerializer.Serialize(body));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "gemini",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);
                if (response == null)
                {
                    _logger.Warn("Google Gemini connection test (ct): No response returned");
                    return false;
                }
                var ok = response.StatusCode == System.Net.HttpStatusCode.OK;
                if (!ok)
                {
                    TryLogGoogleErrorGuidance(response.Content);
                }
                return ok;
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        // Parsing centralized in RecommendationJsonParser

        // Response models
        private class GeminiResponse
        {
            [JsonProperty("candidates")]
            public List<Candidate> Candidates { get; set; }

            [JsonProperty("usageMetadata")]
            public UsageMetadata UsageMetadata { get; set; }
        }

        private class Candidate
        {
            [JsonProperty("content")]
            public Content Content { get; set; }

            [JsonProperty("finishReason")]
            public string FinishReason { get; set; }

            [JsonProperty("safetyRatings")]
            public List<SafetyRating> SafetyRatings { get; set; }
        }

        private class Content
        {
            [JsonProperty("parts")]
            public List<Part> Parts { get; set; }

            [JsonProperty("role")]
            public string Role { get; set; }
        }

        private class Part
        {
            [JsonProperty("text")]
            public string Text { get; set; }

            [JsonProperty("json")]
            public JToken Json { get; set; }

            [JsonProperty("functionCall")]
            public FunctionCall FunctionCall { get; set; }
        }

        private class FunctionCall
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("args")]
            public JToken Arguments { get; set; }
        }

        private class SafetyRating
        {
            [JsonProperty("category")]
            public string Category { get; set; }

            [JsonProperty("probability")]
            public string Probability { get; set; }
        }

        private class UsageMetadata
        {
            [JsonProperty("promptTokenCount")]
            public int PromptTokenCount { get; set; }

            [JsonProperty("candidatesTokenCount")]
            public int CandidatesTokenCount { get; set; }

            [JsonProperty("totalTokenCount")]
            public int TotalTokenCount { get; set; }
        }

        private class GeminiError
        {
            [JsonProperty("error")]
            public ErrorDetail Error { get; set; }
        }

        private class ErrorDetail
        {
            [JsonProperty("code")]
            public int Code { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; }
        }

        /// <summary>
        /// Parses Google error JSON to provide actionable guidance (e.g. SERVICE_DISABLED with activationUrl).
        /// </summary>
        private void TryLogGoogleErrorGuidance(string? errorContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(errorContent)) return;

                // Lightweight parse to avoid tight coupling to Google's full error schema
                var root = Newtonsoft.Json.Linq.JObject.Parse(errorContent);
                var err = root["error"] as Newtonsoft.Json.Linq.JObject;
                if (err == null) return;

                var status = err.Value<string>("status");
                var message = err.Value<string>("message");
                var details = err["details"] as Newtonsoft.Json.Linq.JArray;

                string activationUrl = string.Empty;
                string consumer = string.Empty;
                if (details != null)
                {
                    foreach (var d in details.OfType<Newtonsoft.Json.Linq.JObject>())
                    {
                        var meta = d["metadata"] as Newtonsoft.Json.Linq.JObject;
                        var links = d["links"] as Newtonsoft.Json.Linq.JArray;
                        if (string.IsNullOrEmpty(activationUrl) && meta != null)
                        {
                            activationUrl = meta.Value<string>("activationUrl") ?? activationUrl;
                            consumer = meta.Value<string>("consumer") ?? consumer;
                        }
                        if (string.IsNullOrEmpty(activationUrl) && links != null)
                        {
                            foreach (var l in links.OfType<Newtonsoft.Json.Linq.JObject>())
                            {
                                var url = l.Value<string>("url");
                                if (!string.IsNullOrWhiteSpace(url) && url.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase))
                                {
                                    activationUrl = url;
                                    break;
                                }
                            }
                        }
                    }
                }

                // Provide specific guidance for disabled API
                if (string.Equals(status, "PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(message) &&
                    message.IndexOf("has not been used in project", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!string.IsNullOrEmpty(activationUrl))
                    {
                        var hint = $"Gemini API disabled for this key's Google Cloud project. Enable the Generative Language API: {activationUrl} then retry.";
                        _logger.Error(hint);
                        _lastUserMessage = hint;
                        _lastUserLearnMoreUrl = BrainarrConstants.DocsGeminiServiceDisabled;
                    }
                    else
                    {
                        var hint = "Gemini API disabled for this key. Enable the Generative Language API in your Google Cloud project, or create an AI Studio key at https://aistudio.google.com/apikey";
                        _logger.Error(hint);
                        _lastUserMessage = hint;
                        _lastUserLearnMoreUrl = BrainarrConstants.DocsGeminiSection;
                    }
                    if (!string.IsNullOrEmpty(consumer))
                    {
                        _logger.Info($"Gemini error consumer: {consumer}");
                    }
                }
            }
            catch
            {
                // Best effort only; never throw from guidance helper
            }
        }

        public void SetUserMessage(string message, string? learnMoreUrl = null)
        {
            _lastUserMessage = message;
            _lastUserLearnMoreUrl = learnMoreUrl;
        }

        public string? GetLastUserMessage() => _lastUserMessage;
        public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"Gemini model updated to: {modelName}");
            }
        }
    }
}
