using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.Providers.ClaudeCode;
using Lidarr.Plugin.Common.Subprocess;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Claude Code CLI provider that wraps Common's ClaudeCodeProvider.
    /// Uses subprocess execution instead of HTTP API.
    /// </summary>
    public class ClaudeCodeCliProvider : IAIProvider
    {
        private readonly ClaudeCodeProvider _inner;
        private readonly Logger _logger;
        private readonly BrainarrSettings _settings;
        private string? _lastUserMessage;
        private string? _lastLearnMoreUrl;

        public string ProviderName => "Claude Code (CLI)";

        public ClaudeCodeCliProvider(
            BrainarrSettings settings,
            Logger logger,
            ICliRunner? cliRunner = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Create CliRunner if not provided (DI or testing)
            var runner = cliRunner ?? new CliRunner();

            // Create detector - if explicit path provided, we'll check it directly
            var detector = new ClaudeCodeDetector(runner);

            // Create Common's provider with settings
            var claudeSettings = new ClaudeCodeSettings
            {
                Model = _settings.ClaudeCodeModelId ?? BrainarrConstants.DefaultClaudeCodeModel,
                DefaultTimeout = TimeSpan.FromSeconds(_settings.AIRequestTimeoutSeconds > 0
                    ? _settings.AIRequestTimeoutSeconds
                    : BrainarrConstants.DefaultAITimeout)
            };

            _inner = new ClaudeCodeProvider(runner, detector, claudeSettings);
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(
                    BrainarrConstants.DefaultAITimeout)));
            return await GetRecommendationsAsync(prompt, cts.Token);
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken)
        {
            try
            {
                var request = new LlmRequest
                {
                    Prompt = BuildPrompt(prompt),
                    Model = _settings.ClaudeCodeModelId
                };

                var response = await _inner.CompleteAsync(request, cancellationToken);
                return RecommendationJsonParser.Parse(response.Content, _logger);
            }
            catch (LlmProviderException ex)
            {
                _lastUserMessage = MapExceptionToUserMessage(ex);
                _lastLearnMoreUrl = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#claude-code-cli";
                _logger.Error(ex, "Claude Code CLI error: {Message}", ex.Message);
                return new List<Recommendation>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error in Claude Code CLI provider");
                return new List<Recommendation>();
            }
        }

        public async Task<ProviderHealthResult> TestConnectionAsync()
        {
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout));
            return await TestConnectionAsync(cts.Token);
        }

        public async Task<ProviderHealthResult> TestConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                // If explicit CLI path is configured, validate it exists
                if (!string.IsNullOrWhiteSpace(_settings.ClaudeCodeCliPath))
                {
                    if (!File.Exists(_settings.ClaudeCodeCliPath))
                    {
                        _settings.ClaudeCodeAuthStatus = "CLI Not Found";
                        _lastUserMessage = $"Configured CLI path not found: {_settings.ClaudeCodeCliPath}";
                        _lastLearnMoreUrl = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#claude-code-cli";
                        _logger.Warn("Claude Code CLI not found at configured path");
                        return ProviderHealthResult.Unhealthy(
                            _lastUserMessage,
                            provider: "claude-code",
                            authMethod: "cli",
                            model: _settings.ClaudeCodeModelId ?? BrainarrConstants.DefaultClaudeCodeModel,
                            errorCode: "CLI_NOT_FOUND");
                    }
                }

                var health = await _inner.CheckHealthAsync(cancellationToken);

                // Update auth status in settings for UI display
                _settings.ClaudeCodeAuthStatus = health.IsHealthy
                    ? "Authenticated"
                    : MapHealthToAuthStatus(health.StatusMessage);

                if (!health.IsHealthy)
                {
                    _lastUserMessage = health.StatusMessage;
                    _lastLearnMoreUrl = health.StatusMessage?.Contains("login") == true
                        ? "https://docs.anthropic.com/en/docs/claude-code"
                        : "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#claude-code-cli";
                }

                _logger.Info("Claude Code CLI health check: {Status} - {Message}",
                    health.IsHealthy ? "Healthy" : "Unhealthy",
                    health.StatusMessage ?? "OK");

                return health.IsHealthy
                    ? ProviderHealthResult.Healthy(
                        responseTime: TimeSpan.FromSeconds(1),
                        provider: "claude-code",
                        authMethod: "cli",
                        model: _settings.ClaudeCodeModelId ?? BrainarrConstants.DefaultClaudeCodeModel)
                    : ProviderHealthResult.Unhealthy(
                        _lastUserMessage,
                        provider: "claude-code",
                        authMethod: "cli",
                        model: _settings.ClaudeCodeModelId ?? BrainarrConstants.DefaultClaudeCodeModel,
                        errorCode: MapErrorCodeFromStatusMessage(health.StatusMessage));
            }
            catch (Exception ex)
            {
                _settings.ClaudeCodeAuthStatus = "Error";
                _lastUserMessage = $"Health check failed: {ex.Message}";
                _lastLearnMoreUrl = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#claude-code-cli";
                _logger.Error(ex, "Claude Code CLI health check failed");
                return ProviderHealthResult.Unhealthy(
                    _lastUserMessage,
                    provider: "claude-code",
                    authMethod: "cli",
                    model: _settings.ClaudeCodeModelId ?? BrainarrConstants.DefaultClaudeCodeModel,
                    errorCode: "CONNECTION_FAILED");
            }
        }

        private static string? MapErrorCodeFromStatusMessage(string? statusMessage)
        {
            if (string.IsNullOrEmpty(statusMessage)) return "UNKNOWN";

            var lower = statusMessage.ToLowerInvariant();
            if (lower.Contains("not installed") || lower.Contains("not found"))
                return "CLI_NOT_FOUND";
            if (lower.Contains("login") || lower.Contains("authentication") || lower.Contains("unauthorized"))
                return "AUTH_FAILED";
            if (lower.Contains("timeout") || lower.Contains("overload"))
                return "TIMEOUT";
            if (lower.Contains("rate") || lower.Contains("limit"))
                return "RATE_LIMITED";

            return "CONNECTION_FAILED";
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _settings.ClaudeCodeModelId = modelName;
                _logger.Info("Claude Code CLI model updated to: {Model}", modelName);
            }
        }

        public string? GetLastUserMessage() => _lastUserMessage;
        public string? GetLearnMoreUrl() => _lastLearnMoreUrl;

        private string BuildPrompt(string userPrompt)
        {
            var artistOnly = PromptShapeHelper.IsArtistOnly(userPrompt);
            return artistOnly
                ? $@"You are a music recommendation expert. Based on the user's music library and preferences, provide ARTIST recommendations.

Rules:
1. Return ONLY a JSON array of recommendations
2. Each recommendation must have these fields: artist, genre, confidence (0-1), reason
3. Do NOT include album or year fields
4. Provide diverse, high-quality recommendations
5. Focus on artists that match the user's taste but expand their horizons

User request:
{userPrompt}

Respond with only the JSON array, no other text."
                : $@"You are a music recommendation expert. Based on the user's music library and preferences, provide album recommendations.

Rules:
1. Return ONLY a JSON array of recommendations
2. Each recommendation must have these fields: artist, album, genre, confidence (0-1), reason
3. Provide diverse, high-quality recommendations
4. Focus on albums that match the user's taste but expand their horizons

User request:
{userPrompt}

Respond with only the JSON array, no other text.";
        }

        private static string MapHealthToAuthStatus(string? statusMessage)
        {
            if (string.IsNullOrEmpty(statusMessage))
                return "Unknown";

            var lower = statusMessage.ToLowerInvariant();
            if (lower.Contains("not installed") || lower.Contains("not found"))
                return "CLI Not Found";
            if (lower.Contains("login") || lower.Contains("authentication") || lower.Contains("unauthorized"))
                return "Not Authenticated";
            if (lower.Contains("timeout") || lower.Contains("overload"))
                return "Degraded";

            return "Error";
        }

        private static string? MapExceptionToUserMessage(LlmProviderException ex)
        {
            return ex.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed => "Not authenticated. Run 'claude login' in terminal to authenticate.",
                LlmErrorCode.RateLimited => "Rate limited. Wait a moment and try again.",
                LlmErrorCode.ProviderUnavailable => "Claude Code CLI not found. Install via: irm https://installer.anthropic.com/claude/install.ps1 | iex",
                LlmErrorCode.Timeout => "Request timed out. The CLI may be overloaded.",
                _ => ex.Message
            };
        }
    }
}
