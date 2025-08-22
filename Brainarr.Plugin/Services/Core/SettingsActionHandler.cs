using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class SettingsActionHandler : ISettingsActionHandler
    {
        private readonly ModelDetectionService _modelDetection;
        private readonly Logger _logger;

        public SettingsActionHandler(ModelDetectionService modelDetection, Logger logger)
        {
            _modelDetection = modelDetection ?? throw new ArgumentNullException(nameof(modelDetection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public object HandleAction(string action, BrainarrSettings settings, IDictionary<string, string> query)
        {
            _logger.Info($"HandleAction called with action: {action}");
            
            switch (action)
            {
                case "providerChanged":
                    return HandleProviderChanged(settings);
                    
                case "getModelOptions":
                    return GetModelOptions(settings);
                    
                case "getOllamaOptions":
                    return settings.Provider == AIProvider.Ollama 
                        ? GetOllamaModelOptions(settings) 
                        : new { };
                        
                case "getLMStudioOptions":
                    return settings.Provider == AIProvider.LMStudio 
                        ? GetLMStudioModelOptions(settings) 
                        : new { };
                        
                default:
                    _logger.Info($"Unknown action '{action}', returning empty object");
                    return new { };
            }
        }

        private object HandleProviderChanged(BrainarrSettings settings)
        {
            _logger.Info("Provider changed, clearing model cache");
            settings.DetectedModels?.Clear();
            return new { success = true, message = "Provider changed, model cache cleared" };
        }

        private object GetModelOptions(BrainarrSettings settings)
        {
            _logger.Info($"GetModelOptions called for provider: {settings.Provider}");
            
            if (settings.DetectedModels != null && settings.DetectedModels.Any())
            {
                _logger.Info("Clearing stale detected models from previous provider");
                settings.DetectedModels.Clear();
            }
            
            return settings.Provider switch
            {
                AIProvider.Ollama => GetOllamaModelOptions(settings),
                AIProvider.LMStudio => GetLMStudioModelOptions(settings),
                AIProvider.Perplexity => GetStaticModelOptions(typeof(PerplexityModel)),
                AIProvider.OpenAI => GetStaticModelOptions(typeof(OpenAIModel)),
                AIProvider.Anthropic => GetStaticModelOptions(typeof(AnthropicModel)),
                AIProvider.OpenRouter => GetStaticModelOptions(typeof(OpenRouterModel)),
                AIProvider.DeepSeek => GetStaticModelOptions(typeof(DeepSeekModel)),
                AIProvider.Gemini => GetStaticModelOptions(typeof(GeminiModel)),
                AIProvider.Groq => GetStaticModelOptions(typeof(GroqModel)),
                _ => new { options = new List<object>() }
            };
        }

        private object GetOllamaModelOptions(BrainarrSettings settings)
        {
            _logger.Info("Getting Ollama model options");
            
            if (string.IsNullOrWhiteSpace(settings.OllamaUrl))
            {
                _logger.Info("OllamaUrl is empty, returning fallback options");
                return GetOllamaFallbackOptions();
            }

            try
            {
                var models = _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl)
                    .GetAwaiter().GetResult();

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} Ollama models");
                    var options = models.Select(model => new
                    {
                        Value = model,
                        Name = ModelNameFormatter.FormatModelName(model)
                    }).ToList();
                    
                    return new { options = options };
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get Ollama models for dropdown");
            }

            return GetOllamaFallbackOptions();
        }

        private object GetLMStudioModelOptions(BrainarrSettings settings)
        {
            _logger.Info("Getting LM Studio model options");
            
            if (string.IsNullOrWhiteSpace(settings.LMStudioUrl))
            {
                _logger.Info("LMStudioUrl is empty, returning fallback options");
                return GetLMStudioFallbackOptions();
            }

            try
            {
                var models = _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl)
                    .GetAwaiter().GetResult();

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} LM Studio models");
                    var options = models.Select(model => new
                    {
                        Value = model,
                        Name = ModelNameFormatter.FormatModelName(model)
                    }).ToList();
                    
                    return new { options = options };
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get LM Studio models for dropdown");
            }

            return GetLMStudioFallbackOptions();
        }

        private object GetStaticModelOptions(Type enumType)
        {
            var options = Enum.GetValues(enumType)
                .Cast<Enum>()
                .Select(value => new
                {
                    Value = value.ToString(),
                    Name = ModelNameFormatter.FormatEnumName(value.ToString())
                }).ToList();
            
            return new { options = options };
        }

        private object GetOllamaFallbackOptions()
        {
            return new
            {
                options = new[]
                {
                    new { Value = "qwen2.5:latest", Name = "Qwen 2.5 (Recommended)" },
                    new { Value = "qwen2.5:7b", Name = "Qwen 2.5 7B" },
                    new { Value = "llama3.2:latest", Name = "Llama 3.2" },
                    new { Value = "mistral:latest", Name = "Mistral" }
                }
            };
        }

        private object GetLMStudioFallbackOptions()
        {
            return new
            {
                options = new[]
                {
                    new { Value = "local-model", Name = "Currently Loaded Model" }
                }
            };
        }
    }

    public interface ISettingsActionHandler
    {
        object HandleAction(string action, BrainarrSettings settings, IDictionary<string, string> query);
    }
}