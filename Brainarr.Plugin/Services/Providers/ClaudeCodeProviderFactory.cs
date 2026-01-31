using System;
using Lidarr.Plugin.Common.Subprocess;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.ClaudeCode;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// Factory for creating Claude Code providers.
    /// Encapsulates CLI vs HTTP provider selection logic to avoid Core layer
    /// depending on concrete ClaudeCode provider types.
    /// </summary>
    public static class ClaudeCodeProviderFactory
    {
        /// <summary>
        /// Creates the appropriate Claude Code provider based on settings.
        /// Prefers CLI-based execution when ClaudeCodeCliPath is configured.
        /// Falls back to HTTP API-based execution otherwise.
        /// </summary>
        public static IAIProvider Create(IHttpClient http, Logger logger, BrainarrSettings settings, string model)
        {
            // Try CLI-based execution first (supports NDJSON streaming)
            // Only attempt if CLI path is explicitly configured
            if (!string.IsNullOrWhiteSpace(settings.ClaudeCodeCliPath))
            {
                try
                {
                    var cliRunner = new CliRunner();
                    var cliAdapter = new ClaudeCodeCliAdapter(
                        cliRunner,
                        logger,
                        settings.ClaudeCodeCliPath,
                        model);
                    return cliAdapter;
                }
                catch (Exception ex)
                {
                    logger.Debug(ex, "CLI adapter creation failed, falling back to HTTP API provider");
                }
            }

            // Fall back to HTTP API-based execution
            return new ClaudeCodeSubscriptionProvider(http, logger,
                settings.ClaudeCodeCredentialsPath,
                model);
        }
    }
}
