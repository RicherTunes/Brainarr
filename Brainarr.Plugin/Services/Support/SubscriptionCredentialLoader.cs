using System;
using System.IO;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Loads subscription-based credentials from local credential files.
    /// Supports Claude Code and OpenAI Codex subscription authentication.
    /// </summary>
    public static class SubscriptionCredentialLoader
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Gets the default Claude Code credentials path for the current platform.
        /// </summary>
        public static string GetDefaultClaudeCodePath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".claude", ".credentials.json");
        }

        /// <summary>
        /// Gets the default OpenAI Codex credentials path for the current platform.
        /// </summary>
        public static string GetDefaultCodexPath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".codex", "auth.json");
        }

        /// <summary>
        /// Expands environment variables and ~ in paths.
        /// </summary>
        public static string ExpandPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            // Expand ~ to home directory
            if (path.StartsWith("~"))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                path = Path.Combine(home, path.Substring(1).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            // Expand environment variables
            return Environment.ExpandEnvironmentVariables(path);
        }

        /// <summary>
        /// Loads Claude Code OAuth credentials from the credentials file.
        /// Expected format: { "claudeAiOauth": { "accessToken": "...", "expiresAt": ... } }
        /// </summary>
        /// <param name="path">Path to credentials file (defaults to ~/.claude/.credentials.json)</param>
        /// <returns>The access token, or null if not found/invalid</returns>
        public static CredentialResult LoadClaudeCodeCredentials(string? path = null)
        {
            path = ExpandPath(path ?? GetDefaultClaudeCodePath());

            if (!File.Exists(path))
            {
                return CredentialResult.Failure(
                    $"Claude Code credentials file not found at {path}. Run 'claude login' to authenticate.");
            }

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Navigate to claudeAiOauth.accessToken
                if (root.TryGetProperty("claudeAiOauth", out var oauth))
                {
                    if (oauth.TryGetProperty("accessToken", out var tokenElement))
                    {
                        var token = tokenElement.GetString();
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            return CredentialResult.Failure(
                                "Claude Code accessToken is empty. Run 'claude login' to refresh.");
                        }

                        // Check expiration
                        if (oauth.TryGetProperty("expiresAt", out var expiresAtElement))
                        {
                            var expiresAt = expiresAtElement.GetInt64();
                            var expiresAtDate = DateTimeOffset.FromUnixTimeMilliseconds(expiresAt);
                            if (expiresAtDate < DateTimeOffset.UtcNow)
                            {
                                return CredentialResult.Failure(
                                    $"Claude Code token expired at {expiresAtDate:u}. Run 'claude login' to refresh.");
                            }

                            // Warn if expiring soon (within 24 hours)
                            var timeUntilExpiry = expiresAtDate - DateTimeOffset.UtcNow;
                            if (timeUntilExpiry < TimeSpan.FromHours(24))
                            {
                                Logger.Warn($"Claude Code token expires in {timeUntilExpiry.TotalHours:F1} hours");
                            }
                        }

                        // Extract subscription info for logging
                        var subscriptionType = "unknown";
                        if (oauth.TryGetProperty("subscriptionType", out var subType))
                        {
                            subscriptionType = subType.GetString() ?? "unknown";
                        }

                        Logger.Debug($"Loaded Claude Code credentials (subscription: {subscriptionType}, token: {token.Substring(0, Math.Min(8, token.Length))}...)");
                        return CredentialResult.Success(token);
                    }
                }

                return CredentialResult.Failure(
                    "Invalid Claude Code credentials format. Expected 'claudeAiOauth.accessToken' field.");
            }
            catch (JsonException ex)
            {
                Logger.Error(ex, $"Failed to parse Claude Code credentials from {path}");
                return CredentialResult.Failure($"Invalid JSON in credentials file: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to load Claude Code credentials from {path}");
                return CredentialResult.Failure($"Error reading credentials: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads OpenAI Codex OAuth credentials from the auth file.
        /// Expected format: { "tokens": { "access_token": "..." } }
        /// </summary>
        /// <param name="path">Path to auth file (defaults to ~/.codex/auth.json)</param>
        /// <returns>The access token, or null if not found/invalid</returns>
        public static CredentialResult LoadCodexCredentials(string? path = null)
        {
            path = ExpandPath(path ?? GetDefaultCodexPath());

            if (!File.Exists(path))
            {
                return CredentialResult.Failure(
                    $"OpenAI Codex auth file not found at {path}. Run 'codex auth login' to authenticate.");
            }

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Check for direct OPENAI_API_KEY first (fallback)
                if (root.TryGetProperty("OPENAI_API_KEY", out var apiKeyElement))
                {
                    var apiKey = apiKeyElement.GetString();
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        Logger.Debug($"Loaded OpenAI Codex API key (key: {apiKey.Substring(0, Math.Min(8, apiKey.Length))}...)");
                        return CredentialResult.Success(apiKey);
                    }
                }

                // Navigate to tokens.access_token
                if (root.TryGetProperty("tokens", out var tokens))
                {
                    if (tokens.TryGetProperty("access_token", out var tokenElement))
                    {
                        var token = tokenElement.GetString();
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            return CredentialResult.Failure(
                                "OpenAI Codex access_token is empty. Run 'codex auth login' to refresh.");
                        }

                        Logger.Debug($"Loaded OpenAI Codex credentials (token: {token.Substring(0, Math.Min(8, token.Length))}...)");
                        return CredentialResult.Success(token);
                    }
                }

                return CredentialResult.Failure(
                    "Invalid OpenAI Codex auth format. Expected 'tokens.access_token' or 'OPENAI_API_KEY' field.");
            }
            catch (JsonException ex)
            {
                Logger.Error(ex, $"Failed to parse OpenAI Codex auth from {path}");
                return CredentialResult.Failure($"Invalid JSON in auth file: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to load OpenAI Codex auth from {path}");
                return CredentialResult.Failure($"Error reading auth file: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Result of a credential loading operation.
    /// </summary>
    public class CredentialResult
    {
        public bool IsSuccess { get; }
        public string? Token { get; }
        public string? ErrorMessage { get; }

        private CredentialResult(bool isSuccess, string? token, string? errorMessage)
        {
            IsSuccess = isSuccess;
            Token = token;
            ErrorMessage = errorMessage;
        }

        public static CredentialResult Success(string token) => new(true, token, null);
        public static CredentialResult Failure(string errorMessage) => new(false, null, errorMessage);
    }
}
