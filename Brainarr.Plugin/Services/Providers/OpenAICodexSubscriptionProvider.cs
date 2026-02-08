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
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using Brainarr.Plugin.Services.Security;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// OpenAI Codex subscription provider for music recommendations.
    /// Uses OAuth tokens from ~/.codex/auth.json instead of API keys.
    /// Supports ChatGPT Plus, Team, and Pro subscriptions.
    /// </summary>
    public class OpenAICodexSubscriptionProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _credentialsPath;
        private string _apiKey;
        private string _model;
        private const string API_URL = "https://api.openai.com/v1/chat/completions";
        private string? _lastUserMessage;
        private string? _lastUserLearnMoreUrl;

        public string ProviderName => "OpenAI Codex (Subscription)";

        /// <summary>
        /// Initializes a new instance of the OpenAICodexSubscriptionProvider.
        /// </summary>
        /// <param name="httpClient">HTTP client for API communication</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="credentialsPath">Path to Codex auth file (defaults to ~/.codex/auth.json)</param>
        /// <param name="model">OpenAI model to use (defaults to gpt-4o)</param>
        public OpenAICodexSubscriptionProvider(IHttpClient httpClient, Logger logger, string? credentialsPath = null, string? model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _credentialsPath = credentialsPath ?? SubscriptionCredentialLoader.GetDefaultCodexPath();
            _model = model ?? "gpt-4o";

            // Load credentials immediately to fail fast if missing
            var result = SubscriptionCredentialLoader.LoadCodexCredentials(_credentialsPath);
            if (!result.IsSuccess)
            {
                _lastUserMessage = result.ErrorMessage;
                _logger.Warn($"OpenAI Codex credentials not loaded: {result.ErrorMessage}");
                _apiKey = string.Empty;
            }
            else
            {
                _apiKey = result.Token!;
                _logger.Info($"Initialized OpenAI Codex subscription provider with model: {_model}");
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
            var result = SubscriptionCredentialLoader.LoadCodexCredentials(_credentialsPath);
            if (!result.IsSuccess)
            {
                _lastUserMessage = result.ErrorMessage;
                _logger.Error($"OpenAI Codex credentials error: {result.ErrorMessage}");
                return new List<Recommendation>();
            }
            _apiKey = result.Token!;

            try
            {
                var artistOnly = PromptShapeHelper.IsArtistOnly(prompt);
                var systemPrompt = artistOnly
                    ? "You are a music recommendation expert. Return ONLY a JSON array of artist recommendations. Each object must have: artist, genre, confidence (0-1), reason. Do NOT include album or year fields."
                    : "You are a music recommendation expert. Return ONLY a JSON array of album recommendations. Each object must have: artist, album, genre, confidence (0-1), reason.";

                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = _model,
                    ["messages"] = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = prompt }
                    },
                    ["max_tokens"] = 2000,
                    ["temperature"] = 0.8,
                    ["response_format"] = new { type = "json_object" }
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                var json = SecureJsonSerializer.Serialize(requestBody);
                request.SetContent(json);
                request.RequestTimeout = TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout));

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "openai-codex-subscription",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"OpenAI Codex API error: {response.StatusCode}");
                    TryCaptureHint(response.Content, (int)response.StatusCode);
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<OpenAICompatibleResponse>(response.Content);
                var messageText = responseData?.Choices?.FirstOrDefault()?.Message?.Content;

                if (string.IsNullOrEmpty(messageText))
                {
                    _logger.Warn("Empty response from OpenAI Codex");
                    return new List<Recommendation>();
                }

                return RecommendationJsonParser.Parse(messageText, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from OpenAI Codex");
                return new List<Recommendation>();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout));
            return await TestConnectionAsync(cts.Token);
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
        {
            // Reload credentials
            var result = SubscriptionCredentialLoader.LoadCodexCredentials(_credentialsPath);
            if (!result.IsSuccess)
            {
                _lastUserMessage = result.ErrorMessage;
                _logger.Warn($"OpenAI Codex test failed: {result.ErrorMessage}");
                return false;
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
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "openai-codex-subscription",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: BrainarrConstants.TestConnectionTimeout,
                    maxRetries: 1);

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"OpenAI Codex connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                if (!success)
                {
                    TryCaptureHint(response.Content, (int)response.StatusCode);
                }
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OpenAI Codex connection test failed");
                if (ex is HttpException httpEx)
                {
                    TryCaptureHint(httpEx.Response?.Content, (int)(httpEx.Response?.StatusCode ?? 0));
                }
                return false;
            }
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"OpenAI Codex model updated to: {modelName}");
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

                if (status == 401 || content.IndexOf("invalid_api_key", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "OpenAI Codex authentication failed. Your subscription token may have expired. Run 'codex auth login' to refresh.";
                    _lastUserLearnMoreUrl = "https://platform.openai.com/docs/api-reference/authentication";
                }
                else if (status == 402 || content.IndexOf("insufficient_quota", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "OpenAI subscription quota exhausted. Check your subscription status.";
                }
                else if (status == 429 || content.IndexOf("rate_limit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "OpenAI Codex rate limit exceeded. Wait a moment and try again.";
                }
                else if (content.IndexOf("model_not_found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = $"Model '{_model}' not available with your subscription. Try 'gpt-4o' or 'gpt-4o-mini'.";
                }
            }
            catch (Exception) { /* Non-critical */ }
        }

    }
}
