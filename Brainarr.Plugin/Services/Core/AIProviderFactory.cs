using System;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Factory for creating AI provider instances based on configuration.
    /// Supports optional integration with an external model registry to
    /// adjust provider configuration (model IDs, API keys) at runtime.
    /// </summary>
    public class AIProviderFactory : IProviderFactory
    {
        private readonly IProviderRegistry _registry;
        private readonly ModelRegistryLoader? _registryLoader;
        private readonly string? _registryUrl;
        private readonly SemaphoreSlim _registryRefreshGate = new(1, 1);
        private readonly TimeSpan _registryRefreshInterval = TimeSpan.FromMinutes(10);
        private ModelRegistry? _cachedRegistry;
        private DateTime _lastRegistryRefreshUtc = DateTime.MinValue;

        public static bool UseExternalModelRegistry { get; set; } = string.Equals(
            Environment.GetEnvironmentVariable("BRAINARR_USE_EXTERNAL_MODEL_REGISTRY"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        public AIProviderFactory()
            : this(new ProviderRegistry(), null, null)
        {
        }

        public AIProviderFactory(IProviderRegistry registry)
            : this(registry, null, null)
        {
        }

        public AIProviderFactory(ModelRegistryLoader registryLoader, string? registryUrl)
            : this(new ProviderRegistry(), registryLoader, registryUrl)
        {
        }

        public AIProviderFactory(IProviderRegistry registry, ModelRegistryLoader? registryLoader, string? registryUrl)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _registryLoader = registryLoader;
            _registryUrl = registryUrl;
        }

        public IAIProvider CreateProvider(BrainarrSettings settings, IHttpClient httpClient, Logger logger)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (!UseExternalModelRegistry || _registryLoader == null)
            {
                return _registry.CreateProvider(settings.Provider, settings, httpClient, logger);
            }

            var registry = EnsureRegistryLoaded(default);
            if (registry == null)
            {
                return _registry.CreateProvider(settings.Provider, settings, httpClient, logger);
            }

            var descriptor = registry.FindProvider(settings.Provider.ToString());
            if (descriptor == null)
            {
                return _registry.CreateProvider(settings.Provider, settings, httpClient, logger);
            }

            using var scope = RegistryApplicationScope.Apply(settings.Provider, settings, descriptor);
            if (!scope.RequirementsSatisfied)
            {
                // Fallback to normal behavior if the registry requirements cannot be satisfied.
                return _registry.CreateProvider(settings.Provider, settings, httpClient, logger);
            }

            return _registry.CreateProvider(settings.Provider, settings, httpClient, logger);
        }

        public bool IsProviderAvailable(AIProvider providerType, BrainarrSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (!UseExternalModelRegistry || _registryLoader == null)
            {
                return CheckProviderAvailability(providerType, settings);
            }

            var registry = EnsureRegistryLoaded(default);
            if (registry == null)
            {
                return CheckProviderAvailability(providerType, settings);
            }

            var descriptor = registry.FindProvider(providerType.ToString());
            if (descriptor == null)
            {
                return CheckProviderAvailability(providerType, settings);
            }

            using var scope = RegistryApplicationScope.Apply(providerType, settings, descriptor);
            if (!scope.RequirementsSatisfied)
            {
                return false;
            }

            return CheckProviderAvailability(providerType, settings);
        }

        private ModelRegistry? EnsureRegistryLoaded(CancellationToken cancellationToken)
        {
            if (_registryLoader == null)
            {
                return null;
            }

            var entered = false;
            try
            {
                _registryRefreshGate.Wait(cancellationToken);
                entered = true;

                var now = DateTime.UtcNow;
                if (_cachedRegistry != null && now - _lastRegistryRefreshUtc < _registryRefreshInterval)
                {
                    return _cachedRegistry;
                }

                var result = _registryLoader.LoadAsync(_registryUrl, cancellationToken).GetAwaiter().GetResult();
                if (result.Registry != null)
                {
                    _cachedRegistry = result.Registry;
                    _lastRegistryRefreshUtc = now;
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
                    _registryRefreshGate.Release();
                }
            }
        }

        private static bool CheckProviderAvailability(AIProvider providerType, BrainarrSettings settings)
        {
            return providerType switch
            {
                AIProvider.Ollama => !string.IsNullOrWhiteSpace(settings.OllamaUrlRaw),
                AIProvider.LMStudio => !string.IsNullOrWhiteSpace(settings.LMStudioUrlRaw),
                AIProvider.Perplexity => !string.IsNullOrWhiteSpace(settings.PerplexityApiKey),
                AIProvider.OpenAI => !string.IsNullOrWhiteSpace(settings.OpenAIApiKey),
                AIProvider.Anthropic => !string.IsNullOrWhiteSpace(settings.AnthropicApiKey),
                AIProvider.OpenRouter => !string.IsNullOrWhiteSpace(settings.OpenRouterApiKey),
                AIProvider.DeepSeek => !string.IsNullOrWhiteSpace(settings.DeepSeekApiKey),
                AIProvider.Gemini => !string.IsNullOrWhiteSpace(settings.GeminiApiKey),
                AIProvider.Groq => !string.IsNullOrWhiteSpace(settings.GroqApiKey),
                _ => false
            };
        }

        private sealed class RegistryApplicationScope : IDisposable
        {
            private readonly AIProvider _provider;
            private readonly BrainarrSettings _settings;
            private readonly ModelRegistry.ProviderDescriptor _descriptor;
            private readonly string? _originalManualModel;
            private readonly string? _originalProviderModel;
            private readonly string? _originalApiKey;
            private bool _restoreManualModel;
            private bool _restoreProviderModel;
            private bool _restoreApiKey;

            private RegistryApplicationScope(
                AIProvider provider,
                BrainarrSettings settings,
                ModelRegistry.ProviderDescriptor descriptor)
            {
                _provider = provider;
                _settings = settings;
                _descriptor = descriptor;
                _originalManualModel = settings.ManualModelId;
                _originalProviderModel = GetProviderModelId(provider, settings);
                _originalApiKey = GetProviderApiKey(provider, settings);
            }

            public bool RequirementsSatisfied { get; private set; }

            public static RegistryApplicationScope Apply(
                AIProvider provider,
                BrainarrSettings settings,
                ModelRegistry.ProviderDescriptor descriptor)
            {
                var scope = new RegistryApplicationScope(provider, settings, descriptor);
                scope.ApplyInternal();
                return scope;
            }

            private void ApplyInternal()
            {
                RequirementsSatisfied = ApplyAuthRequirements() && ApplyModelSelection();
            }

            private bool ApplyAuthRequirements()
            {
                if (_descriptor.Auth == null)
                {
                    return true;
                }

                var requirement = _descriptor.Auth;
                switch (requirement.Type?.ToLowerInvariant())
                {
                    case "none":
                        return true;
                    case "bearer":
                        if (string.IsNullOrWhiteSpace(requirement.Env))
                        {
                            return false;
                        }

                        var value = Environment.GetEnvironmentVariable(requirement.Env);
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            return false;
                        }

                        SetProviderApiKey(_provider, _settings, value);
                        _restoreApiKey = true;
                        return true;
                    default:
                        return false;
                }
            }

            private bool ApplyModelSelection()
            {
                var resolution = ResolveModelId(
                    _descriptor,
                    _settings.ManualModelId,
                    GetProviderModelId(_provider, _settings));

                if (!resolution.Success)
                {
                    return false;
                }

                var modelId = resolution.ModelId;
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return false;
                }

                if (_settings.ManualModelId != null)
                {
                    _restoreManualModel = true;
                    _settings.ManualModelId = modelId;
                }
                else
                {
                    SetProviderModelId(_provider, _settings, modelId);
                    _restoreProviderModel = true;
                }

                return true;
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
                    var match = providerDescriptor.Models.FirstOrDefault(model =>
                        string.Equals(model.Id, manualModelId, StringComparison.OrdinalIgnoreCase) ||
                        model.Aliases?.Any(alias => string.Equals(alias, manualModelId, StringComparison.OrdinalIgnoreCase)) == true);

                    return match != null
                        ? RegistryModelResolution.FromModel(match.Id)
                        : RegistryModelResolution.ManualMismatch();
                }

                if (!string.IsNullOrWhiteSpace(providerModelId))
                {
                    var match = providerDescriptor.Models.FirstOrDefault(model =>
                        string.Equals(model.Id, providerModelId, StringComparison.OrdinalIgnoreCase) ||
                        model.Aliases?.Any(alias => string.Equals(alias, providerModelId, StringComparison.OrdinalIgnoreCase)) == true);

                    return match != null
                        ? RegistryModelResolution.FromModel(match.Id)
                        : RegistryModelResolution.ProviderMismatch();
                }

                if (!string.IsNullOrWhiteSpace(providerDescriptor.DefaultModel))
                {
                    var defaultMatch = providerDescriptor.Models.FirstOrDefault(model =>
                        string.Equals(model.Id, providerDescriptor.DefaultModel, StringComparison.OrdinalIgnoreCase) ||
                        model.Aliases?.Any(alias => string.Equals(alias, providerDescriptor.DefaultModel, StringComparison.OrdinalIgnoreCase)) == true);
                    if (defaultMatch != null)
                    {
                        return RegistryModelResolution.FromModel(defaultMatch.Id);
                    }
                }

                var tierDefault = providerDescriptor.Models.FirstOrDefault(model =>
                    string.Equals(model.Metadata?.Tier, "default", StringComparison.OrdinalIgnoreCase));
                if (tierDefault != null)
                {
                    return RegistryModelResolution.FromModel(tierDefault.Id);
                }

                return RegistryModelResolution.FromModel(providerDescriptor.Models.First().Id);
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
        }
    }
}
