using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Provides model options for UI dropdowns. Handles live detection for
    /// local providers and static enum mapping for cloud providers.
    /// Extracted from BrainarrOrchestrator (M6-3).
    /// </summary>
    internal class ModelOptionsProvider
    {
        private readonly IModelDetectionService _modelDetection;

        public ModelOptionsProvider(IModelDetectionService modelDetection)
        {
            _modelDetection = modelDetection ?? throw new ArgumentNullException(nameof(modelDetection));
        }

        public async Task<object> GetModelOptionsAsync(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var effectiveProvider = settings.Provider;
            if (query != null && query.TryGetValue("provider", out var p) && Enum.TryParse<AIProvider>(p, out var parsed))
            {
                effectiveProvider = parsed;
            }

            var ollamaUrl = settings.OllamaUrl;
            var lmUrl = settings.LMStudioUrl;
            if (query != null && query.TryGetValue("baseUrl", out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
            {
                if (effectiveProvider == AIProvider.Ollama) ollamaUrl = baseUrl;
                if (effectiveProvider == AIProvider.LMStudio) lmUrl = baseUrl;
            }

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

            return GetStaticModelOptions(effectiveProvider);
        }

        public async Task<object> DetectModelsAsync(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var effectiveProvider = settings.Provider;
            if (query != null && query.TryGetValue("provider", out var p) && Enum.TryParse<AIProvider>(p, out var parsed))
            {
                effectiveProvider = parsed;
            }

            var ollamaUrl = settings.OllamaUrl;
            var lmUrl = settings.LMStudioUrl;
            if (query != null && query.TryGetValue("baseUrl", out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
            {
                if (effectiveProvider == AIProvider.Ollama) ollamaUrl = baseUrl;
                if (effectiveProvider == AIProvider.LMStudio) lmUrl = baseUrl;
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

        internal static object GetStaticModelOptions(AIProvider provider)
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

        internal static object BuildEnumOptions<TEnum>() where TEnum : Enum
        {
            var options = Enum.GetValues(typeof(TEnum))
                .Cast<Enum>()
                .Select(v => new { value = v.ToString(), name = FormatEnumName(v.ToString()) })
                .ToList();

            return new { options };
        }

        internal static object GetFallbackOptions(AIProvider provider)
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

        private static string FormatModelName(string modelId)
        {
            return ModelNameFormatter.FormatModelName(modelId);
        }

        private static string FormatEnumName(string enumValue)
        {
            return ModelNameFormatter.FormatEnumName(enumValue);
        }
    }
}
