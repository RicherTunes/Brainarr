using System;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Common.Http;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Factory interface for creating AI provider instances.
    /// Enables dependency injection and testability.
    /// </summary>
    public interface IProviderFactory
    {
        /// <summary>
        /// Creates an AI provider instance based on the specified settings.
        /// </summary>
        /// <param name="settings">Configuration settings for the provider</param>
        /// <param name="httpClient">HTTP client for API communication</param>
        /// <param name="logger">Logger instance</param>
        /// <returns>Configured AI provider instance</returns>
        IAIProvider CreateProvider(BrainarrSettings settings, IHttpClient httpClient, Logger logger);

        /// <summary>
        /// Validates if a provider is available and properly configured.
        /// </summary>
        /// <param name="providerType">Type of provider to validate</param>
        /// <param name="settings">Configuration settings</param>
        /// <returns>True if provider is available and configured</returns>
        bool IsProviderAvailable(AIProvider providerType, BrainarrSettings settings);
    }
}