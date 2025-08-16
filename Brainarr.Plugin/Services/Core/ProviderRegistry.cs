using System;
using System.Collections.Generic;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
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
        }

        private void RegisterProviders()
        {
            // Local providers
            Register(AIProvider.Ollama, (settings, http, logger) =>
                new OllamaProvider(
                    settings.OllamaUrl ?? BrainarrConstants.DefaultOllamaUrl,
                    settings.OllamaModel ?? BrainarrConstants.DefaultOllamaModel,
                    http,
                    logger));

            Register(AIProvider.LMStudio, (settings, http, logger) =>
                new LMStudioProvider(
                    settings.LMStudioUrl ?? BrainarrConstants.DefaultLMStudioUrl,
                    settings.LMStudioModel ?? BrainarrConstants.DefaultLMStudioModel,
                    http,
                    logger));

            // Cloud providers with model mapping
            Register(AIProvider.Perplexity, (settings, http, logger) =>
                new PerplexityProvider(http, logger,
                    settings.PerplexityApiKey,
                    MapPerplexityModel(settings.PerplexityModel)));

            Register(AIProvider.OpenAI, (settings, http, logger) =>
                new OpenAIProvider(http, logger,
                    settings.OpenAIApiKey,
                    MapOpenAIModel(settings.OpenAIModel)));

            Register(AIProvider.Anthropic, (settings, http, logger) =>
                new AnthropicProvider(http, logger,
                    settings.AnthropicApiKey,
                    MapAnthropicModel(settings.AnthropicModel)));

            Register(AIProvider.OpenRouter, (settings, http, logger) =>
                new OpenRouterProvider(http, logger,
                    settings.OpenRouterApiKey,
                    MapOpenRouterModel(settings.OpenRouterModel)));

            Register(AIProvider.DeepSeek, (settings, http, logger) =>
                new DeepSeekProvider(http, logger,
                    settings.DeepSeekApiKey,
                    MapDeepSeekModel(settings.DeepSeekModel)));

            Register(AIProvider.Gemini, (settings, http, logger) =>
                new GeminiProvider(http, logger,
                    settings.GeminiApiKey,
                    MapGeminiModel(settings.GeminiModel)));

            Register(AIProvider.Groq, (settings, http, logger) =>
                new GroqProvider(http, logger,
                    settings.GroqApiKey,
                    MapGroqModel(settings.GroqModel)));

            // Claude providers (standard and music-enhanced)
            Register(AIProvider.Claude, (settings, http, logger) =>
                new ClaudeProvider(http, logger,
                    settings.ClaudeApiKey,
                    MapClaudeModel(settings.ClaudeModel, false)));

            Register(AIProvider.ClaudeMusic, (settings, http, logger) =>
                new ClaudeCodeMusicProvider(http, logger,
                    settings.ClaudeApiKey,
                    MapClaudeModel(settings.ClaudeModel, true)));
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
                throw new NotSupportedException($"Provider type {type} is not registered");
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
            return modelEnum switch
            {
                "Sonar_Large" => "llama-3.1-sonar-large-128k-online",
                "Sonar_Small" => "llama-3.1-sonar-small-128k-online",
                "Sonar_Huge" => "llama-3.1-sonar-huge-128k-online",
                _ => "llama-3.1-sonar-large-128k-online"
            };
        }

        private string MapOpenAIModel(string modelEnum)
        {
            return modelEnum switch
            {
                "GPT4o_Mini" => "gpt-4o-mini",
                "GPT4o" => "gpt-4o",
                "GPT4_Turbo" => "gpt-4-turbo",
                "GPT35_Turbo" => "gpt-3.5-turbo",
                _ => "gpt-4o-mini"
            };
        }

        private string MapAnthropicModel(string modelEnum)
        {
            return modelEnum switch
            {
                "Claude35_Haiku" => "claude-3-5-haiku-latest",
                "Claude35_Sonnet" => "claude-3-5-sonnet-latest",
                "Claude3_Opus" => "claude-3-opus-20240229",
                _ => "claude-3-5-haiku-latest"
            };
        }

        private string MapOpenRouterModel(string modelEnum)
        {
            return modelEnum switch
            {
                "Claude35_Haiku" => "anthropic/claude-3.5-haiku",
                "DeepSeekV3" => "deepseek/deepseek-chat",
                "Gemini_Flash" => "google/gemini-flash-1.5",
                "Claude35_Sonnet" => "anthropic/claude-3.5-sonnet",
                "GPT4o_Mini" => "openai/gpt-4o-mini",
                "Llama3_70B" => "meta-llama/llama-3-70b-instruct",
                "GPT4o" => "openai/gpt-4o",
                "Claude3_Opus" => "anthropic/claude-3-opus",
                "Gemini_Pro" => "google/gemini-pro-1.5",
                "Mistral_Large" => "mistral/mistral-large",
                "Qwen_72B" => "qwen/qwen-72b-chat",
                _ => "anthropic/claude-3.5-haiku"
            };
        }

        private string MapDeepSeekModel(string modelEnum)
        {
            return modelEnum switch
            {
                "DeepSeek_Chat" => "deepseek-chat",
                "DeepSeek_Coder" => "deepseek-coder",
                "DeepSeek_Reasoner" => "deepseek-reasoner",
                _ => "deepseek-chat"
            };
        }

        private string MapGeminiModel(string modelEnum)
        {
            return modelEnum switch
            {
                "Gemini_15_Flash" => "gemini-1.5-flash",
                "Gemini_15_Flash_8B" => "gemini-1.5-flash-8b",
                "Gemini_15_Pro" => "gemini-1.5-pro",
                "Gemini_20_Flash" => "gemini-2.0-flash-exp",
                _ => "gemini-1.5-flash"
            };
        }

        private string MapGroqModel(string modelEnum)
        {
            return modelEnum switch
            {
                "Llama33_70B" => "llama-3.3-70b-versatile",
                "Llama32_90B_Vision" => "llama-3.2-90b-vision-preview",
                "Llama31_70B" => "llama-3.1-70b-versatile",
                "Mixtral_8x7B" => "mixtral-8x7b-32768",
                "Gemma2_9B" => "gemma2-9b-it",
                _ => "llama-3.3-70b-versatile"
            };
        }

        private string MapClaudeModel(string modelEnum, bool isMusicProvider)
        {
            // For music provider, we use the same models but with enhanced prompting
            return modelEnum switch
            {
                "Claude35_Haiku" or "Music_Haiku" => "claude-3-5-haiku-latest",
                "Claude35_Sonnet" or "Music_Sonnet" => "claude-3-5-sonnet-latest",
                "Claude3_Opus" or "Music_Opus" => "claude-3-opus-20240229",
                _ => "claude-3-5-sonnet-latest"
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