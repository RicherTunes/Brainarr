using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using Brainarr.Plugin.Services.Security;
using Lidarr.Plugin.Common.Abstractions.Llm;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Claude Code subscription provider for music recommendations.
    /// Uses OAuth tokens from ~/.claude/.credentials.json instead of API keys.
    /// Supports Claude Max and other subscription plans.
    /// </summary>
    public class ClaudeCodeSubscriptionProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _credentialsPath;
        private string _apiKey;
        private string _model;
        private const string API_URL = BrainarrConstants.AnthropicMessagesUrl;
        private const string ANTHROPIC_VERSION = "2023-06-01";
        private string? _lastUserMessage;
        private string? _lastUserLearnMoreUrl;

        public string ProviderName => "Claude Code (Subscription)";

        /// <summary>
        /// Initializes a new instance of the ClaudeCodeSubscriptionProvider.
        /// </summary>
        /// <param name="httpClient">HTTP client for API communication</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="credentialsPath">Path to Claude credentials file (defaults to ~/.claude/.credentials.json)</param>
        /// <param name="model">Claude model to use (defaults to claude-sonnet-4-5-20250514)</param>
        public ClaudeCodeSubscriptionProvider(IHttpClient httpClient, Logger logger, string? credentialsPath = null, string? model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _credentialsPath = credentialsPath ?? SubscriptionCredentialLoader.GetDefaultClaudeCodePath();
            _model = model ?? "claude-sonnet-4-5-20250514";

            // Load credentials immediately to fail fast if missing
            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(_credentialsPath);
            if (!result.IsSuccess)
            {
                _lastUserMessage = result.ErrorMessage;
                _logger.Warn($"Claude Code credentials not loaded: {result.ErrorMessage}");
                _apiKey = string.Empty;
            }
            else
            {
                _apiKey = result.Token!;
                _logger.Info($"Initialized Claude Code subscription provider with model: {_model}");
            }
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
            return await GetRecommendationsAsync(prompt, cts.Token);
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken)
        {
            // Reload credentials in case they were refreshed
            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(_credentialsPath);
            if (!result.IsSuccess)
            {
                _lastUserMessage = result.ErrorMessage;
                _logger.Error($"Claude Code credentials error: {result.ErrorMessage}");
                return new List<Recommendation>();
            }
            _apiKey = result.Token!;

            try
            {
                var artistOnly = PromptShapeHelper.IsArtistOnly(prompt);
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

                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = _model,
                    ["messages"] = new[]
                    {
                        new { role = "user", content = userContent }
                    },
                    ["max_tokens"] = 2000,
                    ["temperature"] = 0.8,
                    ["system"] = "You are a knowledgeable music recommendation assistant. Always respond with valid JSON containing music recommendations."
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("x-api-key", _apiKey)
                    .SetHeader("anthropic-version", ANTHROPIC_VERSION)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                var json = SecureJsonSerializer.Serialize(requestBody);
                request.SetContent(json);
                request.RequestTimeout = TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout));

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "claude-code-subscription",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Claude Code API error: {response.StatusCode}");
                    TryCaptureHint(response.Content, (int)response.StatusCode);
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<ClaudeResponse>(response.Content);
                var messageText = responseData?.Content?.FirstOrDefault()?.Text;

                if (string.IsNullOrEmpty(messageText))
                {
                    _logger.Warn("Empty response from Claude Code");
                    return new List<Recommendation>();
                }

                return RecommendationJsonParser.Parse(messageText, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Claude Code");
                return new List<Recommendation>();
            }
        }

        public async Task<ProviderHealthResult> TestConnectionAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout));
            return await TestConnectionAsync(cts.Token);
        }

        public async Task<ProviderHealthResult> TestConnectionAsync(CancellationToken cancellationToken)
        {
            // Reload credentials
            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(_credentialsPath);
            if (!result.IsSuccess)
            {
                _lastUserMessage = result.ErrorMessage;
                _logger.Warn($"Claude Code test failed: {result.ErrorMessage}");
                return ProviderHealthResult.Unhealthy(result.ErrorMessage, provider: "claude-code", authMethod: "cli", model: _model, errorCode: "INVALID_CREDENTIALS");
            }
            _apiKey = result.Token!;

            try
            {
                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = _model,
                    ["messages"] = new[] { new { role = "user", content = "Reply with 'OK'" } },
                    ["max_tokens"] = 10
                };

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
                    origin: "claude-code-subscription",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: BrainarrConstants.TestConnectionTimeout,
                    maxRetries: 1);

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                var statusCode = (int)response.StatusCode;
                _logger.Info($"Claude Code connection test: {(success ? "Success" : $"Failed with {statusCode}")}");
                if (!success)
                {
                    TryCaptureHint(response.Content, statusCode);
                }
                return success
                    ? ProviderHealthResult.Healthy(responseTime: TimeSpan.FromSeconds(1), provider: "claude-code", authMethod: "cli", model: _model)
                    : ProviderHealthResult.Unhealthy(_lastUserMessage ?? $"Claude Code returned status {statusCode}", provider: "claude-code", authMethod: "cli", model: _model, errorCode: statusCode >= 500 ? "SERVER_ERROR" : "CONNECTION_FAILED");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Claude Code connection test failed");
                if (ex is HttpException httpEx)
                {
                    TryCaptureHint(httpEx.Response?.Content, (int)(httpEx.Response?.StatusCode ?? 0));
                }
                return ProviderHealthResult.Unhealthy(ex.Message, provider: "claude-code", authMethod: "cli", model: _model, errorCode: "CONNECTION_FAILED");
            }
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"Claude Code model updated to: {modelName}");
            }
        }

        public string? GetLastUserMessage() => _lastUserMessage;
        public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;

        private void TryCaptureHint(string? body, int status)
        {
            try
            {
                _lastUserMessage = null;
                _lastUserLearnMoreUrl = null;
                var content = body ?? string.Empty;

                if (status == 401 || content.IndexOf("authentication_error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "Claude Code authentication failed. Your subscription token may have expired. Run 'claude login' to refresh.";
                    _lastUserLearnMoreUrl = "https://docs.anthropic.com/claude/reference/getting-started-with-the-api";
                }
                else if (status == 402 || content.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "Claude subscription credits exhausted. Check your subscription status.";
                }
                else if (status == 429 || content.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "Claude Code rate limit exceeded. Wait a moment and try again.";
                }
            }
            catch (Exception) { /* Non-critical */ }
        }

        private class ClaudeResponse
        {
            [JsonProperty("content")]
            public List<ContentBlock>? Content { get; set; }
        }

        private class ContentBlock
        {
            [JsonProperty("text")]
            public string Text { get; set; } = string.Empty;
        }
    }
}
