using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace Brainarr.Plugin.ImportList.Orchestration
{
    /// <summary>
    /// Coordinates provider initialization and model detection
    /// </summary>
    public class ProviderCoordinator
    {
        private readonly IProviderFactory _providerFactory;
        private readonly ModelDetectionService _modelDetection;
        private readonly Logger _logger;

        public ProviderCoordinator(
            IProviderFactory providerFactory,
            ModelDetectionService modelDetection,
            Logger logger)
        {
            _providerFactory = providerFactory;
            _modelDetection = modelDetection;
            _logger = logger;
        }

        /// <summary>
        /// Initializes the AI provider with optional model auto-detection
        /// </summary>
        public async Task<IAIProvider> InitializeProviderAsync(
            BrainarrSettings settings,
            IHttpClient httpClient)
        {
            // Auto-detect models if enabled
            if (settings.AutoDetectModel)
            {
                await AutoDetectAndSetModelAsync(settings);
            }

            // Create provider using factory
            try
            {
                return _providerFactory.CreateProvider(settings, httpClient, _logger);
            }
            catch (NotSupportedException ex)
            {
                _logger.Error(ex, $"Provider type {settings.Provider} is not supported");
                return null;
            }
            catch (ArgumentException ex)
            {
                _logger.Error(ex, "Invalid provider configuration");
                return null;
            }
        }

        /// <summary>
        /// Auto-detects available models for local providers
        /// </summary>
        private async Task AutoDetectAndSetModelAsync(BrainarrSettings settings)
        {
            try
            {
                _logger.Info($"Auto-detecting models for {settings.Provider}");

                List<string> detectedModels = settings.Provider switch
                {
                    AIProvider.Ollama => await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl),
                    AIProvider.LMStudio => await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl),
                    _ => new List<string>()
                };

                if (detectedModels.Any())
                {
                    // Select the best model based on capabilities
                    var selectedModel = SelectBestModel(detectedModels, settings.Provider);
                    
                    if (!string.IsNullOrEmpty(selectedModel))
                    {
                        UpdateModelSetting(settings, selectedModel);
                        _logger.Info($"Auto-selected model: {selectedModel}");
                    }
                }
                else
                {
                    _logger.Warn($"No models detected for {settings.Provider}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to auto-detect models for {settings.Provider}");
            }
        }

        /// <summary>
        /// Selects the best model from detected options
        /// </summary>
        private string SelectBestModel(List<string> models, AIProvider provider)
        {
            // Prioritize models by capability
            var priorityModels = provider == AIProvider.Ollama
                ? new[] { "llama3", "llama2", "mistral", "mixtral", "gemma", "qwen" }
                : new[] { "llama", "mistral", "gpt", "claude" };

            foreach (var priority in priorityModels)
            {
                var match = models.FirstOrDefault(m => 
                    m.ToLower().Contains(priority));
                if (match != null) return match;
            }

            // Return first available if no priority match
            return models.FirstOrDefault();
        }

        /// <summary>
        /// Updates the model setting based on provider type
        /// </summary>
        private void UpdateModelSetting(BrainarrSettings settings, string model)
        {
            switch (settings.Provider)
            {
                case AIProvider.Ollama:
                    settings.OllamaModel = model;
                    break;
                case AIProvider.LMStudio:
                    settings.LMStudioModel = model;
                    break;
            }
        }
    }
}