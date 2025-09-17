using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

    public sealed class ModelRegistryLoader
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly HttpClient _httpClient;
        private readonly string _cacheFilePath;
        private readonly string _etagFilePath;
        private readonly string _embeddedRegistryPath;
        private readonly object _fileLock = new();

        public ModelRegistryLoader(HttpClient? httpClient = null, string? cacheFilePath = null, string? embeddedRegistryPath = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _cacheFilePath = cacheFilePath ?? GetDefaultCachePath();
            _etagFilePath = _cacheFilePath + ".etag";
            _embeddedRegistryPath = embeddedRegistryPath ?? ResolveRelativeToBaseDirectory(Path.Combine("docs", "models.example.json"));
        }

        public async Task<ModelRegistryLoadResult> LoadAsync(string? registryUrl, CancellationToken cancellationToken = default)
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

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var registry = Deserialize(json);

                if (registry == null)
                {
                    return await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
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
                using var stream = new FileStream(_cacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var registry = await JsonSerializer.DeserializeAsync<ModelRegistry>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
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

            await using var stream = new FileStream(_embeddedRegistryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var registry = await JsonSerializer.DeserializeAsync<ModelRegistry>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            return new ModelRegistryLoadResult(registry, ModelRegistryLoadSource.Embedded, null);
        }

        private static ModelRegistry? Deserialize(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<ModelRegistry>(json, SerializerOptions);
            }
            catch
            {
                return null;
            }
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
            await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
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
