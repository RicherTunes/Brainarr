using System;
using System.Linq;
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

            var providerDescriptor = registry.FindProvider(settings.Provider.ToString());
            if (providerDescriptor == null)
            {
                return _inner.CreateProvider(settings, httpClient, logger);
            }

            using var scope = RegistryApplicationScope.Apply(settings.Provider, settings, providerDescriptor);
            if (!scope.RequirementsSatisfied)
            {
                return _inner.CreateProvider(settings, httpClient, logger);
            }

            return _inner.CreateProvider(settings, httpClient, logger);
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

            var providerDescriptor = registry.FindProvider(providerType.ToString());
            if (providerDescriptor == null)
            {
                return _inner.IsProviderAvailable(providerType, settings);
            }

            using var scope = RegistryApplicationScope.Apply(providerType, settings, providerDescriptor);
            if (!scope.RequirementsSatisfied)
            {
                return false;
            }

            return _inner.IsProviderAvailable(providerType, settings);
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

        private static RegistryModelResolution ResolveModelId(
            ModelRegistry.ProviderDescriptor providerDescriptor,
            string? manualModelId,
            string? providerModelId)
        {
            if (providerDescriptor.Models == null || providerDescriptor.Models.Count == 0)
            {
                return RegistryModelResolution.MissingModels();
            }

            if (!string.IsNullOrWhiteSpace(manualModelId))
            {
                var match = providerDescriptor.Models.FirstOrDefault(m => string.Equals(m.Id, manualModelId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return RegistryModelResolution.FromModel(match.Id);
                }

                return RegistryModelResolution.ManualMismatch();
            }

            if (!string.IsNullOrWhiteSpace(providerModelId))
            {
                var match = providerDescriptor.Models.FirstOrDefault(m => string.Equals(m.Id, providerModelId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return RegistryModelResolution.FromModel(match.Id);
                }

                return RegistryModelResolution.ProviderMismatch();
            }

            var fallback = providerDescriptor.Models.First();
            return RegistryModelResolution.FromModel(fallback.Id);
        }

        private sealed class RegistryApplicationScope : IDisposable
        {
            private readonly AIProvider _provider;
            private readonly BrainarrSettings _settings;
            private readonly string? _originalManualModel;
            private readonly string? _originalProviderModel;
            private readonly string? _originalApiKey;
            private readonly bool _restoreManualModel;
            private readonly bool _restoreProviderModel;
            private readonly bool _restoreApiKey;

            private RegistryApplicationScope(
                AIProvider provider,
                BrainarrSettings settings,
                string? originalManualModel,
                string? originalProviderModel,
                string? originalApiKey,
                bool restoreManualModel,
                bool restoreProviderModel,
                bool restoreApiKey,
                bool requirementsSatisfied)
            {
                _provider = provider;
                _settings = settings;
                _originalManualModel = originalManualModel;
                _originalProviderModel = originalProviderModel;
                _originalApiKey = originalApiKey;
                _restoreManualModel = restoreManualModel;
                _restoreProviderModel = restoreProviderModel;
                _restoreApiKey = restoreApiKey;
                RequirementsSatisfied = requirementsSatisfied;
            }

            public bool RequirementsSatisfied { get; }

            public static RegistryApplicationScope Apply(
                AIProvider provider,
                BrainarrSettings settings,
                ModelRegistry.ProviderDescriptor providerDescriptor)
            {
                var originalManualModel = settings.ManualModelId;
                var originalProviderModel = GetProviderModelId(provider, settings);
                var providerModelIsEffectivelyEmpty = IsProviderModelEffectivelyEmpty(provider, originalProviderModel);
                var providerModelForResolution = providerModelIsEffectivelyEmpty ? null : originalProviderModel;
                var originalApiKey = GetProviderApiKey(provider, settings);

                var restoreManual = false;
                var restoreProvider = false;
                var restoreApiKey = false;
                var requirementsSatisfied = true;

                if (!string.IsNullOrWhiteSpace(providerDescriptor.Auth?.Env))
                {
                    var envValue = Environment.GetEnvironmentVariable(providerDescriptor.Auth.Env);
                    if (string.IsNullOrWhiteSpace(envValue))
                    {
                        requirementsSatisfied = false;
                    }
                    else if (string.IsNullOrWhiteSpace(originalApiKey))
                    {
                        SetProviderApiKey(provider, settings, envValue);
                        restoreApiKey = true;
                    }
                }

                if (requirementsSatisfied)
                {
                    var resolution = ResolveModelId(providerDescriptor, originalManualModel, providerModelForResolution);
                    if (!resolution.Success)
                    {
                        requirementsSatisfied = false;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(originalManualModel))
                        {
                            settings.ManualModelId = resolution.ModelId;
                            restoreManual = true;
                        }

                        if (providerModelIsEffectivelyEmpty)
                        {
                            SetProviderModelId(provider, settings, resolution.ModelId);
                            restoreProvider = true;
                        }
                    }
                }

                return new RegistryApplicationScope(
                    provider,
                    settings,
                    originalManualModel,
                    originalProviderModel,
                    originalApiKey,
                    restoreManual,
                    restoreProvider,
                    restoreApiKey,
                    requirementsSatisfied);
            }

            public void Dispose()
            {
                if (_restoreManualModel)
                {
                    _settings.ManualModelId = _originalManualModel;
                }

                if (_restoreProviderModel)
                {
                    SetProviderModelId(_provider, _settings, _originalProviderModel);
                }

                if (_restoreApiKey)
                {
                    SetProviderApiKey(_provider, _settings, _originalApiKey);
                }
            }
        }

        private readonly struct RegistryModelResolution
        {
            private RegistryModelResolution(string? modelId, bool success)
            {
                ModelId = modelId;
                Success = success;
            }

            public string? ModelId { get; }

            public bool Success { get; }

            public static RegistryModelResolution FromModel(string modelId) => new(modelId, true);

            public static RegistryModelResolution ManualMismatch() => new(null, false);

            public static RegistryModelResolution ProviderMismatch() => new(null, false);

            public static RegistryModelResolution MissingModels() => new(null, false);
        }

        private static bool IsProviderModelEffectivelyEmpty(AIProvider provider, string? modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return true;
            }

            return provider switch
            {
                AIProvider.Ollama => string.Equals(modelId, BrainarrConstants.DefaultOllamaModel, StringComparison.OrdinalIgnoreCase),
                AIProvider.LMStudio => string.Equals(modelId, BrainarrConstants.DefaultLMStudioModel, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
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
                AIProvider.Ollama => settings.OllamaModel,
                AIProvider.LMStudio => settings.LMStudioModel,
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
                case AIProvider.Ollama:
                    settings.OllamaModel = value ?? string.Empty;
                    break;
                case AIProvider.LMStudio:
                    settings.LMStudioModel = value ?? string.Empty;
                    break;
            }
        }
    }
}
