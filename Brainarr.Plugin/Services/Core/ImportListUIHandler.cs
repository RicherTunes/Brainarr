using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Handles UI actions and model discovery for the import list
    /// </summary>
    public class ImportListUIHandler : IImportListUIHandler
    {
        private readonly ModelDetectionService _modelDetection;
        private readonly Logger _logger;

        public ImportListUIHandler(ModelDetectionService modelDetection, Logger logger)
        {
            _modelDetection = modelDetection;
            _logger = logger;
        }

        public object HandleAction(string action, BrainarrSettings settings, IDictionary<string, string> query = null)
        {
            _logger.Info($"HandleAction called with action: {action}");
            
            if (action == "getModelOptions")
            {
                _logger.Info($"HandleAction: getModelOptions called for provider: {settings.Provider}");
                return GetModelOptionsAsync(settings).GetAwaiter().GetResult();
            }

            // Legacy support for old method names (but only if current provider matches)
            if (action == "getOllamaOptions" && settings.Provider == AIProvider.Ollama)
            {
                return GetOllamaModelOptionsAsync(settings.OllamaUrl).GetAwaiter().GetResult();
            }

            if (action == "getLMStudioOptions" && settings.Provider == AIProvider.LMStudio)
            {
                return GetLMStudioModelOptionsAsync(settings.LMStudioUrl).GetAwaiter().GetResult();
            }

            _logger.Info($"HandleAction: Unknown action '{action}' or provider mismatch, returning empty object");
            return new { };
        }

        public async Task<object> GetModelOptionsAsync(BrainarrSettings settings)
        {
            return settings.Provider switch
            {
                AIProvider.Ollama => await GetOllamaModelOptionsAsync(settings.OllamaUrl),
                AIProvider.LMStudio => await GetLMStudioModelOptionsAsync(settings.LMStudioUrl),
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

        public async Task<object> GetOllamaModelOptionsAsync(string ollamaUrl)
        {
            _logger.Info("Getting Ollama model options");
            
            if (string.IsNullOrWhiteSpace(ollamaUrl))
            {
                _logger.Info("OllamaUrl is empty, returning fallback options");
                return GetOllamaFallbackOptions();
            }

            try
            {
                var models = await _modelDetection.GetOllamaModelsAsync(ollamaUrl);

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} Ollama models");
                    var options = models.Select(model => new
                    {
                        Value = model,
                        Name = FormatModelName(model)
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

        public async Task<object> GetLMStudioModelOptionsAsync(string lmStudioUrl)
        {
            _logger.Info("Getting LM Studio model options");
            
            if (string.IsNullOrWhiteSpace(lmStudioUrl))
            {
                _logger.Info("LMStudioUrl is empty, returning fallback options");
                return GetLMStudioFallbackOptions();
            }

            try
            {
                var models = await _modelDetection.GetLMStudioModelsAsync(lmStudioUrl);

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} LM Studio models");
                    var options = models.Select(model => new
                    {
                        Value = model,
                        Name = FormatModelName(model)
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

        public object GetStaticModelOptions(Type enumType)
        {
            var options = Enum.GetValues(enumType)
                .Cast<Enum>()
                .Select(value => new
                {
                    Value = value.ToString(),
                    Name = FormatEnumName(value.ToString())
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

        private string FormatEnumName(string enumValue)
        {
            // Convert enum value to readable name
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
            
            // For LM Studio models with path separators
            if (modelId.Contains("/"))
            {
                var parts = modelId.Split('/');
                if (parts.Length >= 2)
                {
                    var org = parts[0];
                    var modelName = parts[1];
                    var cleanName = CleanModelName(modelName);
                    return $"{cleanName} ({org})";
                }
            }
            
            // For Ollama models with colons
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
            
            // Capitalize known model families
            cleaned = Regex.Replace(cleaned, @"\bqwen\b", "Qwen", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bllama\b", "Llama", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bmistral\b", "Mistral", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bgemma\b", "Gemma", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bphi\b", "Phi", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bcoder\b", "Coder", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\binstruct\b", "Instruct", RegexOptions.IgnoreCase);
            
            // Clean up multiple spaces
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            
            return cleaned;
        }
    }
}