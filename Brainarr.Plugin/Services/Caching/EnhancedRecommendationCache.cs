// NOTE: This file is excluded from the trailing-whitespace and dotnet-format-whitespace
// pre-commit hooks (.pre-commit-config.yaml) because its multi-level cache logic uses
// intentional alignment formatting that conflicts with automatic whitespace trimming.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Caching
{
    /// <summary>
    /// Enhanced cache with TTL, LRU eviction, statistics, and distributed cache support.
    /// </summary>
    public interface IEnhancedCache<TKey, TValue>
    {
        Task<CacheResult<TValue>> GetAsync(TKey key);
        Task SetAsync(TKey key, TValue value, CacheOptions options = null);
        Task<(bool Found, TValue Value)> TryGetAsync(TKey key);
        Task RemoveAsync(TKey key);
        Task ClearAsync();
        CacheStatistics GetStatistics();
        Task WarmupAsync(IEnumerable<KeyValuePair<TKey, TValue>> items);
    }

    public class EnhancedRecommendationCache : IEnhancedCache<string, List<ImportListItemInfo>>, IDisposable
    {
        private readonly Logger _logger;
        private readonly LRUCache<string, CacheEntry> _memoryCache;
        private readonly IDistributedCache _distributedCache;
        private readonly CacheConfiguration _config;
        private readonly CacheMetrics _metrics;
        private readonly Timer _maintenanceTimer;
        private readonly SemaphoreSlim _cacheLock;
        private readonly WeakReferenceCache<string, List<ImportListItemInfo>> _weakCache;

        public EnhancedRecommendationCache(
            Logger logger,
            CacheConfiguration config = null,
            IDistributedCache distributedCache = null)
        {
            _logger = logger;
            _config = config ?? CacheConfiguration.Default;
            _distributedCache = distributedCache;
            _memoryCache = new LRUCache<string, CacheEntry>(_config.MaxMemoryEntries);
            _weakCache = new WeakReferenceCache<string, List<ImportListItemInfo>>();
            _metrics = new CacheMetrics();
            _cacheLock = new SemaphoreSlim(1, 1);

            // Start maintenance timer
            _maintenanceTimer = new Timer(
                PerformMaintenance,
                null,
                _config.MaintenanceInterval,
                _config.MaintenanceInterval);
        }

        /// <summary>
        /// Gets a value from the cache with comprehensive fallback strategy.
        /// </summary>
        public async Task<CacheResult<List<ImportListItemInfo>>> GetAsync(string key)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Level 1: Memory cache (fastest)
                if (_memoryCache.TryGet(key, out var entry))
                {
                    if (!entry.IsExpired)
                    {
                        _metrics.RecordHit(CacheLevel.Memory, stopwatch.Elapsed);

                        // Refresh TTL if sliding expiration
                        if (entry.Options?.SlidingExpiration != null)
                        {
                            entry.RefreshExpiration();
                        }

                        return CacheResult<List<ImportListItemInfo>>.Hit(
                            CloneData(entry.Data),
                            CacheLevel.Memory);
                    }
                    else
                    {
                        // Expired entry - remove it
                        _memoryCache.Remove(key);
                    }
                }

                // Level 2: Weak reference cache (recovered from GC)
                if (_weakCache.TryGet(key, out var weakData))
                {
                    _metrics.RecordHit(CacheLevel.WeakReference, stopwatch.Elapsed);

                    // Promote back to memory cache
                    await SetAsync(key, weakData, CacheOptions.Default);

                    return CacheResult<List<ImportListItemInfo>>.Hit(
                        CloneData(weakData),
                        CacheLevel.WeakReference);
                }

                // Level 3: Distributed cache (if configured)
                if (_distributedCache != null)
                {
                    var distributedData = await _distributedCache.GetAsync<List<ImportListItemInfo>>(key);
                    if (distributedData != null)
                    {
                        _metrics.RecordHit(CacheLevel.Distributed, stopwatch.Elapsed);

                        // Promote to memory cache
                        await SetAsync(key, distributedData, CacheOptions.Default);

                        return CacheResult<List<ImportListItemInfo>>.Hit(
                            CloneData(distributedData),
                            CacheLevel.Distributed);
                    }
                }

                _metrics.RecordMiss(stopwatch.Elapsed);
                return CacheResult<List<ImportListItemInfo>>.Miss();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Cache get failed for key: {key}");
                _metrics.RecordError();
                return CacheResult<List<ImportListItemInfo>>.Failure(ex);
            }
        }

        /// <summary>
        /// Sets a value in the cache with multi-level storage.
        /// </summary>
        public async Task SetAsync(string key, List<ImportListItemInfo> value, CacheOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Cache key cannot be empty", nameof(key));

            if (value == null)
            {
                _logger.Debug($"Not caching null value for key: {key}");
                return;
            }

            options = options ?? CacheOptions.Default;

            try
            {
                await _cacheLock.WaitAsync();

                // Create cache entry
                var entry = new CacheEntry
                {
                    Key = key,
                    Data = value,
                    Options = options,
                    CreatedAt = DateTime.UtcNow,
                    LastAccessed = DateTime.UtcNow,
                    AccessCount = 0
                };

                // Level 1: Always store in memory cache
                _memoryCache.Set(key, entry);

                // Level 2: Store in weak reference cache for GC recovery
                _weakCache.Set(key, value);

                // Level 3: Store in distributed cache if configured
                if (_distributedCache != null && options.UseDistributedCache)
                {
                    await _distributedCache.SetAsync(key, value, options);
                }

                _metrics.RecordSet(value.Count);

                _logger.Debug($"Cached {value.Count} items with key: {key} " +
                            $"(TTL: {options.GetEffectiveTTL().TotalMinutes:F1} minutes)");
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        /// <summary>
        /// Tries to get a value from the cache.
        /// </summary>
        public async Task<(bool Found, List<ImportListItemInfo> Value)> TryGetAsync(string key)
        {
            var result = await GetAsync(key);
            return (result.Found, result.Value);
        }

        /// <summary>
        /// Removes a value from all cache levels.
        /// </summary>
        public async Task RemoveAsync(string key)
        {
            await _cacheLock.WaitAsync();
            try
            {
                _memoryCache.Remove(key);
                _weakCache.Remove(key);

                if (_distributedCache != null)
                {
                    await _distributedCache.RemoveAsync(key);
                }

                _logger.Debug($"Removed cache entry: {key}");
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        /// <summary>
        /// Clears all cache levels.
        /// </summary>
        public async Task ClearAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                _memoryCache.Clear();
                _weakCache.Clear();

                if (_distributedCache != null)
                {
                    await _distributedCache.ClearAsync();
                }

                _metrics.Reset();
                _logger.Info("Cache cleared at all levels");
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        /// <summary>
        /// Pre-warms the cache with data.
        /// </summary>
        public async Task WarmupAsync(IEnumerable<KeyValuePair<string, List<ImportListItemInfo>>> items)
        {
            var count = 0;
            foreach (var item in items)
            {
                await SetAsync(item.Key, item.Value, CacheOptions.LongLived);
                count++;
            }

            _logger.Info($"Cache warmed up with {count} entries");
        }

        /// <summary>
        /// Gets comprehensive cache statistics.
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                TotalHits = _metrics.TotalHits,
                TotalMisses = _metrics.TotalMisses,
                HitRatio = _metrics.GetHitRatio(),
                MemoryCacheSize = _memoryCache.Count,
                WeakCacheSize = _weakCache.Count,
                AverageAccessTime = _metrics.GetAverageAccessTime(),
                HitsByLevel = _metrics.GetHitsByLevel(),
                TopAccessedKeys = _memoryCache.GetTopKeys(10),
                MemoryUsageBytes = EstimateMemoryUsage(),
                LastMaintenanceRun = _metrics.LastMaintenanceRun
            };
        }

        /// <summary>
        /// Generates a cache key with optional versioning.
        /// </summary>
        public static string GenerateCacheKey(
            string provider,
            int maxRecommendations,
            string libraryFingerprint,
            int? version = null)
        {
            var versionSuffix = version.HasValue ? $"_v{version}" : "";
            var keyData = $"{provider}|{maxRecommendations}|{libraryFingerprint}{versionSuffix}";

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyData));
                var shortHash = Convert.ToBase64String(hash)
                    .Replace("+", "")
                    .Replace("/", "")
                    .Substring(0, 12);

                return $"rec_{provider}_{maxRecommendations}_{shortHash}";
            }
        }

        private void PerformMaintenance(object state)
        {
            try
            {
                _cacheLock.Wait();

                // Remove expired entries
                var expiredKeys = _memoryCache.GetExpiredKeys();
                foreach (var key in expiredKeys)
                {
                    _memoryCache.Remove(key);
                }

                // Compact weak reference cache
                _weakCache.Compact();

                // Update metrics
                _metrics.LastMaintenanceRun = DateTime.UtcNow;

                if (expiredKeys.Any())
                {
                    _logger.Debug($"Cache maintenance: removed {expiredKeys.Count()} expired entries");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Cache maintenance failed");
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private List<ImportListItemInfo> CloneData(List<ImportListItemInfo> data)
        {
            // Deep clone to prevent external modifications
            return data?.Select(item => new ImportListItemInfo
            {
                Artist = item.Artist,
                Album = item.Album,
                ReleaseDate = item.ReleaseDate,
                ArtistMusicBrainzId = item.ArtistMusicBrainzId,
                AlbumMusicBrainzId = item.AlbumMusicBrainzId
            }).ToList();
        }

        private long EstimateMemoryUsage()
        {
            // Rough estimation of memory usage
            var bytesPerItem = 200; // Approximate size of ImportListItemInfo
            var totalItems = _memoryCache.Sum(entry => entry.Value?.Data?.Count ?? 0);
            var overhead = _memoryCache.Count * 100; // Cache entry overhead

            return (totalItems * bytesPerItem) + overhead;
        }

        public void Dispose()
        {
            _maintenanceTimer?.Dispose();
            _cacheLock?.Dispose();
        }

        private class CacheEntry
        {
            public string Key { get; set; }
            public List<ImportListItemInfo> Data { get; set; }
            public CacheOptions Options { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessed { get; set; }
            public int AccessCount { get; set; }

            public bool IsExpired
            {
                get
                {
                    var ttl = Options?.GetEffectiveTTL() ?? TimeSpan.FromMinutes(30);
                    var expiryTime = Options?.SlidingExpiration != null ?
                        LastAccessed.Add(ttl) :
                        CreatedAt.Add(ttl);

                    return DateTime.UtcNow > expiryTime;
                }
            }

            public void RefreshExpiration()
            {
                LastAccessed = DateTime.UtcNow;
                AccessCount++;
            }
        }
    }
}
