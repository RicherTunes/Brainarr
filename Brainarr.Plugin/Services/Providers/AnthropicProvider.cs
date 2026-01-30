using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Anthropic provider implementation for music recommendations using Claude models.
    /// Supports Claude 3.5 Sonnet, Claude 3.5 Haiku, Claude 3 Opus, and other Claude models.
    /// </summary>
    /// <remarks>
    /// This provider requires an Anthropic API key from https://console.anthropic.com/
    /// Claude models excel at nuanced understanding and reasoning, making them ideal
    /// for complex music recommendation scenarios requiring cultural context or genre analysis.
    /// </remarks>
    public class AnthropicProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private bool _enableThinking;
        private int? _thinkingBudgetTokens;
        private const string API_URL = BrainarrConstants.AnthropicMessagesUrl;
        private const string ANTHROPIC_VERSION = "2023-06-01";
        private string? _lastUserMessage;
        private string? _lastUserLearnMoreUrl;

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public string ProviderName => "Anthropic";

        /// <summary>
        /// Initializes a new instance of the AnthropicProvider class.
        /// </summary>
        /// <param name="httpClient">HTTP client for API communication</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="apiKey">Anthropic API key (required)</param>
        /// <param name="model">Claude model to use (defaults to claude-3-5-haiku-latest for cost efficiency)</param>
        /// <param name="validator">Optional recommendation validator</param>
        /// <exception cref="ArgumentNullException">Thrown when httpClient or logger is null</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is null or empty</exception>
        public AnthropicProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Anthropic API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model ?? BrainarrConstants.DefaultAnthropicModel; // UI label; mapped on request
            if (_model.Contains("#thinking", StringComparison.Ordinal))
            {
                _enableThinking = true;
                // Parse optional budget tokens: #thinking(tokens=8000) or #thinking(8000)
                try
                {
                    var start = _model.IndexOf("#thinking", StringComparison.Ordinal);
                    var open = _model.IndexOf('(', start);
                    var close = open > 0 ? _model.IndexOf(')', open + 1) : -1;
                    if (open > 0 && close > open)
                    {
                        var inside = _model.Substring(open + 1, close - open - 1).Trim();
                        if (inside.StartsWith("tokens=", StringComparison.OrdinalIgnoreCase))
                        {
                            inside = inside.Substring(7).Trim();
                        }
                        if (int.TryParse(inside, out var budget) && budget > 0)
                        {
                            _thinkingBudgetTokens = budget;
                        }
                    }
                }
                catch (Exception) { /* Non-critical */ }
                _model = _model.Replace("#thinking", string.Empty, StringComparison.Ordinal);
                _model = System.Text.RegularExpressions.Regex.Replace(_model, "\\(tokens=\\d+\\)", string.Empty);
                _model = System.Text.RegularExpressions.Regex.Replace(_model, "\\(\\d+\\)", string.Empty);
                _model = _model.Trim();
            }

            _logger.Info($"Initialized Anthropic provider with model: {_model}");
        }

        /// <summary>
        /// Gets music recommendations from Anthropic Claude based on the provided prompt.
        /// </summary>
        /// <param name="prompt">The prompt containing user's music library and preferences</param>
        /// <returns>List of music recommendations with confidence scores and reasoning</returns>
        /// <remarks>
        /// Uses the Messages API with Claude's advanced reasoning capabilities.
        /// Claude excels at understanding nuanced music preferences and cultural context.
        /// The provider implements automatic retry logic and comprehensive error handling.
        /// </remarks>
        private async Task<List<Recommendation>> GetRecommendationsInternalAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                var artistOnly = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing.PromptShapeHelper.IsArtistOnly(prompt);
                var userContent = artistOnly
                    ? $@"You are a music recommendation expert. Based on the user's music library and preferences, provide ARTIST recommendations.

Rules:
1. Return ONLY a JSON array of recommendations
2. Each recommendation must have these fields: artist, genre, confidence (0-1), reason
3. Do NOT include album or year fields
4. Provide diverse, high-quality recommendations
5. Focus on artists that match the user's taste but expand their horizons

User request:
{prompt}

Respond with only the JSON array, no other text."
                    : $@"You are a music recommendation expert. Based on the user's music library and preferences, provide album recommendations.

Rules:
1. Return ONLY a JSON array of recommendations
2. Each recommendation must have these fields: artist, album, genre, confidence (0-1), reason
3. Provide diverse, high-quality recommendations
4. Focus on albums that match the user's taste but expand their horizons

User request:
{prompt}

Respond with only the JSON array, no other text.";

                var modelRaw = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("anthropic", _model);
                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = modelRaw,
                    ["messages"] = new[]
                    {
                        new { role = "user", content = userContent }
                    },
                    ["max_tokens"] = 2000,
                    ["temperature"] = 0.8,
                    ["system"] = "You are a knowledgeable music recommendation assistant. Always respond with valid JSON containing music recommendations."
                };
                if (_enableThinking)
                {
                    var think = new Dictionary<string, object> { ["type"] = "auto" };
                    if (_thinkingBudgetTokens.HasValue && _thinkingBudgetTokens.Value > 0)
                    {
                        think["budget_tokens"] = _thinkingBudgetTokens.Value;
                    }
                    requestBody["thinking"] = think;
                }

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("x-api-key", _apiKey)
                    .SetHeader("anthropic-version", ANTHROPIC_VERSION)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                var json = SecureJsonSerializer.Serialize(requestBody);
                request.SetContent(json);
                var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
                request.RequestTimeout = TimeSpan.FromSeconds(seconds);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "anthropic",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);

                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = json?.Length > 4000 ? (json.Substring(0, 4000) + "... [truncated]") : json;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Anthropic endpoint: {API_URL}");
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Anthropic request JSON: {snippet}");
                    }
                    catch (Exception) { /* Non-critical */ }
                }

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Anthropic API error: {response.StatusCode}");
                    var errorContent = response.Content ?? string.Empty;
                    if (!string.IsNullOrEmpty(errorContent))
                    {
                        var snippet = RedactSensitiveData(errorContent.Substring(0, Math.Min(errorContent.Length, 500)));
                        _logger.Debug($"Anthropic API error body (truncated): {snippet}");
                    }
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<AnthropicResponse>(response.Content);
                var messageText = responseData?.Content?.FirstOrDefault()?.Text;

                if (string.IsNullOrEmpty(messageText))
                {
                    _logger.Warn("Empty response from Anthropic");
                    return new List<Recommendation>();
                }
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = messageText?.Length > 4000 ? (messageText.Substring(0, 4000) + "... [truncated]") : messageText;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Anthropic response content: {snippet}");
                        if (responseData?.Usage != null)
                        {
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Anthropic usage: prompt={responseData.Usage.InputTokens}, completion={responseData.Usage.OutputTokens}");
                        }
                    }
                    catch (Exception) { /* Non-critical */ }
                }

                return RecommendationJsonParser.Parse(messageText, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Anthropic");
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



        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var modelRaw2 = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("anthropic", _model);
                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = modelRaw2,
                    ["messages"] = new[] { new { role = "user", content = "Reply with 'OK'" } },
                    ["max_tokens"] = 10
                };
                if (_enableThinking)
                {
                    var think = new Dictionary<string, object> { ["type"] = "auto" };
                    if (_thinkingBudgetTokens.HasValue && _thinkingBudgetTokens.Value > 0)
                    {
                        think["budget_tokens"] = _thinkingBudgetTokens.Value;
                    }
                    requestBody["thinking"] = think;
                }

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("x-api-key", _apiKey)
                    .SetHeader("anthropic-version", ANTHROPIC_VERSION)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "anthropic",
                    logger: _logger,
                    cancellationToken: cts.Token,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"Anthropic connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                if (!success)
                {
                    TryCaptureAnthropicHint(response.Content, (int)response.StatusCode);
                }
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Anthropic connection test failed");
                if (ex is NzbDrone.Common.Http.HttpException httpEx)
                {
                    var sc = httpEx.Response != null ? (int)httpEx.Response.StatusCode : 0;
                    TryCaptureAnthropicHint(httpEx.Response?.Content, sc);
                }
                return false;
            }
        }

        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var modelRaw2 = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("anthropic", _model);
                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = modelRaw2,
                    ["messages"] = new[] { new { role = "user", content = "Reply with 'OK'" } },
                    ["max_tokens"] = 10
                };
                if (_enableThinking)
                {
                    var think = new Dictionary<string, object> { ["type"] = "auto" };
                    if (_thinkingBudgetTokens.HasValue && _thinkingBudgetTokens.Value > 0)
                    {
                        think["budget_tokens"] = _thinkingBudgetTokens.Value;
                    }
                    requestBody["thinking"] = think;
                }

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("x-api-key", _apiKey)
                    .SetHeader("anthropic-version", ANTHROPIC_VERSION)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "anthropic",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);
                var ok = response.StatusCode == System.Net.HttpStatusCode.OK;
                if (!ok)
                {
                    TryCaptureAnthropicHint(response.Content, (int)response.StatusCode);
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
        private class AnthropicResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; } = string.Empty;

            [JsonProperty("type")]
            public string Type { get; set; } = string.Empty;

            [JsonProperty("role")]
            public string Role { get; set; }

            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("content")]
            public List<ContentBlock> Content { get; set; }

            [JsonProperty("stop_reason")]
            public string StopReason { get; set; }

            [JsonProperty("stop_sequence")]
            public string StopSequence { get; set; }

            [JsonProperty("usage")]
            public Usage Usage { get; set; }
        }

        private class ContentBlock
        {
            [JsonProperty("type")]
            public string Type { get; set; } = string.Empty;

            [JsonProperty("text")]
            public string Text { get; set; } = string.Empty;
        }

        private class Usage
        {
            [JsonProperty("input_tokens")]
            public int InputTokens { get; set; }

            [JsonProperty("output_tokens")]
            public int OutputTokens { get; set; }
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _enableThinking = false;
                _thinkingBudgetTokens = null;
                if (_model.Contains("#thinking", StringComparison.Ordinal))
                {
                    _enableThinking = true;
                    try
                    {
                        var start = _model.IndexOf("#thinking", StringComparison.Ordinal);
                        var open = _model.IndexOf('(', start);
                        var close = open > 0 ? _model.IndexOf(')', open + 1) : -1;
                        if (open > 0 && close > open)
                        {
                            var inside = _model.Substring(open + 1, close - open - 1).Trim();
                            if (inside.StartsWith("tokens=", StringComparison.OrdinalIgnoreCase))
                            {
                                inside = inside.Substring(7).Trim();
                            }
                            if (int.TryParse(inside, out var budget) && budget > 0)
                            {
                                _thinkingBudgetTokens = budget;
                            }
                        }
                    }
                    catch (Exception) { /* Non-critical */ }
                    _model = _model.Replace("#thinking", string.Empty, StringComparison.Ordinal);
                    _model = System.Text.RegularExpressions.Regex.Replace(_model, "\\(tokens=\\d+\\)", string.Empty);
                    _model = System.Text.RegularExpressions.Regex.Replace(_model, "\\(\\d+\\)", string.Empty);
                    _model = _model.Trim();
                }
                _logger.Info($"Anthropic model updated to: {modelName}");
            }
        }

        public string? GetLastUserMessage() => _lastUserMessage;
        public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;

        private void TryCaptureAnthropicHint(string? body, int status)
        {
            try
            {
                _lastUserMessage = null;
                _lastUserLearnMoreUrl = null;
                var content = body ?? string.Empty;
                if (status == 401 || content.IndexOf("authentication_error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "Invalid Anthropic API key or authentication error. Recreate a key at https://console.anthropic.com and ensure it has API access.";
                    _lastUserLearnMoreUrl = BrainarrConstants.DocsAnthropicSection;
                }
                else if (status == 402 || content.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0 || content.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "Anthropic credits/quota exhausted. Add a payment method or reduce usage: https://console.anthropic.com";
                    _lastUserLearnMoreUrl = BrainarrConstants.DocsAnthropicCreditLimit;
                }
                else if (status == 429 || content.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "Anthropic rate limit exceeded. Wait a minute and retry, or switch to Haiku for lower cost.";
                    _lastUserLearnMoreUrl = BrainarrConstants.DocsAnthropicSection;
                }
                else if (content.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0 && content.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "Anthropic model access denied. Choose a supported model or request access in your Anthropic console.";
                    _lastUserLearnMoreUrl = BrainarrConstants.DocsAnthropicSection;
                }
            }
            catch (Exception) { /* Non-critical */ }
        }

        private static string RedactSensitiveData(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            var result = input;

            // Redact Anthropic API keys (sk-ant-*)
            result = Regex.Replace(result, @"sk-ant-[A-Za-z0-9_-]{5,}", "***REDACTED_KEY***", RegexOptions.IgnoreCase);

            // Redact generic sk- keys
            result = Regex.Replace(result, @"sk-[A-Za-z0-9_-]{10,}", "***REDACTED_KEY***", RegexOptions.IgnoreCase);

            // Redact Bearer tokens
            result = Regex.Replace(result, @"Bearer\s+[A-Za-z0-9_.-]+", "Bearer ***REDACTED***", RegexOptions.IgnoreCase);

            // Redact generic API key patterns
            result = Regex.Replace(result, @"(api[_-]?key|token|secret|password|credential)\s*[=:]\s*[^\s""',}]+", "$1=***REDACTED***", RegexOptions.IgnoreCase);

            return result;
        }
    }
}
