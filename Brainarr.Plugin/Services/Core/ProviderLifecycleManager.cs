using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Manages the lifecycle of AI providers including initialization, health monitoring, and disposal
    /// </summary>
    public sealed class ProviderLifecycleManager : IProviderLifecycleManager
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly IProviderFactory _providerFactory;
        private readonly ModelDetectionService _modelDetection;
        private readonly IProviderHealthMonitor _healthMonitor;
        private IAIProvider _currentProvider;
        private string _currentProviderType;
        private readonly object _providerLock = new object();

        public ProviderLifecycleManager(
            IHttpClient httpClient,
            IProviderFactory providerFactory,
            ModelDetectionService modelDetection,
            IProviderHealthMonitor healthMonitor,
            Logger logger)
        {
            _httpClient = httpClient;
            _providerFactory = providerFactory;
            _modelDetection = modelDetection;
            _healthMonitor = healthMonitor;
            _logger = logger;
        }

        public async Task<bool> InitializeProviderAsync(BrainarrSettings settings)
        {
            try
            {
                lock (_providerLock)
                {
                    // Dispose existing provider if type changed
                    if (_currentProvider != null && _currentProviderType != settings.Provider.ToString())
                    {
                        _logger.Info($"Provider type changed from {_currentProviderType} to {settings.Provider}, disposing old provider");
                        DisposeCurrentProvider();
                    }

                    // Auto-detect models if enabled
                    if (settings.AutoDetectModel)
                    {
                        var modelsDetected = AutoDetectModelsAsync(settings).GetAwaiter().GetResult();
                        if (!modelsDetected)
                        {
                            _logger.Warn("Model auto-detection failed, using configured defaults");
                        }
                    }

                    // Create new provider using factory
                    try
                    {
                        _currentProvider = _providerFactory.CreateProvider(settings, _httpClient, _logger);
                        _currentProviderType = settings.Provider.ToString();
                        _logger.Info($"Successfully initialized {_currentProviderType} provider");
                        return true;
                    }
                    catch (NotSupportedException ex)
                    {
                        _logger.Error(ex, $"Provider type {settings.Provider} is not supported");
                        return false;
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.Error(ex, "Invalid provider configuration");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize provider");
                return false;
            }
        }

        public IAIProvider GetProvider()
        {
            lock (_providerLock)
            {
                return _currentProvider;
            }
        }

        public async Task<bool> IsProviderHealthyAsync()
        {
            if (_currentProvider == null || string.IsNullOrEmpty(_currentProviderType))
            {
                return false;
            }

            var healthStatus = await _healthMonitor.CheckHealthAsync(_currentProviderType, null);
            return healthStatus == HealthStatus.Healthy;
        }

        public async Task<bool> AutoDetectModelsAsync(BrainarrSettings settings)
        {
            try
            {
                _logger.Info($"Auto-detecting models for {settings.Provider}");
                
                List<string> detectedModels = null;
                
                if (settings.Provider == AIProvider.Ollama)
                {
                    detectedModels = await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
                }
                else if (settings.Provider == AIProvider.LMStudio)
                {
                    detectedModels = await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
                }
                
                if (detectedModels != null && detectedModels.Any())
                {
                    var selectedModel = SelectBestModel(detectedModels);
                    
                    if (!string.IsNullOrEmpty(selectedModel))
                    {
                        if (settings.Provider == AIProvider.Ollama)
                        {
                            settings.OllamaModel = selectedModel;
                            _logger.Info($"Auto-detected Ollama model: {selectedModel}");
                        }
                        else if (settings.Provider == AIProvider.LMStudio)
                        {
                            settings.LMStudioModel = selectedModel;
                            _logger.Info($"Auto-detected LM Studio model: {selectedModel}");
                        }
                        return true;
                    }
                }
                
                _logger.Warn($"No models detected for {settings.Provider}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to auto-detect models for {settings.Provider}");
                return false;
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (_currentProvider == null)
            {
                return false;
            }

            try
            {
                return await _currentProvider.TestConnectionAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Connection test failed");
                return false;
            }
        }

        public void RecordSuccess(double responseTimeMs)
        {
            if (!string.IsNullOrEmpty(_currentProviderType))
            {
                _healthMonitor.RecordSuccess(_currentProviderType, responseTimeMs);
            }
        }

        public void RecordFailure(string errorMessage)
        {
            if (!string.IsNullOrEmpty(_currentProviderType))
            {
                _healthMonitor.RecordFailure(_currentProviderType, errorMessage);
            }
        }

        public void Dispose()
        {
            lock (_providerLock)
            {
                DisposeCurrentProvider();
            }
        }

        private void DisposeCurrentProvider()
        {
            if (_currentProvider is IDisposable disposableProvider)
            {
                try
                {
                    disposableProvider.Dispose();
                    _logger.Debug($"Disposed {_currentProviderType} provider");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Error disposing {_currentProviderType} provider");
                }
            }
            _currentProvider = null;
            _currentProviderType = null;
        }

        private string SelectBestModel(List<string> models)
        {
            // Prefer models in this order based on quality and performance
            var preferredModels = new[] 
            { 
                "qwen2.5", "qwen", 
                "llama3.2", "llama3.1", "llama", 
                "mistral", 
                "phi", 
                "gemma" 
            };
            
            foreach (var preferred in preferredModels)
            {
                var match = models.FirstOrDefault(m => m.ToLower().Contains(preferred));
                if (!string.IsNullOrEmpty(match))
                {
                    return match;
                }
            }
            
            // Fallback to first available model
            return models.FirstOrDefault();
        }
    }
}