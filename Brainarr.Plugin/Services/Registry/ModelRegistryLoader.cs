using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Registry
{
    public enum ModelRegistryLoadSource
    {
        None,
        Network,
        Cache,
        CacheNotModified,
        CacheFallback,
        Embedded
    }

    public sealed class ModelRegistryLoadResult
    {
        public ModelRegistryLoadResult(ModelRegistry? registry, ModelRegistryLoadSource source, string? etag)
        {
            Registry = registry;
            Source = source;
            ETag = etag;
        }

        public ModelRegistry? Registry { get; }

        public ModelRegistryLoadSource Source { get; }

        public string? ETag { get; }
    }

    public sealed class ModelRegistryLoaderOptions
    {
        public bool EnableSharedCache { get; init; } = true;

        public TimeSpan SharedCacheTtl { get; init; } = TimeSpan.FromMinutes(10);
    }

    public sealed class ModelRegistryLoader
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly JsonSerializerOptions DocumentSerializerOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly HttpClient _httpClient;
        private readonly string _cacheFilePath;
        private readonly string _etagFilePath;
        private readonly string _embeddedRegistryPath;
        private readonly ModelRegistryLoaderOptions _options;
        private readonly Func<string?, CancellationToken, Task<ModelRegistryLoadResult>>? _customLoader;

        private sealed class SharedCacheEntry
        {
            public SharedCacheEntry()
            {
                Lock = new SemaphoreSlim(1, 1);
            }

            public SemaphoreSlim Lock { get; }

            public ModelRegistryLoadResult? Value { get; set; }

            public DateTime TimestampUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, SharedCacheEntry> SharedCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _fileLock = new();

        public ModelRegistryLoader(
            HttpClient? httpClient = null,
            string? cacheFilePath = null,
            string? embeddedRegistryPath = null,
            ModelRegistryLoaderOptions? options = null,
            Func<string?, CancellationToken, Task<ModelRegistryLoadResult>>? customLoader = null)
        {
            _httpClient = httpClient ?? NzbDrone.Core.ImportLists.Brainarr.Services.Http.SecureHttpClientFactory.Create("registry");
            _cacheFilePath = cacheFilePath ?? GetDefaultCachePath();
            _etagFilePath = _cacheFilePath + ".etag";
            _embeddedRegistryPath = embeddedRegistryPath ?? ResolveRelativeToBaseDirectory(Path.Combine("docs", "models.example.json"));
            _options = options ?? BuildDefaultOptions();
            _customLoader = customLoader;
        }
        public Task<ModelRegistryLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return LoadAsync(null, cancellationToken);
        }

        public async Task<ModelRegistryLoadResult> LoadAsync(string? registryUrl, CancellationToken cancellationToken = default)
        {
            if (_options.EnableSharedCache)
            {
                return await LoadWithSharedCacheAsync(registryUrl, cancellationToken).ConfigureAwait(false);
            }

            return await InvokeLoaderAsync(registryUrl, cancellationToken).ConfigureAwait(false);
        }

        private async Task<ModelRegistryLoadResult> LoadWithSharedCacheAsync(string? registryUrl, CancellationToken cancellationToken)
        {
            var key = BuildCacheKey(registryUrl);
            var entry = SharedCache.GetOrAdd(key, _ => new SharedCacheEntry());

            await entry.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (entry.Value != null && !IsExpired(entry.TimestampUtc))
                {
                    return entry.Value;
                }

                var result = await InvokeLoaderAsync(registryUrl, cancellationToken).ConfigureAwait(false);
                entry.Value = result;
                entry.TimestampUtc = DateTime.UtcNow;
                return result;
            }
            finally
            {
                entry.Lock.Release();
            }
        }

        private Task<ModelRegistryLoadResult> InvokeLoaderAsync(string? registryUrl, CancellationToken cancellationToken)
        {
            if (_customLoader != null)
            {
                return _customLoader(registryUrl, cancellationToken);
            }

            return LoadInternalAsync(registryUrl, cancellationToken);
        }

        private async Task<ModelRegistryLoadResult> LoadInternalAsync(string? registryUrl, CancellationToken cancellationToken)
        {
            var cacheDirectory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }

            if (string.IsNullOrWhiteSpace(registryUrl))
            {
                var registryFromCache = await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
                if (registryFromCache.Registry != null)
                {
                    return registryFromCache;
                }

                var registryFromEmbedded = await TryLoadFromEmbeddedAsync(cancellationToken).ConfigureAwait(false);
                if (registryFromEmbedded.Registry != null)
                {
                    return registryFromEmbedded;
                }

                return new ModelRegistryLoadResult(null, ModelRegistryLoadSource.None, null);
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, registryUrl);

                var cachedEtag = ReadEtag();
                if (!string.IsNullOrWhiteSpace(cachedEtag))
                {
                    if (EntityTagHeaderValue.TryParse(cachedEtag, out var parsed))
                    {
                        request.Headers.IfNoneMatch.Add(parsed);
                    }
                    else
                    {
                        request.Headers.TryAddWithoutValidation("If-None-Match", cachedEtag);
                    }
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    var cached = await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
                    if (cached.Registry != null)
                    {
                        return new ModelRegistryLoadResult(cached.Registry, ModelRegistryLoadSource.CacheNotModified, cached.ETag);
                    }

                    return new ModelRegistryLoadResult(null, ModelRegistryLoadSource.CacheNotModified, cached.ETag);
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var registry = Deserialize(json);

                if (registry == null)
                {
                    var cacheFallback = await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
                    if (cacheFallback.Registry != null)
                    {
                        return new ModelRegistryLoadResult(cacheFallback.Registry, ModelRegistryLoadSource.CacheFallback, cacheFallback.ETag);
                    }

                    var embeddedFallback = await TryLoadFromEmbeddedAsync(cancellationToken).ConfigureAwait(false);
                    if (embeddedFallback.Registry != null)
                    {
                        return embeddedFallback;
                    }

                    return new ModelRegistryLoadResult(null, ModelRegistryLoadSource.CacheFallback, cacheFallback.ETag);
                }

                await PersistCacheAsync(json, cancellationToken).ConfigureAwait(false);
                WriteEtag(response.Headers.ETag?.Tag);

                return new ModelRegistryLoadResult(registry, ModelRegistryLoadSource.Network, response.Headers.ETag?.Tag);
            }
            catch (Exception) when (!(cancellationToken.IsCancellationRequested))
            {
                var fallback = await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
                if (fallback.Registry != null)
                {
                    return new ModelRegistryLoadResult(fallback.Registry, ModelRegistryLoadSource.CacheFallback, fallback.ETag);
                }

                var embeddedFallback = await TryLoadFromEmbeddedAsync(cancellationToken).ConfigureAwait(false);
                if (embeddedFallback.Registry != null)
                {
                    return embeddedFallback;
                }

                return new ModelRegistryLoadResult(null, ModelRegistryLoadSource.CacheFallback, fallback.ETag);
            }
        }

        private async Task<ModelRegistryLoadResult> TryLoadFromCacheAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_cacheFilePath))
            {
                return new ModelRegistryLoadResult(null, ModelRegistryLoadSource.Cache, ReadEtag());
            }

            try
            {
                var json = await File.ReadAllTextAsync(_cacheFilePath, cancellationToken).ConfigureAwait(false);
                var registry = Deserialize(json);
                return new ModelRegistryLoadResult(registry, ModelRegistryLoadSource.Cache, ReadEtag());
            }
            catch
            {
                return new ModelRegistryLoadResult(null, ModelRegistryLoadSource.Cache, ReadEtag());
            }
        }

        private async Task<ModelRegistryLoadResult> TryLoadFromEmbeddedAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_embeddedRegistryPath))
            {
                return new ModelRegistryLoadResult(null, ModelRegistryLoadSource.Embedded, null);
            }

            var json = await File.ReadAllTextAsync(_embeddedRegistryPath, cancellationToken).ConfigureAwait(false);
            var registry = Deserialize(json);
            return new ModelRegistryLoadResult(registry, ModelRegistryLoadSource.Embedded, null);
        }

        private static ModelRegistry? Deserialize(string json)
        {
            try
            {
                var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json, DocumentSerializerOptions);
                if (document != null && document.Providers.Count > 0)
                {
                    var converted = Convert(document);
                    if (Validate(converted))
                    {
                        return converted;
                    }
                }
            }
            catch (JsonException)
            {
                // fall back to legacy shape below
            }

            try
            {
                var registry = JsonSerializer.Deserialize<ModelRegistry>(json, SerializerOptions);
                return Validate(registry) ? registry : null;
            }
            catch
            {
                return null;
            }
        }

        private static ModelRegistry Convert(ModelRegistryDocument document)
        {
            var registry = new ModelRegistry
            {
                Version = string.IsNullOrWhiteSpace(document.Version) ? "1" : document.Version!,
                Providers = new List<ModelRegistry.ProviderDescriptor>()
            };

            foreach (var kvp in document.Providers)
            {
                var providerDoc = kvp.Value ?? new ModelRegistryProvider();
                var slug = string.IsNullOrWhiteSpace(providerDoc.Slug) ? kvp.Key : providerDoc.Slug!;
                var descriptor = new ModelRegistry.ProviderDescriptor
                {
                    Name = string.IsNullOrWhiteSpace(providerDoc.Name) ? providerDoc.DisplayName ?? slug : providerDoc.Name!,
                    Slug = slug,
                    Endpoint = Prefer(providerDoc.Endpoint, providerDoc.AdditionalProperties, "endpoint"),
                    DefaultModel = Prefer(providerDoc.DefaultModel, providerDoc.AdditionalProperties, "defaultModel"),
                    Auth = ConvertAuth(providerDoc.Auth, providerDoc.AdditionalProperties),
                    Timeouts = ConvertTimeouts(providerDoc.Timeouts, providerDoc.AdditionalProperties),
                    Retries = ConvertRetries(providerDoc.Retries, providerDoc.AdditionalProperties),
                    Integrity = ConvertIntegrity(providerDoc.Integrity, providerDoc.AdditionalProperties),
                    Models = ConvertModels(providerDoc.Models)
                };

                registry.Providers.Add(descriptor);
            }

            return registry;
        }

        private static List<ModelRegistry.ModelDescriptor> ConvertModels(List<ModelRegistryEntry> entries)
        {
            var list = new List<ModelRegistry.ModelDescriptor>();
            if (entries == null)
            {
                return list;
            }

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                {
                    continue;
                }

                var descriptor = new ModelRegistry.ModelDescriptor
                {
                    Id = entry.Id,
                    Label = entry.Label,
                    Aliases = entry.Aliases ?? new List<string>(),
                    ContextTokens = entry.ContextTokens ?? 0,
                    Pricing = ConvertPricing(entry.Pricing),
                    Capabilities = ConvertCapabilities(entry.Capabilities),
                    Metadata = ConvertMetadata(entry.Metadata)
                };

                list.Add(descriptor);
            }

            return list;
        }

        private static ModelRegistry.PricingDescriptor? ConvertPricing(ModelRegistryPricing? pricing)
        {
            if (pricing == null)
            {
                return null;
            }

            return new ModelRegistry.PricingDescriptor
            {
                InputPer1k = pricing.InputPer1k,
                OutputPer1k = pricing.OutputPer1k
            };
        }

        private static ModelRegistry.CapabilitiesDescriptor ConvertCapabilities(ModelRegistryCapabilities? capabilities)
        {
            return new ModelRegistry.CapabilitiesDescriptor
            {
                Stream = capabilities?.Stream ?? false,
                JsonMode = capabilities?.JsonMode ?? false,
                Tools = capabilities?.Tools ?? false,
                ToolChoice = capabilities?.ToolChoice
            };
        }

        private static ModelRegistry.MetadataDescriptor? ConvertMetadata(Dictionary<string, string>? metadata)
        {
            if (metadata == null || metadata.Count == 0)
            {
                return null;
            }

            var descriptor = new ModelRegistry.MetadataDescriptor
            {
                Tier = metadata.TryGetValue("tier", out var tier) ? tier : null,
                Quality = metadata.TryGetValue("quality", out var quality) ? quality : null
            };

            return descriptor;
        }

        private static ModelRegistry.AuthDescriptor ConvertAuth(ModelRegistryAuth? auth, Dictionary<string, JsonElement> additionalProperties)
        {
            if (auth == null && additionalProperties.TryGetValue("auth", out var element) && element.ValueKind == JsonValueKind.Object)
            {
                auth = JsonSerializer.Deserialize<ModelRegistryAuth>(element.GetRawText(), DocumentSerializerOptions);
            }

            if (auth == null)
            {
                return new ModelRegistry.AuthDescriptor();
            }

            return new ModelRegistry.AuthDescriptor
            {
                Type = auth.Type ?? string.Empty,
                Env = auth.Env ?? string.Empty
            };
        }

        private static ModelRegistry.TimeoutsDescriptor ConvertTimeouts(ModelRegistryTimeouts? timeouts, Dictionary<string, JsonElement> additionalProperties)
        {
            if (timeouts == null && additionalProperties.TryGetValue("timeouts", out var element) && element.ValueKind == JsonValueKind.Object)
            {
                timeouts = JsonSerializer.Deserialize<ModelRegistryTimeouts>(element.GetRawText(), DocumentSerializerOptions);
            }

            return new ModelRegistry.TimeoutsDescriptor
            {
                ConnectMs = timeouts?.ConnectMs ?? 5000,
                RequestMs = timeouts?.RequestMs ?? 30000
            };
        }

        private static ModelRegistry.RetriesDescriptor ConvertRetries(ModelRegistryRetries? retries, Dictionary<string, JsonElement> additionalProperties)
        {
            if (retries == null && additionalProperties.TryGetValue("retries", out var element) && element.ValueKind == JsonValueKind.Object)
            {
                retries = JsonSerializer.Deserialize<ModelRegistryRetries>(element.GetRawText(), DocumentSerializerOptions);
            }

            return new ModelRegistry.RetriesDescriptor
            {
                Max = retries?.Max ?? 0,
                BackoffMs = retries?.BackoffMs ?? 0
            };
        }

        private static ModelRegistry.IntegrityDescriptor? ConvertIntegrity(ModelRegistryIntegrity? integrity, Dictionary<string, JsonElement> additionalProperties)
        {
            if (integrity == null && additionalProperties.TryGetValue("integrity", out var element) && element.ValueKind == JsonValueKind.Object)
            {
                integrity = JsonSerializer.Deserialize<ModelRegistryIntegrity>(element.GetRawText(), DocumentSerializerOptions);
            }

            if (integrity == null)
            {
                return null;
            }

            return new ModelRegistry.IntegrityDescriptor
            {
                Sha256 = integrity.Sha256 ?? string.Empty
            };
        }

        private static string? Prefer(string? explicitValue, Dictionary<string, JsonElement> additionalProperties, string propertyName)
        {
            if (!string.IsNullOrWhiteSpace(explicitValue))
            {
                return explicitValue;
            }

            if (additionalProperties.TryGetValue(propertyName, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }

            return null;
        }


        private string BuildCacheKey(string? registryUrl)
        {
            var normalizedUrl = string.IsNullOrWhiteSpace(registryUrl) ? "__embedded" : registryUrl;
            return $"{_cacheFilePath}::{normalizedUrl}";
        }

        private bool IsExpired(DateTime timestampUtc)
        {
            var ttl = _options.SharedCacheTtl;
            if (ttl <= TimeSpan.Zero)
            {
                return false;
            }

            if (timestampUtc == default)
            {
                return true;
            }

            return DateTime.UtcNow - timestampUtc > ttl;
        }

        public static void InvalidateSharedCache(string? cacheNamespace = null, string? registryUrl = null)
        {
            if (string.IsNullOrWhiteSpace(cacheNamespace) && string.IsNullOrWhiteSpace(registryUrl))
            {
                SharedCache.Clear();
                return;
            }

            if (string.IsNullOrWhiteSpace(cacheNamespace))
            {
                foreach (var key in SharedCache.Keys)
                {
                    if (registryUrl == null || key.EndsWith($"::{registryUrl}", StringComparison.OrdinalIgnoreCase))
                    {
                        SharedCache.TryRemove(key, out _);
                    }
                }
                return;
            }

            var normalizedUrl = string.IsNullOrWhiteSpace(registryUrl) ? "__embedded" : registryUrl;
            SharedCache.TryRemove($"{cacheNamespace}::{normalizedUrl}", out _);
        }

        private static ModelRegistryLoaderOptions BuildDefaultOptions()
        {
            var enableEnv = Environment.GetEnvironmentVariable("BRAINARR_REGISTRY_SHARED_CACHE");
            var enable = true;
            if (!string.IsNullOrWhiteSpace(enableEnv))
            {
                if (bool.TryParse(enableEnv, out var parsedBool))
                {
                    enable = parsedBool;
                }
                else if (int.TryParse(enableEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                {
                    enable = numeric != 0;
                }
            }

            var ttl = TimeSpan.FromMinutes(10);
            var ttlEnv = Environment.GetEnvironmentVariable("BRAINARR_REGISTRY_SHARED_CACHE_TTL_SECONDS");
            if (!string.IsNullOrWhiteSpace(ttlEnv) && double.TryParse(ttlEnv, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
            {
                ttl = TimeSpan.FromSeconds(seconds);
            }

            return new ModelRegistryLoaderOptions
            {
                EnableSharedCache = enable,
                SharedCacheTtl = ttl
            };
        }
        private static bool Validate(ModelRegistry? registry)
        {
            if (registry == null)
            {
                return false;
            }

            if (!string.Equals(registry.Version, "1", StringComparison.Ordinal))
            {
                return false;
            }

            if (registry.Providers == null || registry.Providers.Count == 0)
            {
                return false;
            }

            foreach (var provider in registry.Providers)
            {
                if (provider == null)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(provider.Slug) ||
                    provider.Models == null ||
                    provider.Models.Count == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task PersistCacheAsync(string json, CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(_cacheFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(json).ConfigureAwait(false);
        }

        private string? ReadEtag()
        {
            lock (_fileLock)
            {
                if (!File.Exists(_etagFilePath))
                {
                    return null;
                }

                return File.ReadAllText(_etagFilePath).Trim();
            }
        }

        private void WriteEtag(string? etag)
        {
            lock (_fileLock)
            {
                if (string.IsNullOrWhiteSpace(etag))
                {
                    if (File.Exists(_etagFilePath))
                    {
                        File.Delete(_etagFilePath);
                    }

                    return;
                }

                File.WriteAllText(_etagFilePath, etag);
            }
        }

        private static string GetDefaultCachePath()
        {
            var directory = Path.Combine(Path.GetTempPath(), "Brainarr", "ModelRegistry");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "registry.json");
        }

        private static string ResolveRelativeToBaseDirectory(string relativePath)
        {
            var baseDirectory = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDirectory, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var current = baseDirectory;
            for (var i = 0; i < 6; i++)
            {
                current = Path.GetFullPath(Path.Combine(current, ".."));
                candidate = Path.Combine(current, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(baseDirectory, relativePath);
        }
    }
}
