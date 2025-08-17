using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class ProviderInitializationService : IProviderInitializationService
    {
        private readonly IProviderFactory _providerFactory;
        private readonly IModelDetectionService _modelDetection;
        private readonly IProviderHealthMonitor _healthMonitor;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public ProviderInitializationService(
            IProviderFactory providerFactory,
            IModelDetectionService modelDetection,
            IProviderHealthMonitor healthMonitor,
            IHttpClient httpClient,
            Logger logger)
        {
            _providerFactory = providerFactory;
            _modelDetection = modelDetection;
            _healthMonitor = healthMonitor;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<IAIProvider> InitializeProviderAsync(BrainarrSettings settings)
        {
            try
            {
                if (settings.AutoDetectModels)
                {
                    await DetectAndConfigureModelsAsync(settings);
                }

                var provider = _providerFactory.CreateProvider(
                    settings.Provider,
                    settings,
                    _httpClient,
                    _logger);

                if (await ValidateProviderAsync(provider, settings))
                {
                    await _healthMonitor.RecordSuccessAsync(settings.Provider.ToString());
                    return provider;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to initialize provider {settings.Provider}");
                await _healthMonitor.RecordFailureAsync(settings.Provider.ToString());
                throw;
            }
        }

        public async Task<bool> ValidateProviderAsync(IAIProvider provider, BrainarrSettings settings)
        {
            if (provider == null)
            {
                _logger.Warn("Provider is null, cannot validate");
                return false;
            }

            var healthStatus = await _healthMonitor.CheckHealthAsync(
                settings.Provider.ToString(),
                settings.BaseUrl);

            if (healthStatus == HealthStatus.Unhealthy)
            {
                _logger.Warn($"Provider {settings.Provider} is unhealthy");
                return false;
            }

            return true;
        }

        public async Task DetectAndConfigureModelsAsync(BrainarrSettings settings)
        {
            if (settings.Provider == AIProvider.Ollama || 
                settings.Provider == AIProvider.LMStudio)
            {
                var detectedModels = await _modelDetection.DetectAvailableModelsAsync(
                    settings.BaseUrl,
                    settings.Provider);

                if (detectedModels?.Any() == true)
                {
                    _logger.Info($"Detected {detectedModels.Count} models for {settings.Provider}");
                    
                    if (string.IsNullOrEmpty(settings.Model))
                    {
                        settings.Model = detectedModels.First();
                        _logger.Info($"Auto-selected model: {settings.Model}");
                    }
                }
            }
        }
    }
}