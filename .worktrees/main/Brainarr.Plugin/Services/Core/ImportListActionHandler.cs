using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Handles UI actions and model detection for the import list.
    /// Separates UI interaction logic from the main import list implementation.
    /// </summary>
    public class ImportListActionHandler
    {
        private readonly ModelDetectionService _modelDetection;
        private readonly BrainarrSettings _settings;
        private readonly Logger _logger;

        public ImportListActionHandler(
            ModelDetectionService modelDetection,
            BrainarrSettings settings,
            Logger logger)
        {
            _modelDetection = modelDetection ?? throw new ArgumentNullException(nameof(modelDetection));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Handles action requests from the UI.
        /// </summary>
        public object HandleAction(string action, IDictionary<string, string> query)
        {
            _logger.Info($"HandleAction called with action: {action}");

            switch (action)
            {
                case "providerChanged":
                    return HandleProviderChanged();
                case "getModelOptions":
                    return GetModelOptions();
                case "getOllamaOptions" when _settings.Provider == AIProvider.Ollama:
                    return GetOllamaModelOptions();
                case "getLMStudioOptions" when _settings.Provider == AIProvider.LMStudio:
                    return GetLMStudioModelOptions();
                default:
                    _logger.Info($"Unknown action '{action}' or provider mismatch");
                    return new { };
            }
        }

        /// <summary>
        /// Auto-detects and sets the appropriate model for local providers.
        /// </summary>
        public async Task<bool> AutoDetectAndSetModelAsync()
        {
            try
            {
                _logger.Info($"Auto-detecting models for {_settings.Provider}");

                List<string> detectedModels = _settings.Provider switch
                {
                    AIProvider.Ollama => await _modelDetection.GetOllamaModelsAsync(_settings.OllamaUrl),
                    AIProvider.LMStudio => await _modelDetection.GetLMStudioModelsAsync(_settings.LMStudioUrl),
                    _ => new List<string>()
                };

                if (detectedModels?.Any() == true)
                {
                    var selectedModel = SelectBestModel(detectedModels);

                    if (_settings.Provider == AIProvider.Ollama)
                    {
                        _settings.OllamaModel = selectedModel;
                        _logger.Info($"Auto-detected Ollama model: {selectedModel}");
                    }
                    else if (_settings.Provider == AIProvider.LMStudio)
                    {
                        _settings.LMStudioModel = selectedModel;
                        _logger.Info($"Auto-detected LM Studio model: {selectedModel}");
                    }

                    return true;
                }
                else
                {
                    _logger.Warn($"No models detected for {_settings.Provider}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to auto-detect models for {_settings.Provider}");
                return false;
            }
        }

        private object HandleProviderChanged()
        {
            _logger.Info("Provider changed, clearing model cache");
            _settings.DetectedModels?.Clear();
            return new { success = true, message = "Provider changed, model cache cleared" };
        }

        private object GetModelOptions()
        {
            _logger.Info($"GetModelOptions called for provider: {_settings.Provider}");

            if (_settings.DetectedModels?.Any() == true)
            {
                _logger.Info("Clearing stale detected models from previous provider");
                _settings.DetectedModels.Clear();
            }

            return _settings.Provider switch
            {
                AIProvider.Ollama => GetOllamaModelOptions(),
                AIProvider.LMStudio => GetLMStudioModelOptions(),
                AIProvider.Perplexity => GetStaticModelOptions(typeof(PerplexityModelKind)),
                AIProvider.OpenAI => GetStaticModelOptions(typeof(OpenAIModelKind)),
                AIProvider.Anthropic => GetStaticModelOptions(typeof(AnthropicModelKind)),
                AIProvider.OpenRouter => GetStaticModelOptions(typeof(OpenRouterModelKind)),
                AIProvider.DeepSeek => GetStaticModelOptions(typeof(DeepSeekModelKind)),
                AIProvider.Gemini => GetStaticModelOptions(typeof(GeminiModelKind)),
                AIProvider.Groq => GetStaticModelOptions(typeof(GroqModelKind)),
                _ => new { options = new List<object>() }
            };
        }

        private object GetOllamaModelOptions()
        {
            _logger.Info("Getting Ollama model options");

            if (string.IsNullOrWhiteSpace(_settings.OllamaUrl))
            {
                _logger.Info("OllamaUrl is empty, returning fallback options");
                return GetOllamaFallbackOptions();
            }

            try
            {
                var models = SafeAsyncHelper.RunSafeSync(() => _modelDetection.GetOllamaModelsAsync(_settings.OllamaUrl));

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} Ollama models");
                    var options = models.Select(model => new
                    {
                        Value = model,
                        Name = NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatModelName(model)
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

        private object GetLMStudioModelOptions()
        {
            _logger.Info("Getting LM Studio model options");

            if (string.IsNullOrWhiteSpace(_settings.LMStudioUrl))
            {
                _logger.Info("LMStudioUrl is empty, returning fallback options");
                return GetLMStudioFallbackOptions();
            }

            try
            {
                var models = SafeAsyncHelper.RunSafeSync(() => _modelDetection.GetLMStudioModelsAsync(_settings.LMStudioUrl));

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} LM Studio models");
                    var options = models.Select(model => new
                    {
                        Value = model,
                        Name = NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatModelName(model)
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
                    Name = NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatEnumName(value.ToString())
                }).ToList();

            return new { options = options };
        }

        private string SelectBestModel(List<string> models)
        {
            var preferredModels = new[] { "qwen", "llama", "mistral", "phi", "gemma" };

            foreach (var preferred in preferredModels)
            {
                var match = models.FirstOrDefault(m => m.ToLower().Contains(preferred));
                if (match != null) return match;
            }

            return models.First();
        }

        // Model/enum name formatting consolidated in Utils/ModelNameFormatter

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
}
