using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Resolves model context window sizes from provider/model specifications
    /// and the model registry cache. Extracted from LibraryAwarePromptBuilder (M6-2).
    /// </summary>
    internal class ModelContextResolver
    {
        private readonly Logger _logger;
        private readonly ModelRegistryLoader _modelRegistryLoader;
        private readonly Lazy<Dictionary<string, ModelContextInfo>> _modelContextCache;

        public ModelContextResolver(Logger logger, ModelRegistryLoader modelRegistryLoader, string? registryUrl)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modelRegistryLoader = modelRegistryLoader ?? throw new ArgumentNullException(nameof(modelRegistryLoader));
            var url = string.IsNullOrWhiteSpace(registryUrl) ? null : registryUrl.Trim();
            _modelContextCache = new Lazy<Dictionary<string, ModelContextInfo>>(() => LoadModelContextCache(url), isThreadSafe: true);
        }

        public ModelContextInfo Resolve(BrainarrSettings settings)
        {
            var providerSlug = ProviderSlugs.ToRegistrySlug(settings.Provider);
            if (providerSlug == null)
            {
                return new ModelContextInfo();
            }

            var rawModelId = ResolveRawModelId(settings.Provider, settings);
            if (string.IsNullOrWhiteSpace(rawModelId))
            {
                return new ModelContextInfo { ModelKey = providerSlug + ":default", RawModelId = rawModelId ?? string.Empty };
            }

            var key = BuildModelCacheKey(providerSlug, rawModelId);
            if (_modelContextCache.Value.TryGetValue(key, out var info))
            {
                return info with { RawModelId = rawModelId };
            }

            return new ModelContextInfo
            {
                ContextTokens = 0,
                ModelKey = key,
                RawModelId = rawModelId
            };
        }

        private string ResolveRawModelId(AIProvider provider, BrainarrSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.ManualModelId))
            {
                return settings.ManualModelId.Trim();
            }

            var friendly = settings.ModelSelection;
            var normalized = ProviderModelNormalizer.Normalize(provider, friendly);
            var providerSlug = ProviderSlugs.ToRegistrySlug(provider);

            if (string.IsNullOrEmpty(providerSlug))
            {
                return normalized;
            }

            try
            {
                return ModelIdMapper.ToRawId(providerSlug, normalized);
            }
            catch
            {
                return normalized;
            }
        }

        internal static string BuildModelCacheKey(string providerSlug, string rawModelId)
        {
            return ($"{providerSlug}:{rawModelId}").ToLowerInvariant();
        }

        private Dictionary<string, ModelContextInfo> LoadModelContextCache(string? registryUrl)
        {
            var map = new Dictionary<string, ModelContextInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var result = SafeAsyncHelper.RunSafeSync(() => _modelRegistryLoader.LoadAsync(registryUrl, default));
                var registry = result.Registry;
                if (registry == null)
                {
                    return map;
                }

                foreach (var provider in registry.Providers)
                {
                    var slug = ExtractProviderSlug(provider);
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        continue;
                    }

                    foreach (var model in provider.Models)
                    {
                        if (string.IsNullOrWhiteSpace(model.Id) || model.ContextTokens <= 0)
                        {
                            continue;
                        }

                        var key = BuildModelCacheKey(slug, model.Id);
                        map[key] = new ModelContextInfo
                        {
                            ContextTokens = model.ContextTokens,
                            ModelKey = key,
                            RawModelId = model.Id
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to load model registry context tokens; using defaults.");
            }

            return map;
        }

        private static string? ExtractProviderSlug(ModelRegistry.ProviderDescriptor provider)
        {
            if (provider == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(provider.Slug))
            {
                return provider.Slug.Trim();
            }

            if (!string.IsNullOrWhiteSpace(provider.Name))
            {
                return provider.Name.Trim();
            }

            return null;
        }
    }

    internal sealed record ModelContextInfo
    {
        public int ContextTokens { get; init; }
        public string ModelKey { get; init; } = string.Empty;
        public string RawModelId { get; init; } = string.Empty;
    }
}
