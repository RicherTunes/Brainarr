using System;
using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public partial class BrainarrSettings
    {
        private string? SanitizeApiKey(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return apiKey;

            // Remove any potential whitespace or control characters
            apiKey = apiKey?.Trim();

            // Basic validation to prevent injection
            if (apiKey != null && apiKey.Length > 500)
            {
                throw new ArgumentException("API key exceeds maximum allowed length");
            }

            return apiKey;
        }

        /// <summary>
        /// Gets provider-specific settings for configuration.
        /// </summary>
        public Dictionary<string, object> GetProviderSettings(AIProvider provider)
        {
            var settings = new Dictionary<string, object>();

            switch (provider)
            {
                case AIProvider.Ollama:
                    settings["url"] = OllamaUrl;
                    settings["model"] = OllamaModel;
                    break;
                case AIProvider.LMStudio:
                    settings["url"] = LMStudioUrl;
                    settings["model"] = LMStudioModel;
                    break;
                case AIProvider.OpenAI:
                    settings["apiKey"] = OpenAIApiKey;
                    settings["model"] = OpenAIModelId;
                    break;
                case AIProvider.Anthropic:
                    settings["apiKey"] = AnthropicApiKey;
                    settings["model"] = AnthropicModelId;
                    break;
                case AIProvider.Perplexity:
                    settings["apiKey"] = PerplexityApiKey;
                    settings["model"] = PerplexityModelId;
                    break;
                case AIProvider.OpenRouter:
                    settings["apiKey"] = OpenRouterApiKey;
                    settings["model"] = OpenRouterModelId;
                    break;
                case AIProvider.DeepSeek:
                    settings["apiKey"] = DeepSeekApiKey;
                    settings["model"] = DeepSeekModelId;
                    break;
                case AIProvider.Gemini:
                    settings["apiKey"] = GeminiApiKey;
                    settings["model"] = GeminiModelId;
                    break;
                case AIProvider.Groq:
                    settings["apiKey"] = GroqApiKey;
                    settings["model"] = GroqModelId;
                    break;
                case AIProvider.ClaudeCodeSubscription:
                    settings["credentialsPath"] = ClaudeCodeCredentialsPath;
                    settings["model"] = ClaudeCodeModelId;
                    break;
                case AIProvider.OpenAICodexSubscription:
                    settings["credentialsPath"] = OpenAICodexCredentialsPath;
                    settings["model"] = OpenAICodexModelId;
                    break;
                case AIProvider.ClaudeCodeCli:
                    // CLI auth is handled by the CLI itself — no credentials file path applies.
                    settings["model"] = ClaudeCodeModelId;
                    break;
            }

            return settings;
        }


        /// <summary>
        /// Gets the default model for a specific provider.
        /// </summary>
        private string? GetCurrentProviderModel()
        {
            return Provider switch
            {
                AIProvider.Ollama => _ollamaModel,
                AIProvider.LMStudio => _lmStudioModel,
                AIProvider.Perplexity => ProviderModelNormalizer.Normalize(AIProvider.Perplexity, string.IsNullOrEmpty(PerplexityModelId) ? BrainarrConstants.DefaultPerplexityModel : PerplexityModelId),
                AIProvider.OpenAI => ProviderModelNormalizer.Normalize(AIProvider.OpenAI, string.IsNullOrEmpty(OpenAIModelId) ? BrainarrConstants.DefaultOpenAIModel : OpenAIModelId),
                AIProvider.Anthropic => ProviderModelNormalizer.Normalize(AIProvider.Anthropic, string.IsNullOrEmpty(AnthropicModelId) ? BrainarrConstants.DefaultAnthropicModel : AnthropicModelId),
                AIProvider.OpenRouter => ProviderModelNormalizer.Normalize(AIProvider.OpenRouter, string.IsNullOrEmpty(OpenRouterModelId) ? BrainarrConstants.DefaultOpenRouterModel : OpenRouterModelId),
                AIProvider.DeepSeek => ProviderModelNormalizer.Normalize(AIProvider.DeepSeek, string.IsNullOrEmpty(DeepSeekModelId) ? BrainarrConstants.DefaultDeepSeekModel : DeepSeekModelId),
                AIProvider.Gemini => ProviderModelNormalizer.Normalize(AIProvider.Gemini, string.IsNullOrEmpty(GeminiModelId) ? BrainarrConstants.DefaultGeminiModel : GeminiModelId),
                AIProvider.Groq => ProviderModelNormalizer.Normalize(AIProvider.Groq, string.IsNullOrEmpty(GroqModelId) ? BrainarrConstants.DefaultGroqModel : GroqModelId),
                AIProvider.ClaudeCodeSubscription => string.IsNullOrEmpty(ClaudeCodeModelId) ? BrainarrConstants.DefaultClaudeCodeModel : ClaudeCodeModelId,
                AIProvider.OpenAICodexSubscription => string.IsNullOrEmpty(OpenAICodexModelId) ? BrainarrConstants.DefaultOpenAICodexModel : OpenAICodexModelId,
                AIProvider.ClaudeCodeCli => string.IsNullOrEmpty(ClaudeCodeModelId) ? "sonnet" : ClaudeCodeModelId,
                _ => null
            };
        }

        private void ClearCurrentProviderModel()
        {
            // Clear the model for the current provider to reset to default
            switch (_provider)
            {
                case AIProvider.Ollama:
                    _ollamaModel = null;
                    break;
                case AIProvider.LMStudio:
                    _lmStudioModel = null;
                    break;
                case AIProvider.Perplexity:
                    PerplexityModelId = null;
                    break;
                case AIProvider.OpenAI:
                    OpenAIModelId = null;
                    break;
                case AIProvider.Anthropic:
                    AnthropicModelId = null;
                    break;
                case AIProvider.OpenRouter:
                    OpenRouterModelId = null;
                    break;
                case AIProvider.DeepSeek:
                    DeepSeekModelId = null;
                    break;
                case AIProvider.Gemini:
                    GeminiModelId = null;
                    break;
                case AIProvider.Groq:
                    GroqModelId = null;
                    break;
                case AIProvider.ClaudeCodeSubscription:
                    ClaudeCodeModelId = null;
                    break;
                case AIProvider.OpenAICodexSubscription:
                    OpenAICodexModelId = null;
                    break;
                case AIProvider.ClaudeCodeCli:
                    ClaudeCodeModelId = null;
                    break;
            }
        }

        private void ClearPreviousProviderModel()
        {
            // Clear the model for the previous provider when switching away
            if (_previousProvider.HasValue)
            {
                switch (_previousProvider.Value)
                {
                    case AIProvider.Ollama:
                        _ollamaModel = null;
                        break;
                    case AIProvider.LMStudio:
                        _lmStudioModel = null;
                        break;
                    case AIProvider.Perplexity:
                        PerplexityModelId = null;
                        break;
                    case AIProvider.OpenAI:
                        OpenAIModelId = null;
                        break;
                    case AIProvider.Anthropic:
                        AnthropicModelId = null;
                        break;
                    case AIProvider.OpenRouter:
                        OpenRouterModelId = null;
                        break;
                    case AIProvider.DeepSeek:
                        DeepSeekModelId = null;
                        break;
                    case AIProvider.Gemini:
                        GeminiModelId = null;
                        break;
                    case AIProvider.Groq:
                        GroqModelId = null;
                        break;
                    case AIProvider.ClaudeCodeSubscription:
                        ClaudeCodeModelId = null;
                        break;
                    case AIProvider.OpenAICodexSubscription:
                        OpenAICodexModelId = null;
                        break;
                    case AIProvider.ClaudeCodeCli:
                        ClaudeCodeModelId = null;
                        break;
                }
            }
        }

        private string GetDefaultModelForProvider(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => BrainarrConstants.DefaultOllamaModel,
                AIProvider.LMStudio => BrainarrConstants.DefaultLMStudioModel,
                AIProvider.Perplexity => ProviderModelNormalizer.Normalize(AIProvider.Perplexity, string.IsNullOrEmpty(PerplexityModelId) ? BrainarrConstants.DefaultPerplexityModel : PerplexityModelId),
                AIProvider.OpenAI => ProviderModelNormalizer.Normalize(AIProvider.OpenAI, string.IsNullOrEmpty(OpenAIModelId) ? BrainarrConstants.DefaultOpenAIModel : OpenAIModelId),
                AIProvider.Anthropic => ProviderModelNormalizer.Normalize(AIProvider.Anthropic, string.IsNullOrEmpty(AnthropicModelId) ? BrainarrConstants.DefaultAnthropicModel : AnthropicModelId),
                AIProvider.OpenRouter => ProviderModelNormalizer.Normalize(AIProvider.OpenRouter, string.IsNullOrEmpty(OpenRouterModelId) ? BrainarrConstants.DefaultOpenRouterModel : OpenRouterModelId),
                AIProvider.DeepSeek => ProviderModelNormalizer.Normalize(AIProvider.DeepSeek, string.IsNullOrEmpty(DeepSeekModelId) ? BrainarrConstants.DefaultDeepSeekModel : DeepSeekModelId),
                AIProvider.Gemini => ProviderModelNormalizer.Normalize(AIProvider.Gemini, string.IsNullOrEmpty(GeminiModelId) ? BrainarrConstants.DefaultGeminiModel : GeminiModelId),
                AIProvider.Groq => ProviderModelNormalizer.Normalize(AIProvider.Groq, string.IsNullOrEmpty(GroqModelId) ? BrainarrConstants.DefaultGroqModel : GroqModelId),
                AIProvider.ClaudeCodeSubscription => BrainarrConstants.DefaultClaudeCodeModel,
                AIProvider.OpenAICodexSubscription => BrainarrConstants.DefaultOpenAICodexModel,
                AIProvider.ClaudeCodeCli => "sonnet",
                _ => "Default"
            };
        }

        // Polymorphic methods to get/set provider-specific configuration
        public string? GetModelForProvider()
        {
            return Provider switch
            {
                AIProvider.Ollama => OllamaModel,
                AIProvider.LMStudio => LMStudioModel,
                AIProvider.OpenAI => ProviderModelNormalizer.Normalize(AIProvider.OpenAI, string.IsNullOrEmpty(OpenAIModelId) ? BrainarrConstants.DefaultOpenAIModel : OpenAIModelId),
                AIProvider.Anthropic => ProviderModelNormalizer.Normalize(AIProvider.Anthropic, string.IsNullOrEmpty(AnthropicModelId) ? BrainarrConstants.DefaultAnthropicModel : AnthropicModelId),
                AIProvider.Perplexity => ProviderModelNormalizer.Normalize(AIProvider.Perplexity, string.IsNullOrEmpty(PerplexityModelId) ? BrainarrConstants.DefaultPerplexityModel : PerplexityModelId),
                AIProvider.OpenRouter => ProviderModelNormalizer.Normalize(AIProvider.OpenRouter, string.IsNullOrEmpty(OpenRouterModelId) ? BrainarrConstants.DefaultOpenRouterModel : OpenRouterModelId),
                AIProvider.DeepSeek => ProviderModelNormalizer.Normalize(AIProvider.DeepSeek, string.IsNullOrEmpty(DeepSeekModelId) ? BrainarrConstants.DefaultDeepSeekModel : DeepSeekModelId),
                AIProvider.Gemini => ProviderModelNormalizer.Normalize(AIProvider.Gemini, string.IsNullOrEmpty(GeminiModelId) ? BrainarrConstants.DefaultGeminiModel : GeminiModelId),
                AIProvider.Groq => ProviderModelNormalizer.Normalize(AIProvider.Groq, string.IsNullOrEmpty(GroqModelId) ? BrainarrConstants.DefaultGroqModel : GroqModelId),
                AIProvider.ClaudeCodeSubscription => string.IsNullOrEmpty(ClaudeCodeModelId) ? BrainarrConstants.DefaultClaudeCodeModel : ClaudeCodeModelId,
                AIProvider.OpenAICodexSubscription => string.IsNullOrEmpty(OpenAICodexModelId) ? BrainarrConstants.DefaultOpenAICodexModel : OpenAICodexModelId,
                AIProvider.ClaudeCodeCli => string.IsNullOrEmpty(ClaudeCodeModelId) ? "sonnet" : ClaudeCodeModelId,
                _ => null
            };
        }

        public void SetModelForProvider(string? model)
        {
            switch (Provider)
            {
                case AIProvider.Ollama:
                    OllamaModel = model;
                    break;
                case AIProvider.LMStudio:
                    LMStudioModel = model;
                    break;
                case AIProvider.OpenAI:
                    OpenAIModelId = model;
                    break;
                case AIProvider.Anthropic:
                    AnthropicModelId = model;
                    break;
                case AIProvider.Perplexity:
                    PerplexityModelId = model;
                    break;
                case AIProvider.OpenRouter:
                    OpenRouterModelId = model;
                    break;
                case AIProvider.DeepSeek:
                    DeepSeekModelId = model;
                    break;
                case AIProvider.Gemini:
                    GeminiModelId = model;
                    break;
                case AIProvider.Groq:
                    GroqModelId = model;
                    break;
                case AIProvider.ClaudeCodeSubscription:
                    ClaudeCodeModelId = model;
                    break;
                case AIProvider.OpenAICodexSubscription:
                    OpenAICodexModelId = model;
                    break;
                case AIProvider.ClaudeCodeCli:
                    ClaudeCodeModelId = model;
                    break;
            }
        }

        public string? GetApiKeyForProvider()
        {
            return Provider switch
            {
                AIProvider.OpenAI => OpenAIApiKey,
                AIProvider.Anthropic => AnthropicApiKey,
                AIProvider.Perplexity => PerplexityApiKey,
                AIProvider.OpenRouter => OpenRouterApiKey,
                AIProvider.DeepSeek => DeepSeekApiKey,
                AIProvider.Gemini => GeminiApiKey,
                AIProvider.Groq => GroqApiKey,
                _ => null
            };
        }

        public string? GetBaseUrlForProvider()
        {
            return Provider switch
            {
                AIProvider.Ollama => OllamaUrl,
                AIProvider.LMStudio => LMStudioUrl,
                _ => null
            };
        }

        private static string NormalizeHttpUrlOrOriginal(string value) => Configuration.UrlValidator.NormalizeHttpUrlOrOriginal(value);

        // ===== Backfill mapping helpers =====

        public IterationProfile GetIterationProfile()
        {
            // Map simple BackfillStrategy to effective iteration settings
            switch (BackfillStrategy)
            {
                case BackfillStrategy.Off:
                    return new IterationProfile
                    {
                        EnableRefinement = false,
                        MaxIterations = 0,
                        ZeroStop = 0,
                        LowStop = 0,
                        CooldownMs = 0,
                        GuaranteeExactTarget = false
                    };

                case BackfillStrategy.Aggressive:
                    return new IterationProfile
                    {
                        EnableRefinement = true,
                        MaxIterations = MaxTopUpIterations > 0 ? MaxTopUpIterations : IterativeMaxIterations,
                        ZeroStop = Math.Max(3, MapZeroStop()),
                        LowStop = Math.Max(8, MapLowStop()),
                        CooldownMs = Math.Min(500, Math.Max(0, IterativeCooldownMs)),
                        GuaranteeExactTarget = true
                    };

                case BackfillStrategy.Standard:
                default:
                    return new IterationProfile
                    {
                        EnableRefinement = true,
                        MaxIterations = MaxTopUpIterations > 0 ? MaxTopUpIterations : IterativeMaxIterations,
                        ZeroStop = Math.Max(1, MapZeroStop()),
                        LowStop = Math.Max(4, MapLowStop()),
                        CooldownMs = IterativeCooldownMs,
                        GuaranteeExactTarget = GuaranteeExactTarget
                    };
            }
        }

        private int MapZeroStop()
        {
            var mapped = TopUpStopSensitivity switch
            {
                StopSensitivity.Strict => 1,
                StopSensitivity.Lenient => 2,
                _ => 1
            };
            return Math.Max(mapped, IterativeZeroSuccessStopThreshold);
        }

        private int MapLowStop()
        {
            var mapped = TopUpStopSensitivity switch
            {
                StopSensitivity.Strict => 1,
                StopSensitivity.Lenient => 4,
                _ => 2
            };
            return Math.Max(mapped, IterativeLowSuccessStopThreshold);
        }

        public bool IsBackfillEnabled() => GetIterationProfile().EnableRefinement;

        /// <summary>
        /// Overall wall-clock budget (ms) for a full recommendation fetch, used as the
        /// <c>SafeAsyncHelper.RunSafeSync</c> timeout. Derived from the per-request AI timeout times
        /// the number of provider calls the run may make (1 initial + each top-up iteration) plus a
        /// fixed overhead for MBID enrichment / validation / dedup. Floored at the legacy 2-minute
        /// default (never shorter than before) and capped at <see cref="BrainarrConstants.MaxOverallFetchTimeoutMs"/>
        /// as a runaway backstop. This is what makes a user's raised "AI Request Timeout" actually
        /// take effect instead of being silently capped at 120s.
        /// </summary>
        public int GetOverallFetchTimeoutMs()
        {
            var perRequest = AIRequestTimeoutSeconds <= 0
                ? BrainarrConstants.DefaultAITimeout
                : AIRequestTimeoutSeconds;

            // Mirror the per-request elevation that RecommendationGenerator/TopUpPlanner apply for
            // local backends (Ollama/LM Studio are slow to first token): when the configured timeout
            // is at/below the conservative default they actually run up to LocalProviderDefaultTimeout.
            // The overall budget must account for that or it would guillotine a single local request.
            var isLocal = Provider == AIProvider.Ollama || Provider == AIProvider.LMStudio;
            if (isLocal && perRequest <= BrainarrConstants.DefaultAITimeout)
            {
                perRequest = BrainarrConstants.LocalProviderDefaultTimeout;
            }

            var ip = GetIterationProfile();
            var topUpIterations = ip.EnableRefinement ? Math.Max(0, ip.MaxIterations) : 0;
            var providerCalls = 1L + topUpIterations; // initial call + each top-up iteration

            var totalSeconds = perRequest * providerCalls + BrainarrConstants.FetchOverheadSeconds;
            var ms = totalSeconds * 1000L;

            if (ms < BrainarrConstants.DefaultAsyncTimeoutMs) ms = BrainarrConstants.DefaultAsyncTimeoutMs;
            if (ms > BrainarrConstants.MaxOverallFetchTimeoutMs) ms = BrainarrConstants.MaxOverallFetchTimeoutMs;
            return (int)ms;
        }

        /// <summary>
        /// Output-token budget (max_tokens) for a single provider request, scaled to the requested
        /// recommendation count so a full list isn't truncated mid-array when the model has time to
        /// finish. Capped at <see cref="BrainarrConstants.MaxOutputTokensCeiling"/>. Note this is the
        /// completion cap, NOT the (much larger) model context window. Truncation past this cap is
        /// still recovered by RecommendationJsonParser's salvage, but budgeting correctly lets a
        /// single request return the whole list when time allows.
        /// </summary>
        public int GetOutputTokenBudget()
        {
            var target = MaxRecommendations > 0 ? MaxRecommendations : BrainarrConstants.DefaultRecommendations;
            long desired = (long)target * BrainarrConstants.OutputTokensPerRecommendation
                + BrainarrConstants.OutputTokensStructuralOverhead;

            // Never budget more output than the model can plausibly generate within the per-request
            // timeout: overshooting gets the HTTP call cancelled mid-stream (no body → nothing to
            // salvage), which is strictly worse than a clean truncation we CAN salvage.
            var perRequest = AIRequestTimeoutSeconds <= 0
                ? BrainarrConstants.DefaultAITimeout
                : AIRequestTimeoutSeconds;
            long timeoutBoundedCeiling = (long)perRequest * BrainarrConstants.ConservativeOutputTokensPerSecond;

            long budget = Math.Min(desired, timeoutBoundedCeiling);
            // Floor at the proven-safe default so behavior is never worse than before, even on short
            // timeouts (a 2000-token GLM response truncates but completes, and salvage recovers it).
            if (budget < BrainarrConstants.DefaultMaxTokens) budget = BrainarrConstants.DefaultMaxTokens;
            if (budget > BrainarrConstants.MaxOutputTokensCeiling) budget = BrainarrConstants.MaxOutputTokensCeiling;
            return (int)budget;
        }

        private static bool IsPerplexityModelValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim();
            var lower = v.Replace('_', '-').ToLowerInvariant();
            if (lower.StartsWith("sonar-")) return true;
            if (lower.StartsWith("llama-3.1-sonar")) return true;
            // Accept enum-style names
            return Enum.TryParse(typeof(PerplexityModelKind), v, ignoreCase: true, out _);
        }

        private static bool LooksLikeLocalModelValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim();
            // LM Studio/Ollama models often contain a slash or a tag (:) or the sentinel "local-model"
            return v.Equals("local-model", StringComparison.OrdinalIgnoreCase) || v.Contains('/') || v.Contains(':');
        }
    }
}
