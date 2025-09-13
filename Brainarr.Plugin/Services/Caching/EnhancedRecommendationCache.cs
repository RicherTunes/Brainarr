#if BRAINARR_EXPERIMENTAL_CACHE
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
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
        Task<bool> TryGetAsync(TKey key, out TValue value);
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
                return CacheResult<List<ImportListItemInfo>>.Error(ex);
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
        public async Task<bool> TryGetAsync(string key, out List<ImportListItemInfo> value)
        {
            var result = await GetAsync(key);
            value = result.Value;
            return result.Found;
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

    /// <summary>
    /// LRU (Least Recently Used) cache implementation.
    /// </summary>
    public class LRUCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<LRUCacheItem>> _cache;
        private readonly LinkedList<LRUCacheItem> _lruList;
        private readonly ReaderWriterLockSlim _lock;

        public LRUCache(int capacity)
        {
            _capacity = capacity;
            _cache = new Dictionary<TKey, LinkedListNode<LRUCacheItem>>(capacity);
            _lruList = new LinkedList<LRUCacheItem>();
            _lock = new ReaderWriterLockSlim();
        }

        public int Count => _cache.Count;

        public bool TryGet(TKey key, out TValue value)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        // Move to front (most recently used)
                        _lruList.Remove(node);
                        _lruList.AddFirst(node);
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                    
                    value = node.Value.Value;
                    return true;
                }
                
                value = default;
                return false;
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }

        public void Set(TKey key, TValue value)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_cache.TryGetValue(key, out var existingNode))
                {
                    // Update existing
                    _lruList.Remove(existingNode);
                    existingNode.Value.Value = value;
                    _lruList.AddFirst(existingNode);
                }
                else
                {
                    // Add new
                    if (_cache.Count >= _capacity)
                    {
                        // Evict least recently used
                        var lru = _lruList.Last;
                        _cache.Remove(lru.Value.Key);
                        _lruList.RemoveLast();
                    }
                    
                    var cacheItem = new LRUCacheItem { Key = key, Value = value };
                    var node = _lruList.AddFirst(cacheItem);
                    _cache[key] = node;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Remove(TKey key)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _cache.Remove(key);
                    _lruList.Remove(node);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _cache.Clear();
                _lruList.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IEnumerable<TKey> GetExpiredKeys()
        {
            _lock.EnterReadLock();
            try
            {
                // This would check for expired entries based on TTL
                // For now, return empty as expiration is handled in CacheEntry
                return Enumerable.Empty<TKey>();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public List<KeyValuePair<TKey, int>> GetTopKeys(int count)
        {
            _lock.EnterReadLock();
            try
            {
                return _lruList
                    .Take(count)
                    .Select((item, index) => new KeyValuePair<TKey, int>(item.Key, _lruList.Count - index))
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public int Sum(Func<KeyValuePair<TKey, TValue>, int> selector)
        {
            _lock.EnterReadLock();
            try
            {
                return _cache.Sum(kvp => selector(new KeyValuePair<TKey, TValue>(kvp.Key, kvp.Value.Value.Value)));
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private class LRUCacheItem
        {
            public TKey Key { get; set; }
            public TValue Value { get; set; }
        }
    }

    /// <summary>
    /// Weak reference cache for GC-recoverable items.
    /// </summary>
    public class WeakReferenceCache<TKey, TValue> where TValue : class
    {
        private readonly ConcurrentDictionary<TKey, WeakReference> _cache = new();

        public int Count => _cache.Count(kvp => kvp.Value.IsAlive);

        public bool TryGet(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var weakRef) && weakRef.IsAlive)
            {
                value = weakRef.Target as TValue;
                return value != null;
            }
            
            value = null;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            _cache[key] = new WeakReference(value);
        }

        public void Remove(TKey key)
        {
            _cache.TryRemove(key, out _);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public void Compact()
        {
            // Remove dead references
            var deadKeys = _cache.Where(kvp => !kvp.Value.IsAlive).Select(kvp => kvp.Key).ToList();
            foreach (var key in deadKeys)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    public interface IDistributedCache
    {
        Task<T> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, CacheOptions options);
        Task RemoveAsync(string key);
        Task ClearAsync();
    }

    public class CacheOptions
    {
        public TimeSpan? AbsoluteExpiration { get; set; }
        public TimeSpan? SlidingExpiration { get; set; }
        public CachePriority Priority { get; set; } = CachePriority.Normal;
        public bool UseDistributedCache { get; set; } = true;
        public Dictionary<string, object> Tags { get; set; }

        public TimeSpan GetEffectiveTTL()
        {
            return SlidingExpiration ?? AbsoluteExpiration ?? TimeSpan.FromMinutes(30);
        }

        public static CacheOptions Default => new()
        {
            AbsoluteExpiration = TimeSpan.FromMinutes(30)
        };

        public static CacheOptions ShortLived => new()
        {
            AbsoluteExpiration = TimeSpan.FromMinutes(5)
        };

        public static CacheOptions LongLived => new()
        {
            AbsoluteExpiration = TimeSpan.FromHours(2),
            Priority = CachePriority.High
        };

        public static CacheOptions Sliding => new()
        {
            SlidingExpiration = TimeSpan.FromMinutes(15)
        };
    }

    public enum CachePriority
    {
        Low,
        Normal,
        High
    }

    public enum CacheLevel
    {
        Memory,
        WeakReference,
        Distributed
    }

    public class CacheResult<T>
    {
        public bool Found { get; set; }
        public T Value { get; set; }
        public CacheLevel? Level { get; set; }
        public Exception Error { get; set; }

        public static CacheResult<T> Hit(T value, CacheLevel level)
        {
            return new CacheResult<T> { Found = true, Value = value, Level = level };
        }

        public static CacheResult<T> Miss()
        {
            return new CacheResult<T> { Found = false };
        }

        public static CacheResult<T> Error(Exception ex)
        {
            return new CacheResult<T> { Found = false, Error = ex };
        }
    }

    public class CacheStatistics
    {
        public long TotalHits { get; set; }
        public long TotalMisses { get; set; }
        public double HitRatio { get; set; }
        public int MemoryCacheSize { get; set; }
        public int WeakCacheSize { get; set; }
        public double AverageAccessTime { get; set; }
        public Dictionary<CacheLevel, long> HitsByLevel { get; set; }
        public List<KeyValuePair<string, int>> TopAccessedKeys { get; set; }
        public long MemoryUsageBytes { get; set; }
        public DateTime? LastMaintenanceRun { get; set; }
    }

    public class CacheConfiguration
    {
        public int MaxMemoryEntries { get; set; } = 1000;
        public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool EnableDistributedCache { get; set; } = false;
        public bool EnableWeakReferences { get; set; } = true;

        public static CacheConfiguration Default => new();

        public static CacheConfiguration HighPerformance => new()
        {
            MaxMemoryEntries = 5000,
            MaintenanceInterval = TimeSpan.FromMinutes(10)
        };

        public static CacheConfiguration LowMemory => new()
        {
            MaxMemoryEntries = 100,
            MaintenanceInterval = TimeSpan.FromMinutes(2),
            EnableWeakReferences = true
        };
    }

    internal class CacheMetrics
    {
        private long _totalHits;
        private long _totalMisses;
        private long _totalErrors;
        private readonly ConcurrentDictionary<CacheLevel, long> _hitsByLevel = new();
        private readonly ConcurrentBag<double> _accessTimes = new();

        public long TotalHits => _totalHits;
        public long TotalMisses => _totalMisses;
        public DateTime? LastMaintenanceRun { get; set; }
        
        public void RecordHit(CacheLevel level, TimeSpan duration)
        {
            Interlocked.Increment(ref _totalHits);
            _hitsByLevel.AddOrUpdate(level, 1, (_, count) => count + 1);
            RecordAccessTime(duration.TotalMilliseconds);
        }

        public void RecordMiss(TimeSpan duration)
        {
            Interlocked.Increment(ref _totalMisses);
            RecordAccessTime(duration.TotalMilliseconds);
        }

        public void RecordError()
        {
            Interlocked.Increment(ref _totalErrors);
        }

        public void RecordSet(int itemCount)
        {
            // Track set operations if needed
        }

        public double GetHitRatio()
        {
            var total = _totalHits + _totalMisses;
            return total > 0 ? (double)_totalHits / total : 0;
        }

        public double GetAverageAccessTime()
        {
            return _accessTimes.Any() ? _accessTimes.Average() : 0;
        }

        public Dictionary<CacheLevel, long> GetHitsByLevel()
        {
            return new Dictionary<CacheLevel, long>(_hitsByLevel);
        }

        public void Reset()
        {
            _totalHits = 0;
            _totalMisses = 0;
            _totalErrors = 0;
            _hitsByLevel.Clear();
            _accessTimes.Clear();
        }

        private void RecordAccessTime(double milliseconds)
        {
            _accessTimes.Add(milliseconds);

            // Keep only last 1000 access times
            while (_accessTimes.Count > 1000)
            {
                _accessTimes.TryTake(out _);
            }
        }
    }
}
#endif
