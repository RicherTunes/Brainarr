using System;
using System.Collections.Generic;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Registry pattern for managing AI provider creation.
    /// Eliminates switch statements and enables extensibility.
    /// </summary>
    public interface IProviderRegistry
    {
        /// <summary>
        /// Registers a provider factory function.
        /// </summary>
        void Register(AIProvider type, Func<BrainarrSettings, IHttpClient, Logger, IAIProvider> factory);

        /// <summary>
        /// Creates a provider instance.
        /// </summary>
        IAIProvider CreateProvider(AIProvider type, BrainarrSettings settings, IHttpClient httpClient, Logger logger);

        /// <summary>
        /// Checks if a provider type is registered.
        /// </summary>
        bool IsRegistered(AIProvider type);

        /// <summary>
        /// Gets all registered provider types.
        /// </summary>
        IEnumerable<AIProvider> GetRegisteredProviders();
    }

    public class ProviderRegistry : IProviderRegistry
    {
        private readonly Dictionary<AIProvider, Func<BrainarrSettings, IHttpClient, Logger, IAIProvider>> _factories;
        private readonly Dictionary<AIProvider, Func<string, string>> _modelMappers;

        public ProviderRegistry()
        {
            _factories = new Dictionary<AIProvider, Func<BrainarrSettings, IHttpClient, Logger, IAIProvider>>();
            _modelMappers = new Dictionary<AIProvider, Func<string, string>>();

            // Register all providers
            RegisterProviders();

            // Sanity-check model mappings on startup (warn-only by default)
            try { ModelIdMappingValidator.AssertValid(false, LogManager.GetCurrentClassLogger()); } catch { }
        }

        private void RegisterProviders()
        {
            // Local providers with validation settings
            Register(AIProvider.Ollama, (settings, http, logger) =>
            {
                var validator = new RecommendationValidator(
                    logger,
                    settings.CustomFilterPatterns,
                    settings.EnableStrictValidation);

                return new OllamaProvider(
                    settings.OllamaUrl ?? BrainarrConstants.DefaultOllamaUrl,
                    settings.OllamaModel ?? BrainarrConstants.DefaultOllamaModel,
                    http,
                    logger,
                    validator);
            });

            Register(AIProvider.LMStudio, (settings, http, logger) =>
            {
                var validator = new RecommendationValidator(
                    logger,
                    settings.CustomFilterPatterns,
                    settings.EnableStrictValidation);

                return new LMStudioProvider(
                    settings.LMStudioUrl ?? BrainarrConstants.DefaultLMStudioUrl,
                    settings.LMStudioModel ?? BrainarrConstants.DefaultLMStudioModel,
                    http,
                    logger,
                    validator,
                    allowArtistOnly: settings.RecommendationMode == RecommendationMode.Artists,
                    temperature: settings.LMStudioTemperature,
                    maxTokens: settings.LMStudioMaxTokens);
            });

            // Cloud providers with model mapping
            Register(AIProvider.Perplexity, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapPerplexityModel(settings.PerplexityModelId);
                var preferStructured = settings.PreferStructuredJsonForChat;
                return new PerplexityProvider(http, logger,
                    settings.PerplexityApiKey,
                    model,
                    preferStructured: preferStructured);
            });

            Register(AIProvider.OpenAI, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapOpenAIModel(settings.OpenAIModelId);
                var preferStructured = settings.PreferStructuredJsonForChat;
                return new OpenAIProvider(http, logger,
                    settings.OpenAIApiKey,
                    model,
                    preferStructured: preferStructured);
            });

            Register(AIProvider.Anthropic, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapAnthropicModel(settings.AnthropicModelId);
                // If thinking mode requests thinking, append sentinel so provider can include the proper param
                if (settings.ThinkingMode != NzbDrone.Core.ImportLists.Brainarr.ThinkingMode.Off && !string.IsNullOrWhiteSpace(model) && !model.Contains("#thinking"))
                {
                    model += "#thinking";
                    if (settings.ThinkingBudgetTokens > 0)
                    {
                        model += $"(tokens={settings.ThinkingBudgetTokens})";
                    }
                }
                return new AnthropicProvider(http, logger,
                    settings.AnthropicApiKey,
                    model);
            });

            Register(AIProvider.OpenRouter, (settings, http, logger) =>
            {
                // For OpenRouter, allow direct pass-through IDs (e.g., "anthropic/claude-3.5-sonnet")
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapOpenRouterModel(settings.OpenRouterModelId);
                // Convenience: auto-switch Anthropic routes to :thinking variant if thinking mode is not Off
                if (settings.ThinkingMode != NzbDrone.Core.ImportLists.Brainarr.ThinkingMode.Off &&
                    !string.IsNullOrWhiteSpace(model) &&
                    model.StartsWith("anthropic/claude-", StringComparison.OrdinalIgnoreCase) &&
                    !model.Contains(":thinking", StringComparison.OrdinalIgnoreCase))
                {
                    model += ":thinking";
                }
                var preferStructured = settings.PreferStructuredJsonForChat;
                return new OpenRouterProvider(http, logger,
                    settings.OpenRouterApiKey,
                    model,
                    preferStructured: preferStructured);
            });

            Register(AIProvider.DeepSeek, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapDeepSeekModel(settings.DeepSeekModelId);
                var preferStructured = settings.PreferStructuredJsonForChat;
                return new DeepSeekProvider(http, logger,
                    settings.DeepSeekApiKey,
                    model,
                    preferStructured: preferStructured);
            });

            Register(AIProvider.Gemini, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapGeminiModel(settings.GeminiModelId);
                return new GeminiProvider(http, logger,
                    settings.GeminiApiKey,
                    model);
            });

            Register(AIProvider.Groq, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapGroqModel(settings.GroqModelId);
                var preferStructured = settings.PreferStructuredJsonForChat;
                return new GroqProvider(http, logger,
                    settings.GroqApiKey,
                    model,
                    preferStructured: preferStructured);
            });
        }

        public void Register(AIProvider type, Func<BrainarrSettings, IHttpClient, Logger, IAIProvider> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            _factories[type] = factory;
        }

        public IAIProvider CreateProvider(AIProvider type, BrainarrSettings settings, IHttpClient httpClient, Logger logger)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            if (!_factories.TryGetValue(type, out var factory))
            {
                throw new NotSupportedException($"Provider type {type} is not supported");
            }

            return factory(settings, httpClient, logger);
        }

        public bool IsRegistered(AIProvider type)
        {
            return _factories.ContainsKey(type);
        }

        public IEnumerable<AIProvider> GetRegisteredProviders()
        {
            return _factories.Keys;
        }

        #region Model Mapping Methods

        private string MapPerplexityModel(string modelEnum)
        {
            // Delegate to centralized mapper to avoid drift between code paths
            return NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("perplexity", modelEnum);
        }

        private string MapOpenAIModel(string modelEnum)
        {
            return modelEnum switch
            {
                "GPT41" => "gpt-4.1",
                "GPT41_Mini" => "gpt-4.1-mini",
                "GPT41_Nano" => "gpt-4.1-nano",
                "GPT4o" => "gpt-4o",
                "GPT4o_Mini" => "gpt-4o-mini",
                "O4_Mini" => "o4-mini",
                _ => "gpt-4.1-mini"
            };
        }

        private string MapAnthropicModel(string modelEnum)
        {
            return modelEnum switch
            {
                "ClaudeSonnet4" => "claude-sonnet-4-20250514",
                "Claude37_Sonnet" => "claude-3-7-sonnet-latest",
                "Claude35_Haiku" => "claude-3-5-haiku-latest",
                "Claude3_Opus" => "claude-3-opus-20240229",
                _ => "claude-sonnet-4-20250514"
            };
        }

        private string MapOpenRouterModel(string modelEnum)
        {
            if (string.IsNullOrWhiteSpace(modelEnum)) return "openrouter/auto";

            // Pass-through if already a full model id like "vendor/model"
            if (modelEnum.Contains("/")) return modelEnum;

            return modelEnum switch
            {
                "Auto" => "openrouter/auto",
                "ClaudeSonnet4" => "anthropic/claude-sonnet-4-20250514",
                "GPT41_Mini" => "openai/gpt-4.1-mini",
                "Gemini25_Flash" => "google/gemini-2.5-flash",
                "Llama33_70B" => "meta-llama/llama-3.3-70b-versatile",
                "DeepSeekV3" => "deepseek/deepseek-chat",
                "Claude35_Haiku" => "anthropic/claude-3.5-haiku",
                _ => "openrouter/auto"
            };
        }

        private string MapDeepSeekModel(string modelEnum)
        {
            return modelEnum switch
            {
                "DeepSeek_Chat" => "deepseek-chat",
                "DeepSeek_Reasoner" => "deepseek-reasoner",
                "DeepSeek_R1" => "deepseek-r1",
                "DeepSeek_Search" => "deepseek-search",
                _ => "deepseek-chat"
            };
        }

        private string MapGeminiModel(string modelEnum)
        {
            return modelEnum switch
            {
                "Gemini_25_Pro" => "gemini-2.5-pro-latest",
                "Gemini_25_Flash" => "gemini-2.5-flash",
                "Gemini_25_Flash_Lite" => "gemini-2.5-flash-lite",
                "Gemini_20_Flash" => "gemini-2.0-flash",
                "Gemini_15_Flash" => "gemini-1.5-flash",
                "Gemini_15_Flash_8B" => "gemini-1.5-flash-8b",
                "Gemini_15_Pro" => "gemini-1.5-pro",
                _ => "gemini-2.5-flash"
            };
        }

        private string MapGroqModel(string modelEnum)
        {
            return modelEnum switch
            {
                "Llama33_70B_Versatile" => "llama-3.3-70b-versatile",
                "Llama33_70B_SpecDec" => "llama-3.3-70b-specdec",
                "DeepSeek_R1_Distill_L70B" => "deepseek-r1-distill-llama-70b",
                "Llama31_8B_Instant" => "llama-3.1-8b-instant",
                _ => "llama-3.3-70b-versatile"
            };
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for provider registry.
    /// </summary>
    public static class ProviderRegistryExtensions
    {
        /// <summary>
        /// Registers a provider with automatic parameter extraction.
        /// </summary>
        public static void RegisterAuto<TProvider>(this IProviderRegistry registry, AIProvider type)
            where TProvider : IAIProvider
        {
            // This could use reflection to auto-wire providers
            // For now, manual registration is clearer
        }

        /// <summary>
        /// Tries to create a provider, returning null on failure.
        /// </summary>
        public static IAIProvider TryCreateProvider(this IProviderRegistry registry,
            AIProvider type, BrainarrSettings settings, IHttpClient httpClient, Logger logger)
        {
            try
            {
                return registry.CreateProvider(type, settings, httpClient, logger);
            }
            catch
            {
                return null;
            }
        }
    }
}
