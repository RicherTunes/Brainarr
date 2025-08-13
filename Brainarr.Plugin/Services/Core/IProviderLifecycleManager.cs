using System;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Manages the lifecycle of AI providers including initialization, health monitoring, and disposal
    /// </summary>
    public interface IProviderLifecycleManager : IDisposable
    {
        /// <summary>
        /// Initializes a provider based on the given settings
        /// </summary>
        /// <param name="settings">The Brainarr settings containing provider configuration</param>
        /// <returns>True if initialization succeeded, false otherwise</returns>
        Task<bool> InitializeProviderAsync(BrainarrSettings settings);

        /// <summary>
        /// Gets the currently initialized provider
        /// </summary>
        /// <returns>The active AI provider or null if not initialized</returns>
        IAIProvider GetProvider();

        /// <summary>
        /// Checks if the current provider is healthy and operational
        /// </summary>
        /// <returns>True if the provider is healthy, false otherwise</returns>
        Task<bool> IsProviderHealthyAsync();

        /// <summary>
        /// Performs auto-detection of available models for local providers
        /// </summary>
        /// <param name="settings">The settings to update with detected models</param>
        /// <returns>True if models were detected and set, false otherwise</returns>
        Task<bool> AutoDetectModelsAsync(BrainarrSettings settings);

        /// <summary>
        /// Tests the connection to the configured provider
        /// </summary>
        /// <returns>True if connection test succeeded, false otherwise</returns>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// Records a successful provider operation for health monitoring
        /// </summary>
        /// <param name="responseTimeMs">The response time in milliseconds</param>
        void RecordSuccess(double responseTimeMs);

        /// <summary>
        /// Records a failed provider operation for health monitoring
        /// </summary>
        /// <param name="errorMessage">The error message describing the failure</param>
        void RecordFailure(string errorMessage);
    }
}