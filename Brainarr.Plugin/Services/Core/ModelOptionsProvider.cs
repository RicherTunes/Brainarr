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
                // Z.AI PaaS + Coding Plan: without these the model dropdown rendered EMPTY in the
                // settings UI, so users couldn't pick a model and the unset value fell back to the
                // "default" sentinel (which Z.AI rejects with [1210]). Both enums share GLM ids.
                AIProvider.ZaiGlm => BuildEnumOptions<ZaiGlmModelKind>(),
                AIProvider.ZaiCoding => BuildEnumOptions<ZaiCodingModelKind>(),
                // Codex via ChatGPT subscription: without this case the model dropdown rendered
                // EMPTY (no enum was mapped), so users couldn't pick a model. The values are the
                // ACTUAL backend slugs (with dots) — enum member names can't contain '.'/'-', and
                // the codex ModelSelection getter passes the stored id through verbatim, so a direct
                // slug list needs no ModelIdMapper translation. Set restricted to what the ChatGPT
                // backend accepts for subscription accounts (live-confirmed 2026-08).
                AIProvider.OpenAICodexSubscription => BuildOpenAICodexOptions(),
                _ => new { options = Array.Empty<object>() }
            };
        }

        internal static object BuildOpenAICodexOptions()
        {
            // Slugs come from BrainarrConstants.OpenAICodexModels — the single source of truth shared
            // with the provider's model coercion, so the dropdown can never offer a slug the provider
            // would reject (or vice versa). Labels are display-only.
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-5.6-terra"] = "GPT-5.6 Terra (balanced, default)",
                ["gpt-5.6-sol"] = "GPT-5.6 Sol (flagship)",
                ["gpt-5.6-luna"] = "GPT-5.6 Luna (fast & cheap)",
                ["gpt-5.5"] = "GPT-5.5 (previous generation)",
            };

            return new
            {
                options = BrainarrConstants.OpenAICodexModels
                    .Select(slug => new
                    {
                        value = slug,
                        name = labels.TryGetValue(slug, out var label) ? label : FormatModelName(slug)
                    })
                    .ToList()
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
