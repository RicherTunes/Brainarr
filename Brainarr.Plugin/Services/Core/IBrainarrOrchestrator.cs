using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using FluentValidation.Results;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Main orchestrator interface for the Brainarr plugin, coordinating all aspects of
    /// AI-powered music recommendation generation including provider management,
    /// health monitoring, caching, and library analysis.
    /// </summary>
    public interface IBrainarrOrchestrator
    {
        /// <summary>
        /// Synchronously fetches music recommendations using the configured AI provider.
        /// This method blocks until completion and is primarily used for legacy compatibility.
        /// </summary>
        /// <param name="settings">Complete provider configuration and preferences</param>
        /// <returns>A list of import items ready for Lidarr processing</returns>
        /// <remarks>
        /// Consider using FetchRecommendationsAsync for better performance in async contexts.
        /// This method internally calls the async version using GetAwaiter().GetResult().
        /// </remarks>
        IList<ImportListItemInfo> FetchRecommendations(BrainarrSettings settings);
        
        /// <summary>
        /// Asynchronously fetches music recommendations with full orchestration including
        /// caching, health monitoring, rate limiting, and intelligent library analysis.
        /// </summary>
        /// <param name="settings">Complete provider configuration including discovery mode and preferences</param>
        /// <returns>A list of validated and sanitized import items for Lidarr</returns>
        /// <remarks>
        /// This method performs the complete recommendation workflow:
        /// - Provider initialization and health validation
        /// - Library profile generation and caching
        /// - Intelligent recommendation strategy selection (library-aware vs simple)
        /// - Result validation and conversion to Lidarr format
        /// - Performance monitoring and error handling
        /// </remarks>
        Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings);
        
        /// <summary>
        /// Initializes or reinitializes the AI provider based on current settings.
        /// Handles provider factory creation, model detection for local providers,
        /// and configuration validation.
        /// </summary>
        /// <param name="settings">Provider configuration settings</param>
        /// <remarks>
        /// Provider initialization is idempotent - calling multiple times with the same
        /// settings will not recreate the provider unnecessarily.
        /// </remarks>
        void InitializeProvider(BrainarrSettings settings);
        
        /// <summary>
        /// Updates the provider configuration, typically after settings changes.
        /// This is equivalent to calling InitializeProvider but makes intent clearer.
        /// </summary>
        /// <param name="settings">Updated provider configuration settings</param>
        void UpdateProviderConfiguration(BrainarrSettings settings);
        
        /// <summary>
        /// Checks if the current AI provider is healthy and operational.
        /// Uses health monitoring data to determine provider availability.
        /// </summary>
        /// <returns>True if the provider is healthy and can process requests</returns>
        bool IsProviderHealthy();
        
        /// <summary>
        /// Gets a human-readable status string for the current provider.
        /// Includes provider type and health status information.
        /// </summary>
        /// <returns>A formatted status string for logging and UI display</returns>
        string GetProviderStatus();
        
        /// <summary>
        /// Validates the plugin configuration by testing provider connections and settings.
        /// </summary>
        /// <param name="settings">Settings to validate</param>
        /// <param name="failures">Collection to add validation failures to</param>
        void ValidateConfiguration(BrainarrSettings settings, List<ValidationFailure> failures);
        
        /// <summary>
        /// Handles UI actions such as model detection and provider option retrieval.
        /// </summary>
        /// <param name="action">The action to perform</param>
        /// <param name="query">Query parameters</param>
        /// <param name="settings">Current settings</param>
        /// <returns>Action result object</returns>
        object HandleAction(string action, IDictionary<string, string> query, BrainarrSettings settings);
    }
}