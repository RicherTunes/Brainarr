using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Background service that monitors subscription credentials and attempts auto-renewal
    /// when tokens are about to expire. Supports both Claude Code and OpenAI Codex credentials.
    /// </summary>
    public class CredentialRefreshService : IDisposable
    {
        private readonly Logger _logger;
        private readonly Timer _refreshTimer;
        private readonly string _claudeCodePath;
        private readonly string _openAICodexPath;
        private readonly TimeSpan _refreshThreshold;
        private readonly TimeSpan _checkInterval;
        private bool _disposed;

        /// <summary>
        /// Event raised when credentials are refreshed successfully.
        /// </summary>
        public event EventHandler<CredentialRefreshEventArgs>? CredentialsRefreshed;

        /// <summary>
        /// Event raised when credential refresh fails and manual intervention is needed.
        /// </summary>
        public event EventHandler<CredentialRefreshEventArgs>? RefreshFailed;

        /// <summary>
        /// Initializes the credential refresh service.
        /// </summary>
        /// <param name="claudeCodePath">Path to Claude Code credentials (null for default)</param>
        /// <param name="openAICodexPath">Path to OpenAI Codex credentials (null for default)</param>
        /// <param name="refreshThreshold">Time before expiry to attempt refresh (default: 2 hours)</param>
        /// <param name="checkInterval">How often to check credentials (default: 30 minutes)</param>
        public CredentialRefreshService(
            string? claudeCodePath = null,
            string? openAICodexPath = null,
            TimeSpan? refreshThreshold = null,
            TimeSpan? checkInterval = null)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _claudeCodePath = SubscriptionCredentialLoader.ExpandPath(
                claudeCodePath ?? SubscriptionCredentialLoader.GetDefaultClaudeCodePath());
            _openAICodexPath = SubscriptionCredentialLoader.ExpandPath(
                openAICodexPath ?? SubscriptionCredentialLoader.GetDefaultCodexPath());
            _refreshThreshold = refreshThreshold ?? TimeSpan.FromHours(2);
            _checkInterval = checkInterval ?? TimeSpan.FromMinutes(30);

            _refreshTimer = new Timer(CheckAndRefreshCredentials, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Starts the background credential monitoring.
        /// </summary>
        public void Start()
        {
            _logger.Info("Starting credential refresh service");
            _refreshTimer.Change(TimeSpan.Zero, _checkInterval);
        }

        /// <summary>
        /// Stops the background credential monitoring.
        /// </summary>
        public void Stop()
        {
            _logger.Info("Stopping credential refresh service");
            _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void CheckAndRefreshCredentials(object? state)
        {
            try
            {
                CheckClaudeCodeCredentials();
                CheckOpenAICodexCredentials();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during credential refresh check");
            }
        }

        private void CheckClaudeCodeCredentials()
        {
            if (!File.Exists(_claudeCodePath))
                return;

            try
            {
                var json = File.ReadAllText(_claudeCodePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("claudeAiOauth", out var oauth))
                    return;

                if (!oauth.TryGetProperty("expiresAt", out var expiresAtElement))
                    return;

                var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiresAtElement.GetInt64());
                var timeUntilExpiry = expiresAt - DateTimeOffset.UtcNow;

                if (timeUntilExpiry <= TimeSpan.Zero)
                {
                    _logger.Warn("Claude Code token has expired. Manual re-authentication required.");
                    OnRefreshFailed("ClaudeCode", "Token has expired. Run 'claude login' to re-authenticate.");
                    return;
                }

                if (timeUntilExpiry <= _refreshThreshold)
                {
                    _logger.Info($"Claude Code token expires in {timeUntilExpiry.TotalMinutes:F0} minutes. Attempting refresh...");
                    AttemptClaudeCodeRefresh(oauth);
                }
                else
                {
                    _logger.Debug($"Claude Code token valid for {timeUntilExpiry.TotalHours:F1} hours");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking Claude Code credentials");
            }
        }

        private void AttemptClaudeCodeRefresh(JsonElement oauth)
        {
            // Check for refresh token
            if (oauth.TryGetProperty("refreshToken", out var refreshTokenElement))
            {
                var refreshToken = refreshTokenElement.GetString();
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    // Try OAuth2 refresh flow
                    if (TryOAuth2Refresh("ClaudeCode", refreshToken))
                    {
                        OnCredentialsRefreshed("ClaudeCode", "Token refreshed successfully via OAuth2");
                        return;
                    }
                }
            }

            // Fall back to CLI refresh
            if (TryCliRefresh("claude", "--refresh"))
            {
                OnCredentialsRefreshed("ClaudeCode", "Token refreshed successfully via CLI");
                return;
            }

            // If all refresh attempts fail, notify user
            OnRefreshFailed("ClaudeCode", "Auto-refresh failed. Run 'claude login' to re-authenticate.");
        }

        private void CheckOpenAICodexCredentials()
        {
            if (!File.Exists(_openAICodexPath))
                return;

            try
            {
                var json = File.ReadAllText(_openAICodexPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // OpenAI Codex may store expiration differently
                // Check tokens.expires_at or tokens.expires_in
                if (!root.TryGetProperty("tokens", out var tokens))
                    return;

                DateTimeOffset? expiresAt = null;

                if (tokens.TryGetProperty("expires_at", out var expiresAtElement))
                {
                    if (expiresAtElement.ValueKind == JsonValueKind.Number)
                    {
                        expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtElement.GetInt64());
                    }
                }

                if (expiresAt == null)
                {
                    // No expiration info, assume valid
                    _logger.Debug("OpenAI Codex credentials have no expiration info");
                    return;
                }

                var timeUntilExpiry = expiresAt.Value - DateTimeOffset.UtcNow;

                if (timeUntilExpiry <= TimeSpan.Zero)
                {
                    _logger.Warn("OpenAI Codex token has expired. Manual re-authentication required.");
                    OnRefreshFailed("OpenAICodex", "Token has expired. Run 'codex auth login' to re-authenticate.");
                    return;
                }

                if (timeUntilExpiry <= _refreshThreshold)
                {
                    _logger.Info($"OpenAI Codex token expires in {timeUntilExpiry.TotalMinutes:F0} minutes. Attempting refresh...");
                    AttemptOpenAICodexRefresh(tokens);
                }
                else
                {
                    _logger.Debug($"OpenAI Codex token valid for {timeUntilExpiry.TotalHours:F1} hours");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking OpenAI Codex credentials");
            }
        }

        private void AttemptOpenAICodexRefresh(JsonElement tokens)
        {
            // Check for refresh token
            if (tokens.TryGetProperty("refresh_token", out var refreshTokenElement))
            {
                var refreshToken = refreshTokenElement.GetString();
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    // Try OAuth2 refresh flow
                    if (TryOAuth2Refresh("OpenAICodex", refreshToken))
                    {
                        OnCredentialsRefreshed("OpenAICodex", "Token refreshed successfully via OAuth2");
                        return;
                    }
                }
            }

            // Fall back to CLI refresh
            if (TryCliRefresh("codex", "auth refresh"))
            {
                OnCredentialsRefreshed("OpenAICodex", "Token refreshed successfully via CLI");
                return;
            }

            // If all refresh attempts fail, notify user
            OnRefreshFailed("OpenAICodex", "Auto-refresh failed. Run 'codex auth login' to re-authenticate.");
        }

        private bool TryOAuth2Refresh(string provider, string refreshToken)
        {
            // OAuth2 refresh would require hitting the provider's token endpoint
            // This is complex as we'd need client IDs and secrets
            // For now, return false and rely on CLI refresh
            _logger.Debug($"{provider}: OAuth2 refresh not implemented, falling back to CLI");
            return false;
        }

        private bool TryCliRefresh(string command, string arguments)
        {
            try
            {
                _logger.Debug($"Attempting CLI refresh: {command} {arguments}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    _logger.Warn($"Failed to start {command} process");
                    return false;
                }

                process.WaitForExit(30000); // 30 second timeout

                if (process.ExitCode == 0)
                {
                    _logger.Info($"CLI refresh succeeded: {command} {arguments}");
                    return true;
                }
                else
                {
                    var error = process.StandardError.ReadToEnd();
                    _logger.Warn($"CLI refresh failed with exit code {process.ExitCode}: {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"CLI refresh exception for {command}");
                return false;
            }
        }

        private void OnCredentialsRefreshed(string provider, string message)
        {
            _logger.Info($"[{provider}] {message}");
            CredentialsRefreshed?.Invoke(this, new CredentialRefreshEventArgs(provider, message, true));
        }

        private void OnRefreshFailed(string provider, string message)
        {
            _logger.Warn($"[{provider}] {message}");
            RefreshFailed?.Invoke(this, new CredentialRefreshEventArgs(provider, message, false));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _refreshTimer.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Event args for credential refresh events.
    /// </summary>
    public class CredentialRefreshEventArgs : EventArgs
    {
        public string Provider { get; }
        public string Message { get; }
        public bool Success { get; }

        public CredentialRefreshEventArgs(string provider, string message, bool success)
        {
            Provider = provider;
            Message = message;
            Success = success;
        }
    }
}
