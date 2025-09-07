using System;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Common.Http;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Factory for creating AI provider instances based on configuration.
    /// Uses provider registry pattern for extensibility.
    /// </summary>
    public class AIProviderFactory : IProviderFactory
    {
        private readonly IProviderRegistry _registry;

        public AIProviderFactory()
        {
            _registry = new ProviderRegistry();
        }

        public AIProviderFactory(IProviderRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Creates an AI provider instance based on the specified settings.
        /// </summary>
        public IAIProvider CreateProvider(BrainarrSettings settings, IHttpClient httpClient, Logger logger)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            // Use registry pattern instead of switch statement
            return _registry.CreateProvider(settings.Provider, settings, httpClient, logger);
        }

        /// <summary>
        /// Validates if a provider is available and properly configured.
        /// </summary>
        public bool IsProviderAvailable(AIProvider providerType, BrainarrSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            switch (providerType)
            {
                case AIProvider.Ollama:
                    // Use OllamaUrlRaw to check actual value without default
                    return !string.IsNullOrWhiteSpace(settings.OllamaUrlRaw);

                case AIProvider.LMStudio:
                    // Use LMStudioUrlRaw to check actual value without default
                    return !string.IsNullOrWhiteSpace(settings.LMStudioUrlRaw);

                case AIProvider.Perplexity:
                    return !string.IsNullOrWhiteSpace(settings.PerplexityApiKey);

                case AIProvider.OpenAI:
                    return !string.IsNullOrWhiteSpace(settings.OpenAIApiKey);

                case AIProvider.Anthropic:
                    return !string.IsNullOrWhiteSpace(settings.AnthropicApiKey);

                case AIProvider.OpenRouter:
                    return !string.IsNullOrWhiteSpace(settings.OpenRouterApiKey);

                case AIProvider.DeepSeek:
                    return !string.IsNullOrWhiteSpace(settings.DeepSeekApiKey);

                case AIProvider.Gemini:
                    return !string.IsNullOrWhiteSpace(settings.GeminiApiKey);

                case AIProvider.Groq:
                    return !string.IsNullOrWhiteSpace(settings.GroqApiKey);

                default:
                    return false;
            }
        }
    }
}
