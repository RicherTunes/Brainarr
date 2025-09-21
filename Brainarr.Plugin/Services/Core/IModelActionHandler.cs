using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Handles model-specific actions for AI providers including connection testing,
    /// model detection, and provider-specific operations.
    /// This interface abstracts provider actions from the UI layer.
    /// </summary>
    public interface IModelActionHandler
    {
        /// <summary>
        /// Tests the connection to the configured AI provider.
        /// For local providers (Ollama/LM Studio), verifies service availability.
        /// For cloud providers, validates API credentials and connectivity.
        /// </summary>
        /// <param name="settings">The provider configuration settings</param>
        /// <returns>A string indicating success or failure with details</returns>
        Task<string> HandleTestConnectionAsync(BrainarrSettings settings);

        /// <summary>
        /// Tests the connection to the configured AI provider and returns structured details
        /// without changing existing string-based contracts. Useful for UI surfaces that
        /// want to show a provider-specific hint (e.g., activation URL) alongside success.
        /// </summary>
        /// <param name="settings">The provider configuration settings</param>
        /// <returns>A structured result with success status and optional hint</returns>
        Task<TestConnectionResult> HandleTestConnectionDetailsAsync(BrainarrSettings settings);

        /// <summary>
        /// Retrieves available models for the configured provider.
        /// For local providers, performs live detection of installed models.
        /// For cloud providers, returns predefined model enumerations.
        /// </summary>
        /// <param name="settings">The provider configuration settings</param>
        /// <returns>A list of available model options for UI selection</returns>
        Task<List<SelectOption>> HandleGetModelsAsync(BrainarrSettings settings);

        /// <summary>
        /// Analyzes the user's music library to provide insights or recommendations.
        /// Currently a placeholder for future library analysis functionality.
        /// </summary>
        /// <param name="settings">The provider configuration settings</param>
        /// <returns>A string containing analysis results</returns>
        Task<string> HandleAnalyzeLibraryAsync(BrainarrSettings settings);

        /// <summary>
        /// Handles generic provider-specific actions from the UI.
        /// Includes operations like clearing model cache or refreshing model lists.
        /// </summary>
        /// <param name="action">The action to perform (e.g., "providerChanged", "getModelOptions")</param>
        /// <param name="settings">The provider configuration settings</param>
        /// <returns>An object containing action results for the UI</returns>
        object HandleProviderAction(string action, BrainarrSettings settings);
    }

    /// <summary>
    /// Structured result for provider connection tests.
    /// Exposes a machine-friendly success flag and an optional human-friendly hint.
    /// </summary>
    public class TestConnectionResult
    {
        public bool Success { get; set; }
        public string? Hint { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string? Docs { get; set; }
    }
}
