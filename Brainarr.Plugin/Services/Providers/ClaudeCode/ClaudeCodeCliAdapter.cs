using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Providers.ClaudeCode;
using Lidarr.Plugin.Common.Subprocess;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.ClaudeCode
{
    /// <summary>
    /// Adapts Common's ClaudeCodeProvider infrastructure for Brainarr's IAIProvider interface.
    /// Uses CLI-based execution with NDJSON streaming support.
    /// </summary>
    public class ClaudeCodeCliAdapter : IAIProvider
    {
        private readonly ICliRunner _cliRunner;
        private readonly ClaudeCodeDetector _detector;
        private readonly ClaudeCodeCapabilities _capabilities;
        private readonly Logger _logger;
        private readonly string? _cliPath;
        private string _model;
        private string? _cachedCliPath;
        private CapabilitySet? _cachedCapabilities;
        private string? _lastUserMessage;
        private string? _lastUserLearnMoreUrl;

        public string ProviderName => "Claude Code CLI";

        /// <summary>
        /// Initializes a new instance of the ClaudeCodeCliAdapter.
        /// </summary>
        /// <param name="cliRunner">CLI runner for subprocess execution.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="cliPath">Optional explicit CLI path (auto-detected if null).</param>
        /// <param name="model">Model to use (defaults to sonnet).</param>
        public ClaudeCodeCliAdapter(
            ICliRunner cliRunner,
            Logger logger,
            string? cliPath = null,
            string? model = null)
        {
            _cliRunner = cliRunner ?? throw new ArgumentNullException(nameof(cliRunner));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cliPath = cliPath;
            _model = model ?? "sonnet";
            _detector = new ClaudeCodeDetector(cliRunner);
            _capabilities = new ClaudeCodeCapabilities(cliRunner);
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
            return await GetRecommendationsAsync(prompt, cts.Token);
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken)
        {
            _lastUserMessage = null;
            _lastUserLearnMoreUrl = null;

            try
            {
                // Find CLI
                var cliPath = await GetCliPathAsync(cancellationToken);
                if (cliPath == null)
                {
                    _lastUserMessage = "Claude Code CLI not found. Install via: irm https://installer.anthropic.com/claude/install.ps1 | iex (Windows) or curl -fsSL https://installer.anthropic.com/claude/install.sh | sh (Linux/Mac)";
                    _logger.Warn("Claude Code CLI not found");
                    return new List<Recommendation>();
                }

                // Get capabilities
                var caps = await GetCapabilitiesAsync(cliPath, cancellationToken);

                // Build system prompt for music recommendations
                var artistOnly = PromptShapeHelper.IsArtistOnly(prompt);
                var systemPrompt = BuildSystemPrompt(artistOnly);

                // Decide streaming vs buffered based on capabilities
                if (caps.SupportsStreamJson)
                {
                    return await ExecuteWithStreamingAsync(cliPath, caps, prompt, systemPrompt, cancellationToken);
                }
                else
                {
                    return await ExecuteBufferedAsync(cliPath, caps, prompt, systemPrompt, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Claude Code request cancelled");
                return new List<Recommendation>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Claude Code CLI");
                _lastUserMessage = $"CLI execution failed: {RedactSensitiveData(GetFullExceptionMessage(ex))}";
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
            _lastUserMessage = null;
            _lastUserLearnMoreUrl = null;

            try
            {
                // Step 1: Find CLI
                var cliPath = await GetCliPathAsync(cancellationToken);
                if (cliPath == null)
                {
                    _lastUserMessage = "Claude Code CLI not installed. Install via: irm https://installer.anthropic.com/claude/install.ps1 | iex (Windows) or curl -fsSL https://installer.anthropic.com/claude/install.sh | sh (Linux/Mac)";
                    _logger.Warn("Claude Code CLI not found during health check");
                    return false;
                }

                // Step 2: Verify version
                var versionResult = await _cliRunner.ExecuteAsync(
                    cliPath,
                    new[] { "--version" },
                    new CliRunnerOptions
                    {
                        Timeout = TimeSpan.FromSeconds(5),
                        ThrowOnNonZeroExitCode = false,
                    },
                    cancellationToken);

                if (!versionResult.IsSuccess)
                {
                    var sanitizedStderr = RedactSensitiveData(versionResult.StandardError);
                    _lastUserMessage = $"CLI version check failed: {sanitizedStderr}";
                    _logger.Warn($"Claude Code version check failed: {sanitizedStderr}");
                    return false;
                }

                // Step 3: Probe capabilities
                var caps = await GetCapabilitiesAsync(cliPath, cancellationToken);

                // Step 4: Verify authentication with minimal prompt
                var args = caps.BuildArguments("Reply with only the word OK");
                var authResult = await _cliRunner.ExecuteAsync(
                    cliPath,
                    args,
                    new CliRunnerOptions
                    {
                        Timeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout),
                        ThrowOnNonZeroExitCode = false,
                    },
                    cancellationToken);

                if (!authResult.IsSuccess)
                {
                    var stderr = authResult.StandardError.ToLowerInvariant();
                    if (stderr.Contains("authentication") || stderr.Contains("login") || stderr.Contains("unauthorized"))
                    {
                        _lastUserMessage = "Not authenticated. Run 'claude login' in terminal to authenticate.";
                        _lastUserLearnMoreUrl = "https://docs.anthropic.com/";
                    }
                    else
                    {
                        var sanitizedAuthError = RedactSensitiveData(authResult.StandardError);
                        _lastUserMessage = $"CLI error: {sanitizedAuthError}";
                    }
                    _logger.Warn($"Claude Code auth check failed: {RedactSensitiveData(authResult.StandardError)}");
                    return false;
                }

                _logger.Info("Claude Code CLI health check passed");
                return true;
            }
            catch (TimeoutException)
            {
                _lastUserMessage = "CLI health check timed out";
                _logger.Warn("Claude Code health check timed out");
                return false;
            }
            catch (OperationCanceledException)
            {
                _lastUserMessage = "Health check cancelled";
                _logger.Debug("Claude Code health check cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _lastUserMessage = $"Health check failed: {RedactSensitiveData(GetFullExceptionMessage(ex))}";
                _logger.Error(ex, "Claude Code health check failed");
                return false;
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

        private async Task<string?> GetCliPathAsync(CancellationToken cancellationToken)
        {
            // Use explicit path if provided
            if (!string.IsNullOrEmpty(_cliPath) && System.IO.File.Exists(_cliPath))
            {
                return _cliPath;
            }

            // Auto-detect
            if (_cachedCliPath == null)
            {
                _cachedCliPath = await _detector.FindClaudeCliAsync(cancellationToken);
            }
            return _cachedCliPath;
        }

        private async Task<CapabilitySet> GetCapabilitiesAsync(string cliPath, CancellationToken cancellationToken)
        {
            if (_cachedCapabilities == null)
            {
                _cachedCapabilities = await _capabilities.GetCapabilitiesAsync(cliPath, cancellationToken);
            }
            return _cachedCapabilities;
        }

        private string BuildSystemPrompt(bool artistOnly)
        {
            if (artistOnly)
            {
                return @"You are a music recommendation expert. Return ONLY a JSON array of artist recommendations.
Each object must have: artist (string), genre (string), confidence (0-1), reason (string).
Do NOT include album or year fields. Respond with only valid JSON, no other text.";
            }

            return @"You are a music recommendation expert. Return ONLY a JSON array of album recommendations.
Each object must have: artist (string), album (string), genre (string), confidence (0-1), reason (string).
Respond with only valid JSON, no other text.";
        }

        /// <summary>
        /// Executes with NDJSON streaming, collecting content as it arrives.
        /// </summary>
        private async Task<List<Recommendation>> ExecuteWithStreamingAsync(
            string cliPath,
            CapabilitySet caps,
            string prompt,
            string systemPrompt,
            CancellationToken cancellationToken)
        {
            var args = caps.BuildStreamingArguments(prompt, systemPrompt, _model);
            if (args == null)
            {
                // Fallback to buffered if streaming args can't be built
                return await ExecuteBufferedAsync(cliPath, caps, prompt, systemPrompt, cancellationToken);
            }

            var options = new CliRunnerOptions
            {
                Timeout = TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)),
                ThrowOnNonZeroExitCode = false,
            };

            var contentBuilder = new StringBuilder();
            var parser = new ClaudeCodeStreamParser(msg => _logger.Debug($"Stream event: {msg}"));

            await foreach (var chunk in parser.ParseAsync(
                _cliRunner.StreamAsync(cliPath, args, options, cancellationToken),
                cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.ContentDelta))
                {
                    contentBuilder.Append(chunk.ContentDelta);
                }

                if (chunk.IsComplete)
                {
                    break;
                }
            }

            var content = contentBuilder.ToString();
            if (string.IsNullOrEmpty(content))
            {
                _logger.Warn("Empty streaming response from Claude Code CLI");
                return new List<Recommendation>();
            }

            return RecommendationJsonParser.Parse(content, _logger);
        }

        /// <summary>
        /// Executes with buffered output (non-streaming).
        /// </summary>
        private async Task<List<Recommendation>> ExecuteBufferedAsync(
            string cliPath,
            CapabilitySet caps,
            string prompt,
            string systemPrompt,
            CancellationToken cancellationToken)
        {
            var args = caps.BuildArguments(prompt, systemPrompt, _model);

            var options = new CliRunnerOptions
            {
                Timeout = TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)),
                ThrowOnNonZeroExitCode = false,
            };

            var result = await _cliRunner.ExecuteAsync(cliPath, args, options, cancellationToken);

            if (!result.IsSuccess)
            {
                MapCliError(result);
                return new List<Recommendation>();
            }

            var content = ClaudeCodeResponseParser.ParseJsonResponse(result.StandardOutput);
            if (string.IsNullOrEmpty(content.Content))
            {
                _logger.Warn("Empty buffered response from Claude Code CLI");
                return new List<Recommendation>();
            }

            return RecommendationJsonParser.Parse(content.Content, _logger);
        }

        private void MapCliError(CliResult result)
        {
            var stderr = result.StandardError.ToLowerInvariant();

            if (stderr.Contains("authentication") || stderr.Contains("login") || stderr.Contains("unauthorized"))
            {
                _lastUserMessage = "Not authenticated. Run 'claude login' to authenticate.";
                _lastUserLearnMoreUrl = "https://docs.anthropic.com/";
            }
            else if (stderr.Contains("rate") || stderr.Contains("quota") || stderr.Contains("limit"))
            {
                _lastUserMessage = "Rate limited by Claude. Wait a moment and try again.";
            }
            else if (result.ExitCode == 127)
            {
                _lastUserMessage = "Claude CLI not found";
            }
            else
            {
                // Redact sensitive data before including in user message
                var sanitizedError = RedactSensitiveData(result.StandardError);
                _lastUserMessage = $"CLI error (exit {result.ExitCode}): {sanitizedError}";
            }

            // Log with redaction for debugging (user-facing message is already sanitized above)
            _logger.Warn($"Claude Code CLI error: {_lastUserMessage}");
        }

        /// <summary>
        /// Extracts full exception message including inner exceptions.
        /// </summary>
        private static string GetFullExceptionMessage(Exception ex)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            var messages = new StringBuilder();
            var current = ex;
            while (current != null)
            {
                if (messages.Length > 0)
                {
                    messages.Append(" -> ");
                }
                messages.Append(current.Message);
                current = current.InnerException;
            }
            return messages.ToString();
        }

        /// <summary>
        /// Redacts sensitive data patterns from error messages to prevent credential leakage.
        /// </summary>
        private static string RedactSensitiveData(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var result = input;

            // Redact API keys (sk-ant-*, anthropic_*)
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"(sk-ant-|anthropic_)[A-Za-z0-9_-]+",
                "***REDACTED_KEY***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Redact ANTHROPIC_API_KEY=value patterns
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"ANTHROPIC_API_KEY\s*=\s*[^\s]+",
                "ANTHROPIC_API_KEY=***REDACTED***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Redact any remaining key=value patterns that look like credentials
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"(api[_-]?key|token|secret|password|credential)\s*[=:]\s*[^\s]+",
                "$1=***REDACTED***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Redact session tokens
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"(session[_-]?token|access[_-]?token|refresh[_-]?token)\s*[=:]\s*[^\s]+",
                "$1=***REDACTED***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return result;
        }
    }
}
