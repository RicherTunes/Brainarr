using System;
using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    /// <summary>
    /// Factory for creating provider-specific settings instances.
    /// This eliminates switch statements when creating provider settings.
    /// </summary>
    public class ProviderSettingsFactory
    {
        private readonly Dictionary<AIProvider, Func<IProviderSettings>> _factories;

        public ProviderSettingsFactory()
        {
            _factories = new Dictionary<AIProvider, Func<IProviderSettings>>
            {
                [AIProvider.Ollama] = () => new OllamaProviderSettings(),
                [AIProvider.LMStudio] = () => new LMStudioProviderSettings(),
                [AIProvider.Perplexity] = () => new PerplexityProviderSettings(),
                [AIProvider.OpenAI] = () => new OpenAIProviderSettings(),
                [AIProvider.Anthropic] = () => new AnthropicProviderSettings(),
                [AIProvider.OpenRouter] = () => new OpenRouterProviderSettings(),
                [AIProvider.DeepSeek] = () => new DeepSeekProviderSettings(),
                [AIProvider.Gemini] = () => new GeminiProviderSettings(),
                [AIProvider.Groq] = () => new GroqProviderSettings()
            };
        }

        /// <summary>
        /// Creates a new instance of provider settings for the specified provider.
        /// </summary>
        /// <param name="provider">The provider type</param>
        /// <returns>New provider settings instance</returns>
        public IProviderSettings CreateSettings(AIProvider provider)
        {
            if (_factories.TryGetValue(provider, out var factory))
            {
                return factory();
            }

            throw new ArgumentOutOfRangeException(nameof(provider), $"Unsupported provider: {provider}");
        }

        /// <summary>
        /// Gets all supported provider types.
        /// </summary>
        public IEnumerable<AIProvider> GetSupportedProviders()
        {
            return _factories.Keys;
        }
    }
}
