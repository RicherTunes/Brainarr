using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class ProviderValidator : IProviderValidator
    {
        private readonly IServiceConfiguration _services;
        private readonly Logger _logger;

        public ProviderValidator(IServiceConfiguration services, Logger logger)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void ValidateProvider(BrainarrSettings settings, List<ValidationFailure> failures)
        {
            try
            {
                var provider = _services.CreateProvider(settings);
                
                if (provider == null)
                {
                    failures.Add(new ValidationFailure(nameof(settings.Provider), 
                        "AI provider not configured"));
                    return;
                }

                var connected = provider.TestConnectionAsync().GetAwaiter().GetResult();
                if (!connected)
                {
                    failures.Add(new ValidationFailure(string.Empty, 
                        $"Cannot connect to {provider.ProviderName}"));
                    return;
                }

                ValidateLocalProviderModels(settings, failures);

                _logger.Info($"Test successful: Connected to {provider.ProviderName}");
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Test failed: {ex.Message}"));
            }
        }

        private void ValidateLocalProviderModels(BrainarrSettings settings, List<ValidationFailure> failures)
        {
            if (settings.Provider == AIProvider.Ollama)
            {
                ValidateOllamaModels(settings, failures);
            }
            else if (settings.Provider == AIProvider.LMStudio)
            {
                ValidateLMStudioModels(settings, failures);
            }
            else
            {
                _logger.Info($"✅ Connected successfully to {settings.Provider}");
            }
        }

        private void ValidateOllamaModels(BrainarrSettings settings, List<ValidationFailure> failures)
        {
            var models = _services.ModelDetection.GetOllamaModelsAsync(settings.OllamaUrl)
                .GetAwaiter().GetResult();
            
            if (models.Any())
            {
                _logger.Info($"✅ Found {models.Count} Ollama models: {string.Join(", ", models)}");
                settings.DetectedModels = models;
                
                var topModels = models.Take(3).ToList();
                var modelList = string.Join(", ", topModels);
                if (models.Count > 3) modelList += $" (and {models.Count - 3} more)";
                
                _logger.Info($"🎯 Recommended: Copy one of these models into the field above: {modelList}");
            }
            else
            {
                failures.Add(new ValidationFailure(string.Empty, 
                    "No suitable models found. Install models like: ollama pull qwen2.5"));
            }
        }

        private void ValidateLMStudioModels(BrainarrSettings settings, List<ValidationFailure> failures)
        {
            var models = _services.ModelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl)
                .GetAwaiter().GetResult();
            
            if (models.Any())
            {
                _logger.Info($"✅ Found {models.Count} LM Studio models: {string.Join(", ", models)}");
                settings.DetectedModels = models;
                
                var topModels = models.Take(3).ToList();
                var modelList = string.Join(", ", topModels);
                if (models.Count > 3) modelList += $" (and {models.Count - 3} more)";
                
                _logger.Info($"🎯 Recommended: Copy one of these models into the field above: {modelList}");
            }
            else
            {
                failures.Add(new ValidationFailure(string.Empty, 
                    "No models loaded. Load a model in LM Studio first."));
            }
        }
    }

    public interface IProviderValidator
    {
        void ValidateProvider(BrainarrSettings settings, List<ValidationFailure> failures);
    }
}