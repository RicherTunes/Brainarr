using System;
using System.Threading;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Registry
{
    public sealed class RegistryAwareProviderFactoryDecorator : IProviderFactory
    {
        private readonly IProviderFactory _inner;
        private readonly ModelRegistryLoader _loader;
        private readonly string? _registryUrl;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(10);
        private ModelRegistry? _cachedRegistry;
        private DateTime _lastRefreshUtc = DateTime.MinValue;

        public static bool UseExternalModelRegistry { get; set; } = string.Equals(
            Environment.GetEnvironmentVariable("BRAINARR_USE_EXTERNAL_MODEL_REGISTRY"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        public RegistryAwareProviderFactoryDecorator(IProviderFactory inner, ModelRegistryLoader loader, string? registryUrl)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _registryUrl = registryUrl;
        }

        public IAIProvider CreateProvider(BrainarrSettings settings, IHttpClient httpClient, Logger logger)
        {
            if (!UseExternalModelRegistry)
            {
                return _inner.CreateProvider(settings, httpClient, logger);
            }

            var registry = EnsureRegistryLoaded(default);
            if (registry == null)
            {
                return _inner.CreateProvider(settings, httpClient, logger);
            }

            var slug = settings.Provider.ToString().ToLowerInvariant();
            var providerDescriptor = registry.FindProviderBySlug(slug);
            if (providerDescriptor == null)
            {
                return _inner.CreateProvider(settings, httpClient, logger);
            }

            var originalManualModel = settings.ManualModelId;
            var originalProviderModel = GetProviderModelId(settings.Provider, settings);
            var originalApiKey = GetProviderApiKey(settings.Provider, settings);
            var temporaryApiKey = originalApiKey;
            var appliedModelId = originalManualModel;

            if (string.IsNullOrWhiteSpace(originalApiKey) &&
                !string.IsNullOrWhiteSpace(providerDescriptor.Auth?.EnvironmentVariable))
            {
                temporaryApiKey = Environment.GetEnvironmentVariable(providerDescriptor.Auth.EnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(temporaryApiKey))
                {
                    SetProviderApiKey(settings.Provider, settings, temporaryApiKey);
                }
            }

            if (string.IsNullOrWhiteSpace(originalManualModel))
            {
                var selectedModel = providerDescriptor.Models.Count > 0 ? providerDescriptor.Models[0] : null;
                if (selectedModel != null)
                {
                    settings.ManualModelId = selectedModel.Id;
                    appliedModelId = selectedModel.Id;
                    if (string.IsNullOrWhiteSpace(originalProviderModel))
                    {
                        SetProviderModelId(settings.Provider, settings, selectedModel.Id);
                    }
                }
            }

            try
            {
                return _inner.CreateProvider(settings, httpClient, logger);
            }
            finally
            {
                settings.ManualModelId = originalManualModel;
                if (!string.Equals(originalProviderModel, appliedModelId, StringComparison.Ordinal))
                {
                    SetProviderModelId(settings.Provider, settings, originalProviderModel);
                }
                if (!string.Equals(originalApiKey, temporaryApiKey, StringComparison.Ordinal))
                {
                    SetProviderApiKey(settings.Provider, settings, originalApiKey);
                }
            }
        }

        public bool IsProviderAvailable(AIProvider providerType, BrainarrSettings settings)
        {
            if (!UseExternalModelRegistry)
            {
                return _inner.IsProviderAvailable(providerType, settings);
            }

            var registry = EnsureRegistryLoaded(default);
            if (registry == null)
            {
                return _inner.IsProviderAvailable(providerType, settings);
            }

            var providerDescriptor = registry.FindProviderBySlug(providerType.ToString().ToLowerInvariant());
            if (providerDescriptor == null)
            {
                return _inner.IsProviderAvailable(providerType, settings);
            }

            var originalManualModel = settings.ManualModelId;
            var originalProviderModel = GetProviderModelId(providerType, settings);
            var originalApiKey = GetProviderApiKey(providerType, settings);
            var temporaryApiKey = originalApiKey;
            var appliedModelId = originalManualModel;

            if (string.IsNullOrWhiteSpace(originalApiKey) &&
                !string.IsNullOrWhiteSpace(providerDescriptor.Auth?.EnvironmentVariable))
            {
                temporaryApiKey = Environment.GetEnvironmentVariable(providerDescriptor.Auth.EnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(temporaryApiKey))
                {
                    SetProviderApiKey(providerType, settings, temporaryApiKey);
                }
            }

            if (string.IsNullOrWhiteSpace(originalManualModel))
            {
                var selectedModel = providerDescriptor.Models.Count > 0 ? providerDescriptor.Models[0] : null;
                if (selectedModel != null)
                {
                    settings.ManualModelId = selectedModel.Id;
                    appliedModelId = selectedModel.Id;
                    if (string.IsNullOrWhiteSpace(originalProviderModel))
                    {
                        SetProviderModelId(providerType, settings, selectedModel.Id);
                    }
                }
            }

            try
            {
                return _inner.IsProviderAvailable(providerType, settings);
            }
            finally
            {
                settings.ManualModelId = originalManualModel;
                if (!string.Equals(originalProviderModel, appliedModelId, StringComparison.Ordinal))
                {
                    SetProviderModelId(providerType, settings, originalProviderModel);
                }
                if (!string.Equals(originalApiKey, temporaryApiKey, StringComparison.Ordinal))
                {
                    SetProviderApiKey(providerType, settings, originalApiKey);
                }
            }
        }

        private ModelRegistry? EnsureRegistryLoaded(CancellationToken cancellationToken)
        {
            var entered = false;
            try
            {
                _refreshGate.Wait(cancellationToken);
                entered = true;

                var now = DateTime.UtcNow;
                if (_cachedRegistry != null && now - _lastRefreshUtc < _refreshInterval)
                {
                    return _cachedRegistry;
                }

                var result = _loader.LoadAsync(_registryUrl, cancellationToken).GetAwaiter().GetResult();
                if (result.Registry != null)
                {
                    _cachedRegistry = result.Registry;
                    _lastRefreshUtc = now;
                }

                return _cachedRegistry;
            }
            catch
            {
                return _cachedRegistry;
            }
            finally
            {
                if (entered)
                {
                    _refreshGate.Release();
                }
            }
        }

        private static string? GetProviderApiKey(AIProvider provider, BrainarrSettings settings)
        {
            return provider switch
            {
                AIProvider.Perplexity => settings.PerplexityApiKey,
                AIProvider.OpenAI => settings.OpenAIApiKey,
                AIProvider.Anthropic => settings.AnthropicApiKey,
                AIProvider.OpenRouter => settings.OpenRouterApiKey,
                AIProvider.DeepSeek => settings.DeepSeekApiKey,
                AIProvider.Gemini => settings.GeminiApiKey,
                AIProvider.Groq => settings.GroqApiKey,
                _ => null
            };
        }

        private static void SetProviderApiKey(AIProvider provider, BrainarrSettings settings, string? value)
        {
            switch (provider)
            {
                case AIProvider.Perplexity:
                    settings.PerplexityApiKey = value;
                    break;
                case AIProvider.OpenAI:
                    settings.OpenAIApiKey = value;
                    break;
                case AIProvider.Anthropic:
                    settings.AnthropicApiKey = value;
                    break;
                case AIProvider.OpenRouter:
                    settings.OpenRouterApiKey = value;
                    break;
                case AIProvider.DeepSeek:
                    settings.DeepSeekApiKey = value;
                    break;
                case AIProvider.Gemini:
                    settings.GeminiApiKey = value;
                    break;
                case AIProvider.Groq:
                    settings.GroqApiKey = value;
                    break;
            }
        }

        private static string? GetProviderModelId(AIProvider provider, BrainarrSettings settings)
        {
            return provider switch
            {
                AIProvider.Perplexity => settings.PerplexityModelId,
                AIProvider.OpenAI => settings.OpenAIModelId,
                AIProvider.Anthropic => settings.AnthropicModelId,
                AIProvider.OpenRouter => settings.OpenRouterModelId,
                AIProvider.DeepSeek => settings.DeepSeekModelId,
                AIProvider.Gemini => settings.GeminiModelId,
                AIProvider.Groq => settings.GroqModelId,
                _ => null
            };
        }

        private static void SetProviderModelId(AIProvider provider, BrainarrSettings settings, string? value)
        {
            switch (provider)
            {
                case AIProvider.Perplexity:
                    settings.PerplexityModelId = value;
                    break;
                case AIProvider.OpenAI:
                    settings.OpenAIModelId = value;
                    break;
                case AIProvider.Anthropic:
                    settings.AnthropicModelId = value;
                    break;
                case AIProvider.OpenRouter:
                    settings.OpenRouterModelId = value;
                    break;
                case AIProvider.DeepSeek:
                    settings.DeepSeekModelId = value;
                    break;
                case AIProvider.Gemini:
                    settings.GeminiModelId = value;
                    break;
                case AIProvider.Groq:
                    settings.GroqModelId = value;
                    break;
            }
        }
    }
}
