using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Concrete implementation of IModelActionHandler that manages model-specific operations
    /// for AI providers including connection testing, model detection, and provider actions.
    /// Handles both local providers (Ollama/LM Studio) and cloud providers with appropriate strategies.
    /// </summary>
    /// <remarks>
    /// This class is responsible for:
    /// - Testing connectivity to AI providers
    /// - Auto-detecting available models on local providers  
    /// - Managing model selection and caching
    /// - Providing fallback options when detection fails
    /// - Handling UI interactions for model management
    /// </remarks>
    public class ModelActionHandler : IModelActionHandler
    {
        private readonly IModelDetectionService _modelDetection;
        private readonly IProviderFactory _providerFactory;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        /// <summary>
        /// Initializes a new instance of the ModelActionHandler.
        /// </summary>
        /// <param name="modelDetection">Service for detecting models on local AI providers</param>
        /// <param name="providerFactory">Factory for creating provider instances</param>
        /// <param name="httpClient">HTTP client for provider communications</param>
        /// <param name="logger">Logger instance for debugging and monitoring</param>
        public ModelActionHandler(
            IModelDetectionService modelDetection,
            IProviderFactory providerFactory,
            IHttpClient httpClient,
            Logger logger)
        {
            _modelDetection = modelDetection;
            _providerFactory = providerFactory;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> HandleTestConnectionAsync(BrainarrSettings settings)
        {
            try
            {
                var provider = _providerFactory.CreateProvider(settings, _httpClient, _logger);
                if (provider == null)
                {
                    return "Failed: Provider not configured";
                }

                var connected = await provider.TestConnectionAsync();
                if (!connected)
                {
                    return $"Failed: Cannot connect to {provider.ProviderName}";
                }

                // Detect models for local providers
                if (settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio)
                {
                    await DetectAndUpdateModels(settings);
                }

                return $"Success: Connected to {provider.ProviderName}";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Test connection failed");
                return $"Failed: {ex.Message}";
            }
        }

        public async Task<List<SelectOption>> HandleGetModelsAsync(BrainarrSettings settings)
        {
            _logger.Info($"Getting model options for provider: {settings.Provider}");

            try
            {
                return settings.Provider switch
                {
                    AIProvider.Ollama => await GetOllamaModelOptions(settings),
                    AIProvider.LMStudio => await GetLMStudioModelOptions(settings),
                    AIProvider.Perplexity => GetStaticModelOptions(typeof(PerplexityModel)),
                    AIProvider.OpenAI => GetStaticModelOptions(typeof(OpenAIModel)),
                    AIProvider.Anthropic => GetStaticModelOptions(typeof(AnthropicModel)),
                    AIProvider.OpenRouter => GetStaticModelOptions(typeof(OpenRouterModel)),
                    AIProvider.DeepSeek => GetStaticModelOptions(typeof(DeepSeekModel)),
                    AIProvider.Gemini => GetStaticModelOptions(typeof(GeminiModel)),
                    AIProvider.Groq => GetStaticModelOptions(typeof(GroqModel)),
                    _ => new List<SelectOption>()
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to get model options for {settings.Provider}");
                return GetFallbackOptions(settings.Provider);
            }
        }

        public async Task<string> HandleAnalyzeLibraryAsync(BrainarrSettings settings)
        {
            // Placeholder for library analysis functionality
            return "Library analysis complete";
        }

        public object HandleProviderAction(string action, BrainarrSettings settings)
        {
            _logger.Info($"Handling provider action: {action}");

            if (action == "providerChanged")
            {
                settings.DetectedModels?.Clear();
                return new { success = true, message = "Provider changed, model cache cleared" };
            }

            if (action == "getModelOptions")
            {
                var models = SafeAsyncHelper.RunSafeSync(() => HandleGetModelsAsync(settings));
                return new { options = models };
            }

            return new { };
        }

        private async Task DetectAndUpdateModels(BrainarrSettings settings)
        {
            List<string> models = null;

            if (settings.Provider == AIProvider.Ollama)
            {
                models = await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
            }
            else if (settings.Provider == AIProvider.LMStudio)
            {
                models = await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
            }

            if (models?.Any() == true)
            {
                settings.DetectedModels = models;
                _logger.Info($"Detected {models.Count} models for {settings.Provider}");

                if (settings.AutoDetectModel)
                {
                    AutoSelectBestModel(settings, models);
                }
            }
        }

        private void AutoSelectBestModel(BrainarrSettings settings, List<string> models)
        {
            var preferredModels = new[] { "qwen", "llama", "mistral", "phi", "gemma" };
            
            string selectedModel = null;
            foreach (var preferred in preferredModels)
            {
                selectedModel = models.FirstOrDefault(m => m.ToLower().Contains(preferred));
                if (selectedModel != null) break;
            }
            
            selectedModel = selectedModel ?? models.First();
            
            if (settings.Provider == AIProvider.Ollama)
            {
                settings.OllamaModel = selectedModel;
                _logger.Info($"Auto-selected Ollama model: {selectedModel}");
            }
            else
            {
                settings.LMStudioModel = selectedModel;
                _logger.Info($"Auto-selected LM Studio model: {selectedModel}");
            }
        }

        private async Task<List<SelectOption>> GetOllamaModelOptions(BrainarrSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.OllamaUrl))
            {
                return GetFallbackOptions(AIProvider.Ollama);
            }

            var models = await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
            if (models.Any())
            {
                return models.Select(m => new SelectOption
                {
                    Value = m,
                    Name = FormatModelName(m)
                }).ToList();
            }

            return GetFallbackOptions(AIProvider.Ollama);
        }

        private async Task<List<SelectOption>> GetLMStudioModelOptions(BrainarrSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.LMStudioUrl))
            {
                return GetFallbackOptions(AIProvider.LMStudio);
            }

            var models = await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
            if (models.Any())
            {
                return models.Select(m => new SelectOption
                {
                    Value = m,
                    Name = FormatModelName(m)
                }).ToList();
            }

            return GetFallbackOptions(AIProvider.LMStudio);
        }

        private List<SelectOption> GetStaticModelOptions(Type enumType)
        {
            return Enum.GetValues(enumType)
                .Cast<Enum>()
                .Select(value => new SelectOption
                {
                    Value = value.ToString(),
                    Name = FormatEnumName(value.ToString())
                }).ToList();
        }

        private List<SelectOption> GetFallbackOptions(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => new List<SelectOption>
                {
                    new() { Value = "qwen2.5:latest", Name = "Qwen 2.5 (Recommended)" },
                    new() { Value = "qwen2.5:7b", Name = "Qwen 2.5 7B" },
                    new() { Value = "llama3.2:latest", Name = "Llama 3.2" },
                    new() { Value = "mistral:latest", Name = "Mistral" }
                },
                AIProvider.LMStudio => new List<SelectOption>
                {
                    new() { Value = "local-model", Name = "Currently Loaded Model" }
                },
                _ => new List<SelectOption>()
            };
        }

        private string FormatEnumName(string enumValue)
        {
            return enumValue
                .Replace("_", " ")
                .Replace("GPT4o", "GPT-4o")
                .Replace("Claude35", "Claude 3.5")
                .Replace("Claude3", "Claude 3")
                .Replace("Llama33", "Llama 3.3")
                .Replace("Llama32", "Llama 3.2")
                .Replace("Llama31", "Llama 3.1")
                .Replace("Gemini15", "Gemini 1.5")
                .Replace("Gemini20", "Gemini 2.0");
        }

        private string FormatModelName(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return "Unknown Model";
            
            if (modelId.Contains("/"))
            {
                var parts = modelId.Split('/');
                if (parts.Length >= 2)
                {
                    var org = parts[0];
                    var modelName = CleanModelName(parts[1]);
                    return $"{modelName} ({org})";
                }
            }
            
            if (modelId.Contains(":"))
            {
                var parts = modelId.Split(':');
                if (parts.Length >= 2)
                {
                    var modelName = CleanModelName(parts[0]);
                    var tag = parts[1];
                    return $"{modelName}:{tag}";
                }
            }
            
            return CleanModelName(modelId);
        }

        private string CleanModelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            
            var cleaned = name
                .Replace("-", " ")
                .Replace("_", " ")
                .Replace(".", " ");
            
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bqwen\b", "Qwen", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bllama\b", "Llama", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bmistral\b", "Mistral", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bgemma\b", "Gemma", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bphi\b", "Phi", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
            
            return cleaned;
        }
    }
}