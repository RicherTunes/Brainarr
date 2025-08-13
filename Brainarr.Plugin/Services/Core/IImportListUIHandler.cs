using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Handles UI actions and model discovery for the import list
    /// </summary>
    public interface IImportListUIHandler
    {
        /// <summary>
        /// Handles UI action requests from the Lidarr interface
        /// </summary>
        /// <param name="action">The action to perform</param>
        /// <param name="settings">The current Brainarr settings</param>
        /// <param name="query">Optional query parameters</param>
        /// <returns>Response object for the UI</returns>
        object HandleAction(string action, BrainarrSettings settings, IDictionary<string, string> query = null);

        /// <summary>
        /// Gets model options for the currently selected provider
        /// </summary>
        /// <param name="settings">The current Brainarr settings</param>
        /// <returns>Model options for dropdown display</returns>
        Task<object> GetModelOptionsAsync(BrainarrSettings settings);

        /// <summary>
        /// Gets available Ollama models
        /// </summary>
        /// <param name="ollamaUrl">The Ollama server URL</param>
        /// <returns>List of available models for dropdown</returns>
        Task<object> GetOllamaModelOptionsAsync(string ollamaUrl);

        /// <summary>
        /// Gets available LM Studio models
        /// </summary>
        /// <param name="lmStudioUrl">The LM Studio server URL</param>
        /// <returns>List of available models for dropdown</returns>
        Task<object> GetLMStudioModelOptionsAsync(string lmStudioUrl);

        /// <summary>
        /// Gets static model options for cloud providers
        /// </summary>
        /// <param name="providerType">The provider type enum</param>
        /// <returns>List of model options from enum</returns>
        object GetStaticModelOptions(System.Type providerType);
    }
}