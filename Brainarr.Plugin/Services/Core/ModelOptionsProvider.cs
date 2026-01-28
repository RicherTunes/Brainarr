using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Provides model options for AI providers, including dynamic detection for local providers
    /// and static options for cloud providers.
    /// </summary>
    public class ModelOptionsProvider : IModelOptionsProvider
    {
        private readonly Logger _logger;
        private readonly IModelDetectionService _modelDetection;

        public ModelOptionsProvider(
            Logger logger,
            IModelDetectionService modelDetection)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modelDetection = modelDetection ?? throw new ArgumentNullException(nameof(modelDetection));
        }

        /// <summary>
        /// Gets model options for the specified provider, with optional query parameter overrides.
        /// </summary>
        /// <param name="settings">Current settings containing provider and URLs.</param>
        /// <param name="query">Optional query parameters to override provider/baseUrl.</param>
        /// <returns>Object with options array containing { value, name } pairs.</returns>
        public async Task<object> GetModelOptionsAsync(BrainarrSettings settings, IDictionary<string, string> query = null)
        {
            var effectiveProvider = settings.Provider;
            var ollamaUrl = settings.OllamaUrl;
            var lmUrl = settings.LMStudioUrl;

            // Allow overrides via query for unsaved UI changes
            if (query != null)
            {
                if (query.TryGetValue("provider", out var p) && Enum.TryParse<AIProvider>(p, out var parsed))
                {
                    effectiveProvider = parsed;
                }

                if (query.TryGetValue("baseUrl", out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
                {
                    if (effectiveProvider == AIProvider.Ollama) ollamaUrl = baseUrl;
                    if (effectiveProvider == AIProvider.LMStudio) lmUrl = baseUrl;
                }
            }

            // Local providers - dynamic model detection
            if (effectiveProvider == AIProvider.Ollama)
            {
                var models = await _modelDetection.GetOllamaModelsAsync(ollamaUrl);
                if (models != null && models.Any())
                {
                    return new
                    {
                        options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList()
                    };
                }
                return GetFallbackOptions(AIProvider.Ollama);
            }
            else if (effectiveProvider == AIProvider.LMStudio)
            {
                var models = await _modelDetection.GetLMStudioModelsAsync(lmUrl);
                if (models != null && models.Any())
                {
                    return new
                    {
                        options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList()
                    };
                }
                return GetFallbackOptions(AIProvider.LMStudio);
            }

            // Cloud providers - static options
            return GetStaticModelOptions(effectiveProvider);
        }

        /// <summary>
        /// Detects available models for the specified provider, with optional query parameter overrides.
        /// </summary>
        /// <param name="settings">Current settings containing provider and URLs.</param>
        /// <param name="query">Optional query parameters to override provider/baseUrl.</param>
        /// <returns>Object with options array containing detected models.</returns>
        public async Task<object> DetectModelsAsync(BrainarrSettings settings, IDictionary<string, string> query = null)
        {
            var effectiveProvider = settings.Provider;
            var ollamaUrl = settings.OllamaUrl;
            var lmUrl = settings.LMStudioUrl;

            // Allow overrides via query
            if (query != null)
            {
                if (query.TryGetValue("provider", out var p) && Enum.TryParse<AIProvider>(p, out var parsed))
                {
                    effectiveProvider = parsed;
                }

                if (query.TryGetValue("baseUrl", out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
                {
                    if (effectiveProvider == AIProvider.Ollama) ollamaUrl = baseUrl;
                    if (effectiveProvider == AIProvider.LMStudio) lmUrl = baseUrl;
                }
            }

            if (effectiveProvider == AIProvider.Ollama)
            {
                var models = await _modelDetection.GetOllamaModelsAsync(ollamaUrl);
                return new { options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList() };
            }
            else if (effectiveProvider == AIProvider.LMStudio)
            {
                var models = await _modelDetection.GetLMStudioModelsAsync(lmUrl);
                return new { options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList() };
            }

            return new { options = Array.Empty<object>() };
        }

        /// <summary>
        /// Gets static model options for cloud providers based on enum values.
        /// </summary>
        private static object GetStaticModelOptions(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.OpenAI => BuildEnumOptions<OpenAIModelKind>(),
                AIProvider.Anthropic => BuildEnumOptions<AnthropicModelKind>(),
                AIProvider.Perplexity => BuildEnumOptions<PerplexityModelKind>(),
                AIProvider.OpenRouter => BuildEnumOptions<OpenRouterModelKind>(),
                AIProvider.DeepSeek => BuildEnumOptions<DeepSeekModelKind>(),
                AIProvider.Gemini => BuildEnumOptions<GeminiModelKind>(),
                AIProvider.Groq => BuildEnumOptions<GroqModelKind>(),
                _ => new { options = Array.Empty<object>() }
            };
        }

        /// <summary>
        /// Builds options from enum values for cloud providers.
        /// </summary>
        private static object BuildEnumOptions<TEnum>() where TEnum : Enum
        {
            var options = Enum.GetValues(typeof(TEnum))
                .Cast<Enum>()
                .Select(v => new { value = v.ToString(), name = FormatEnumName(v.ToString()) })
                .ToList();

            return new { options };
        }

        /// <summary>
        /// Gets fallback model options when detection fails for local providers.
        /// </summary>
        private static object GetFallbackOptions(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => new
                {
                    options = new[]
                    {
                        new { value = "qwen2.5:latest", name = "Qwen 2.5 (Recommended)" },
                        new { value = "qwen2.5:7b", name = "Qwen 2.5 7B" },
                        new { value = "llama3.2:latest", name = "Llama 3.2" },
                        new { value = "mistral:latest", name = "Mistral" }
                    }
                },
                AIProvider.LMStudio => new
                {
                    options = new[]
                    {
                        new { value = "local-model", name = "Currently Loaded Model" }
                    }
                },
                _ => new { options = Array.Empty<object>() }
            };
        }

        /// <summary>
        /// Formats a model name for display.
        /// </summary>
        private static string FormatModelName(string modelId)
        {
            return ModelNameFormatter.FormatModelName(modelId);
        }

        /// <summary>
        /// Formats an enum name for display.
        /// </summary>
        private static string FormatEnumName(string enumValue)
        {
            return ModelNameFormatter.FormatEnumName(enumValue);
        }
    }
}
