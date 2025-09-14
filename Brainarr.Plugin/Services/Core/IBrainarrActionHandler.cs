using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Handles UI actions and provider-specific operations for the Brainarr plugin.
    /// Provides model detection and option retrieval for different AI providers.
    /// </summary>
    public interface IBrainarrActionHandler
    {
        /// <summary>
        /// Handles dynamic UI actions from the Lidarr configuration interface.
        /// Processes model detection requests for local providers (Ollama, LM Studio)
        /// and returns formatted options for static cloud providers.
        /// </summary>
        /// <param name="action">The UI action to perform (e.g., "getOllamaModels", "getLMStudioModels")</param>
        /// <param name="query">Query parameters containing provider configuration (e.g., baseUrl)</param>
        /// <returns>An object containing model options or action results for the UI</returns>
        object HandleAction(string action, IDictionary<string, string> query);

        /// <summary>
        /// Gets available model options for a specific AI provider.
        /// For local providers, performs live model detection.
        /// For cloud providers, returns predefined enum-based options.
        /// </summary>
        /// <param name="provider">The AI provider name (e.g., "OpenAI", "Anthropic", "Gemini")</param>
        /// <returns>An object containing formatted model options for UI display</returns>
        object GetModelOptions(string provider);

        /// <summary>
        /// Gets fallback model options for providers that support failover.
        /// Used when primary model detection fails or for redundancy configurations.
        /// </summary>
        /// <param name="provider">The AI provider name</param>
        /// <returns>An object containing fallback model options, typically mirrors GetModelOptions</returns>
        object GetFallbackModelOptions(string provider);
    }
}
