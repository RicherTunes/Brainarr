using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service interface for detecting available AI models from providers.
    /// </summary>
    public interface IModelDetectionService
    {
        /// <summary>
        /// Gets available models from an Ollama instance.
        /// </summary>
        /// <param name="baseUrl">Base URL of the Ollama instance</param>
        /// <returns>List of available model names</returns>
        Task<List<string>> GetOllamaModelsAsync(string baseUrl);

        /// <summary>
        /// Gets available models from an LM Studio instance.
        /// </summary>
        /// <param name="baseUrl">Base URL of the LM Studio instance</param>
        /// <returns>List of available model names</returns>
        Task<List<string>> GetLMStudioModelsAsync(string baseUrl);

        /// <summary>
        /// Detects the best available model for a provider.
        /// </summary>
        /// <param name="providerType">Type of AI provider</param>
        /// <param name="baseUrl">Base URL of the provider</param>
        /// <returns>Recommended model name or null if none found</returns>
        Task<string> DetectBestModelAsync(AIProvider providerType, string baseUrl);
    }
}