using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Common.Http;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class BrainarrActionHandler : IBrainarrActionHandler
    {
        private readonly IHttpClient _httpClient;
        private readonly ModelDetectionService _modelDetection;
        private readonly Logger _logger;

        public BrainarrActionHandler(
            IHttpClient httpClient,
            ModelDetectionService modelDetection,
            Logger logger)
        {
            _httpClient = httpClient;
            _modelDetection = modelDetection;
            _logger = logger;
        }

        public object HandleAction(string action, IDictionary<string, string> query)
        {
            try
            {
                _logger.Debug($"Handling action: {action}");
                
                return action switch
                {
                    "getOllamaModels" => GetOllamaModelOptions(query),
                    "getLMStudioModels" => GetLMStudioModelOptions(query),
                    "getOpenAIModels" => GetStaticModelOptions(typeof(OpenAIModel)),
                    "getAnthropicModels" => GetStaticModelOptions(typeof(AnthropicModel)),
                    "getGeminiModels" => GetStaticModelOptions(typeof(GeminiModel)),
                    "getGroqModels" => GetStaticModelOptions(typeof(GroqModel)),
                    "getDeepSeekModels" => GetStaticModelOptions(typeof(DeepSeekModel)),
                    "getPerplexityModels" => GetStaticModelOptions(typeof(PerplexityModel)),
                    "getOpenRouterModels" => GetStaticModelOptions(typeof(OpenRouterModel)),
                    "getOllamaFallbackModels" => GetOllamaFallbackOptions(query),
                    "getLMStudioFallbackModels" => GetLMStudioFallbackOptions(query),
                    _ => new { options = new List<object>() }
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to handle action: {action}");
                return new { options = new List<object>() };
            }
        }

        public object GetModelOptions(string provider)
        {
            var providerEnum = Enum.Parse<AIProvider>(provider);
            
            return providerEnum switch
            {
                AIProvider.OpenAI => GetStaticModelOptions(typeof(OpenAIModel)),
                AIProvider.Anthropic => GetStaticModelOptions(typeof(AnthropicModel)),
                AIProvider.Gemini => GetStaticModelOptions(typeof(GeminiModel)),
                AIProvider.Groq => GetStaticModelOptions(typeof(GroqModel)),
                AIProvider.DeepSeek => GetStaticModelOptions(typeof(DeepSeekModel)),
                AIProvider.Perplexity => GetStaticModelOptions(typeof(PerplexityModel)),
                AIProvider.OpenRouter => GetStaticModelOptions(typeof(OpenRouterModel)),
                _ => new { options = new List<object>() }
            };
        }

        public object GetFallbackModelOptions(string provider)
        {
            return GetModelOptions(provider);
        }

        private object GetOllamaModelOptions(IDictionary<string, string> query)
        {
            try
            {
                var baseUrl = query.ContainsKey("baseUrl") ? query["baseUrl"] : "http://localhost:11434";
                
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    return GetDefaultOllamaOptions();
                }

                var models = _modelDetection.DetectOllamaModelsAsync(baseUrl)
                    .GetAwaiter()
                    .GetResult();

                if (models != null && models.Any())
                {
                    var options = models.Select(model => new
                    {
                        value = model,
                        name = FormatModelName(model)
                    }).ToList();

                    _logger.Info($"Detected {options.Count} Ollama models");
                    return new { options };
                }

                return GetDefaultOllamaOptions();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to detect Ollama models, using defaults");
                return GetDefaultOllamaOptions();
            }
        }

        private object GetLMStudioModelOptions(IDictionary<string, string> query)
        {
            try
            {
                var baseUrl = query.ContainsKey("baseUrl") ? query["baseUrl"] : "http://localhost:1234";
                
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    return GetDefaultLMStudioOptions();
                }

                var models = _modelDetection.DetectLMStudioModelsAsync(baseUrl)
                    .GetAwaiter()
                    .GetResult();

                if (models != null && models.Any())
                {
                    var options = models.Select(model => new
                    {
                        value = model,
                        name = FormatModelName(model)
                    }).ToList();

                    _logger.Info($"Detected {options.Count} LM Studio models");
                    return new { options };
                }

                return GetDefaultLMStudioOptions();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to detect LM Studio models, using defaults");
                return GetDefaultLMStudioOptions();
            }
        }

        private object GetStaticModelOptions(Type enumType)
        {
            var options = Enum.GetValues(enumType)
                .Cast<Enum>()
                .Select(e => new
                {
                    value = e.ToString(),
                    name = FormatEnumName(e.ToString())
                })
                .ToList();

            return new { options };
        }

        private object GetOllamaFallbackOptions(IDictionary<string, string> query)
        {
            var result = GetOllamaModelOptions(query);
            
            if (result is IDictionary<string, object> dict && 
                dict.ContainsKey("options") && 
                dict["options"] is List<object> options && 
                options.Any())
            {
                var fallbackOptions = new List<object>
                {
                    new { value = "", name = "None (Disable Fallback)" }
                };
                fallbackOptions.AddRange(options);
                return new { options = fallbackOptions };
            }

            return GetDefaultOllamaFallbackOptions();
        }

        private object GetLMStudioFallbackOptions(IDictionary<string, string> query)
        {
            var result = GetLMStudioModelOptions(query);
            
            if (result is IDictionary<string, object> dict && 
                dict.ContainsKey("options") && 
                dict["options"] is List<object> options && 
                options.Any())
            {
                var fallbackOptions = new List<object>
                {
                    new { value = "", name = "None (Disable Fallback)" }
                };
                fallbackOptions.AddRange(options);
                return new { options = fallbackOptions };
            }

            return GetDefaultLMStudioFallbackOptions();
        }

        private object GetDefaultOllamaOptions()
        {
            var options = new[]
            {
                new { value = "llama3.1", name = "Llama 3.1 (Latest)" },
                new { value = "llama3", name = "Llama 3" },
                new { value = "llama2", name = "Llama 2" },
                new { value = "mistral", name = "Mistral" },
                new { value = "mixtral", name = "Mixtral" },
                new { value = "qwen2", name = "Qwen 2" },
                new { value = "gemma2", name = "Gemma 2" },
                new { value = "phi3", name = "Phi 3" }
            };

            return new { options };
        }

        private object GetDefaultLMStudioOptions()
        {
            var options = new[]
            {
                new { value = "local-model", name = "Local Model (Default)" },
                new { value = "loaded-model", name = "Currently Loaded Model" }
            };

            return new { options };
        }

        private object GetDefaultOllamaFallbackOptions()
        {
            var options = new[]
            {
                new { value = "", name = "None (Disable Fallback)" },
                new { value = "llama3.1", name = "Llama 3.1 (Latest)" },
                new { value = "llama3", name = "Llama 3" },
                new { value = "llama2", name = "Llama 2" },
                new { value = "mistral", name = "Mistral" },
                new { value = "gemma2", name = "Gemma 2" }
            };

            return new { options };
        }

        private object GetDefaultLMStudioFallbackOptions()
        {
            var options = new[]
            {
                new { value = "", name = "None (Disable Fallback)" },
                new { value = "local-model", name = "Local Model (Default)" }
            };

            return new { options };
        }

        private string FormatEnumName(string enumValue)
        {
            if (string.IsNullOrEmpty(enumValue))
                return enumValue;

            var formatted = enumValue
                .Replace("_", " ")
                .Replace("-", " ");

            if (formatted.StartsWith("gpt", StringComparison.OrdinalIgnoreCase))
            {
                formatted = formatted.ToUpper();
            }
            else if (formatted.Contains("claude", StringComparison.OrdinalIgnoreCase))
            {
                formatted = System.Text.RegularExpressions.Regex.Replace(
                    formatted, 
                    @"\bclaude\b", 
                    "Claude", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            return formatted;
        }

        private string FormatModelName(string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                return modelId;

            var name = CleanModelName(modelId);

            if (name.Contains(':'))
            {
                var parts = name.Split(':');
                var modelName = parts[0];
                var tag = parts.Length > 1 ? parts[1] : "";

                modelName = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                    .ToTitleCase(modelName.Replace("-", " ").Replace("_", " "));

                if (!string.IsNullOrEmpty(tag) && tag != "latest")
                {
                    var size = ExtractModelSize(tag);
                    if (!string.IsNullOrEmpty(size))
                    {
                        modelName = $"{modelName} ({size})";
                    }
                    else
                    {
                        modelName = $"{modelName} ({tag})";
                    }
                }

                return modelName;
            }

            return System.Globalization.CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(name.Replace("-", " ").Replace("_", " "));
        }

        private string CleanModelName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            name = name.Trim();

            var pathSeparators = new[] { '/', '\\' };
            foreach (var separator in pathSeparators)
            {
                if (name.Contains(separator))
                {
                    var parts = name.Split(separator);
                    name = parts[parts.Length - 1];
                }
            }

            name = System.Text.RegularExpressions.Regex.Replace(name, @"\.gguf$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return name;
        }

        private string ExtractModelSize(string tag)
        {
            var sizePattern = @"(\d+\.?\d*[bB])";
            var match = System.Text.RegularExpressions.Regex.Match(tag, sizePattern);
            
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpper();
            }

            if (tag.Contains("7b", StringComparison.OrdinalIgnoreCase)) return "7B";
            if (tag.Contains("13b", StringComparison.OrdinalIgnoreCase)) return "13B";
            if (tag.Contains("30b", StringComparison.OrdinalIgnoreCase)) return "30B";
            if (tag.Contains("70b", StringComparison.OrdinalIgnoreCase)) return "70B";

            return null;
        }
    }
}