using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Common.Http;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class ProviderManager : IProviderManager, IDisposable
    {
        private readonly IHttpClient _httpClient;
        private readonly IProviderFactory _providerFactory;
        private readonly ModelDetectionService _modelDetection;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IRateLimiter _rateLimiter;
        private readonly Logger _logger;
        private IAIProvider? _currentProvider;
        private BrainarrSettings? _currentSettings;

        public ProviderManager(
            IHttpClient httpClient,
            IProviderFactory providerFactory,
            ModelDetectionService modelDetection,
            IRetryPolicy retryPolicy,
            IRateLimiter rateLimiter,
            Logger logger)
        {
            _httpClient = httpClient;
            _providerFactory = providerFactory;
            _modelDetection = modelDetection;
            _retryPolicy = retryPolicy;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }

        public IAIProvider GetCurrentProvider()
        {
            return _currentProvider;
        }

        public void InitializeProvider(BrainarrSettings settings)
        {
            if (IsProviderCurrent(settings))
            {
                _logger.Debug("Provider already initialized with current settings");
                return;
            }

            try
            {
                DisposeCurrentProvider();

                _currentProvider = _providerFactory.CreateProvider(
                    settings,
                    _httpClient,
                    _logger);

                _currentSettings = settings;

                if (ShouldAutoDetect(settings))
                {
                    AsyncHelper.RunSync(() => AutoConfigureModel(settings));
                }

                _logger.Info($"Initialized {settings.Provider} provider successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to initialize {settings.Provider} provider");
                throw;
            }
        }

        public void UpdateProvider(BrainarrSettings settings)
        {
            if (!IsProviderCurrent(settings))
            {
                InitializeProvider(settings);
                return;
            }

            if (_currentProvider != null && HasModelChanged(settings))
            {
                var model = GetConfiguredModel(settings);
                _currentProvider.UpdateModel(model);
                _currentSettings = settings;
                _logger.Info($"Updated provider model to: {model}");
            }
        }

        public async Task<List<string>> DetectAvailableModels(BrainarrSettings settings)
        {
            try
            {
                return settings.Provider switch
                {
                    AIProvider.Ollama => await _modelDetection.DetectOllamaModelsAsync(settings.BaseUrl),
                    AIProvider.LMStudio => await _modelDetection.DetectLMStudioModelsAsync(settings.BaseUrl),
                    _ => new List<string>()
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to detect models for {settings.Provider}");
                return new List<string>();
            }
        }

        public string? SelectBestModel(List<string> availableModels)
        {
            if (availableModels == null || !availableModels.Any())
            {
                return null;
            }

            var rankedModels = new Dictionary<string, int>
            {
                { "llama3", 100 },
                { "llama-3.1", 95 },
                { "llama2", 90 },
                { "mistral", 85 },
                { "mixtral", 83 },
                { "qwen2", 80 },
                { "qwen", 78 },
                { "gemma2", 75 },
                { "gemma", 73 },
                { "phi3", 70 },
                { "phi", 68 },
                { "neural-chat", 65 },
                { "deepseek", 60 },
                { "codellama", 50 },
                { "vicuna", 45 },
                { "alpaca", 40 }
            };

            var scoredModels = availableModels
                .Select(model => new
                {
                    Model = model,
                    Score = CalculateModelScore(model.ToLower(), rankedModels)
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Model.Length)
                .ToList();

            var selected = scoredModels.First().Model;
            _logger.Info($"Selected best model: {selected} (score: {scoredModels.First().Score})");
            
            return selected;
        }

        public bool IsProviderReady()
        {
            return _currentProvider != null;
        }

        public void Dispose()
        {
            DisposeCurrentProvider();
        }

        private bool IsProviderCurrent(BrainarrSettings settings)
        {
            if (_currentProvider == null || _currentSettings == null)
            {
                return false;
            }

            return _currentSettings.Provider == settings.Provider &&
                   _currentSettings.BaseUrl == settings.BaseUrl;
        }

        private bool ShouldAutoDetect(BrainarrSettings settings)
        {
            return settings.EnableAutoDetection &&
                   (settings.Provider == AIProvider.Ollama || 
                    settings.Provider == AIProvider.LMStudio);
        }

        private async Task AutoConfigureModel(BrainarrSettings settings)
        {
            try
            {
                var availableModels = await DetectAvailableModels(settings);
                
                if (!availableModels.Any())
                {
                    _logger.Warn("No models detected for auto-configuration");
                    return;
                }

                var currentModel = GetConfiguredModel(settings);
                
                if (string.IsNullOrEmpty(currentModel) || !availableModels.Contains(currentModel))
                {
                    var bestModel = SelectBestModel(availableModels);
                    
                    SetConfiguredModel(settings, bestModel);
                    _currentProvider?.UpdateModel(bestModel);
                    
                    _logger.Info($"Auto-configured model: {bestModel}");
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Auto-configuration failed, using default settings");
            }
        }

        private bool HasModelChanged(BrainarrSettings settings)
        {
            var currentModel = GetConfiguredModel(_currentSettings);
            var newModel = GetConfiguredModel(settings);
            return currentModel != newModel;
        }

        private string? GetConfiguredModel(BrainarrSettings settings)
        {
            // Use polymorphic method to get model configuration
            return settings.GetModelForProvider();
        }

        private void SetConfiguredModel(BrainarrSettings settings, string? model)
        {
            // Use polymorphic method to set model configuration
            settings.SetModelForProvider(model);
        }

        private int CalculateModelScore(string model, Dictionary<string, int> rankings)
        {
            foreach (var ranking in rankings)
            {
                if (model.Contains(ranking.Key))
                {
                    var sizeBonus = 0;
                    if (model.Contains("70b")) sizeBonus = 20;
                    else if (model.Contains("34b") || model.Contains("33b")) sizeBonus = 15;
                    else if (model.Contains("13b")) sizeBonus = 10;
                    else if (model.Contains("8b") || model.Contains("7b")) sizeBonus = 5;
                    
                    return ranking.Value + sizeBonus;
                }
            }
            
            return 0;
        }

        private void DisposeCurrentProvider()
        {
            if (_currentProvider is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Error disposing provider");
                }
            }
            
            _currentProvider = null;
            _currentSettings = null;
        }
    }
}