using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Utils;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Concrete implementation of IBrainarrActionHandler that processes UI actions
    /// for AI provider configuration. Handles model detection for local providers
    /// and provides formatted options for cloud providers.
    /// </summary>
    /// <remarks>
    /// This handler is specifically designed for the Lidarr UI integration,
    /// processing dynamic actions that populate dropdown menus and validate configurations.
    /// It ensures graceful degradation when providers are unavailable.
    /// </remarks>
    public class BrainarrActionHandler : IBrainarrActionHandler
    {
        private readonly IHttpClient _httpClient;
        private readonly ModelDetectionService _modelDetection;
        private readonly Logger _logger;

        /// <summary>
        /// Initializes a new instance of the BrainarrActionHandler.
        /// </summary>
        /// <param name="httpClient">HTTP client for making provider API calls</param>
        /// <param name="modelDetection">Service for detecting available models on local providers</param>
        /// <param name="logger">Logger instance for debugging and error tracking</param>
        public BrainarrActionHandler(
            IHttpClient httpClient,
            ModelDetectionService modelDetection,
            Logger logger)
        {
            _httpClient = httpClient;
            _modelDetection = modelDetection;
            _logger = logger;
        }

        /// <summary>
        /// Handles dynamic UI actions from the Lidarr configuration interface.
        /// Routes action requests to appropriate model detection or option retrieval methods.
        /// </summary>
        /// <param name="action">The action to perform (e.g., "getOllamaModels", "getOpenAIModels")</param>
        /// <param name="query">Query parameters containing configuration data like baseUrl</param>
        /// <returns>An object containing model options formatted for UI consumption</returns>
        /// <remarks>
        /// For local providers (Ollama/LM Studio), performs live model detection.
        /// For cloud providers, returns static enum-based model lists.
        /// All exceptions are caught and logged to ensure UI stability.
        /// </remarks>
        public object HandleAction(string action, IDictionary<string, string> query)
        {
            try
            {
                _logger.Debug($"Handling action: {action}");
                
                return action switch
                {
                    "getOllamaModels" => GetOllamaModelOptions(query),
                    "getLMStudioModels" => GetLMStudioModelOptions(query),
                    "getOpenAIModels" => GetStaticModelOptions(typeof(OpenAIModelKind)),
                    "getAnthropicModels" => GetStaticModelOptions(typeof(AnthropicModelKind)),
                    "getGeminiModels" => GetStaticModelOptions(typeof(GeminiModelKind)),
                    "getGroqModels" => GetStaticModelOptions(typeof(GroqModelKind)),
                    "getDeepSeekModels" => GetStaticModelOptions(typeof(DeepSeekModelKind)),
                    "getPerplexityModels" => GetStaticModelOptions(typeof(PerplexityModelKind)),
                    "getOpenRouterModels" => GetStaticModelOptions(typeof(OpenRouterModelKind)),
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

        /// <summary>
        /// Gets available model options for a specific AI provider.
        /// Uses enum reflection to generate options for cloud providers.
        /// </summary>
        /// <param name="provider">The AI provider name</param>
        /// <returns>An object containing formatted model options</returns>
        public object GetModelOptions(string provider)
        {
            var providerEnum = Enum.Parse<AIProvider>(provider);
            
            return providerEnum switch
            {
                AIProvider.OpenAI => GetStaticModelOptions(typeof(OpenAIModelKind)),
                AIProvider.Anthropic => GetStaticModelOptions(typeof(AnthropicModelKind)),
                AIProvider.Gemini => GetStaticModelOptions(typeof(GeminiModelKind)),
                AIProvider.Groq => GetStaticModelOptions(typeof(GroqModelKind)),
                AIProvider.DeepSeek => GetStaticModelOptions(typeof(DeepSeekModelKind)),
                AIProvider.Perplexity => GetStaticModelOptions(typeof(PerplexityModelKind)),
                AIProvider.OpenRouter => GetStaticModelOptions(typeof(OpenRouterModelKind)),
                _ => new { options = new List<object>() }
            };
        }

        /// <summary>
        /// Gets fallback model options for providers that support failover.
        /// Currently returns the same options as GetModelOptions for simplicity.
        /// </summary>
        /// <param name="provider">The AI provider name</param>
        /// <returns>Fallback model options, identical to primary options</returns>
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
                        name = ModelNameFormatter.FormatModelName(model)
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
                        name = ModelNameFormatter.FormatModelName(model)
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
                    name = ModelNameFormatter.FormatEnumName(e.ToString())
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

        // Formatting moved to ModelNameFormatter utility to avoid duplication
    }
}
