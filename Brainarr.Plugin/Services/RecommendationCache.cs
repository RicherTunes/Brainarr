using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IRecommendationCache : IDisposable
    {
        bool TryGet(string cacheKey, out List<ImportListItemInfo> recommendations);
        void Set(string cacheKey, List<ImportListItemInfo> recommendations, TimeSpan? duration = null);
        void Clear();
        string GenerateCacheKey(string provider, int maxRecommendations, string libraryFingerprint);
    }

    public class RecommendationCache : IRecommendationCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache;
        private readonly Logger _logger;
        private readonly TimeSpan _defaultCacheDuration;
        private readonly Timer _cleanupTimer;
        private readonly object _cleanupLock = new object();
        private volatile bool _disposed = false;
        private const int SmallCacheThreshold = 50;
        private const int ParallelProcessingThreshold = 1000;

        private class CacheEntry
        {
            public List<ImportListItemInfo> Data { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        public RecommendationCache(Logger logger, TimeSpan? defaultDuration = null)
        {
            _logger = logger;
            _defaultCacheDuration = defaultDuration ?? TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes);
            _cache = new ConcurrentDictionary<string, CacheEntry>();
            
            // Start timer-based cleanup every 5 minutes
            _cleanupTimer = new Timer(TimerBasedCleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public bool TryGet(string cacheKey, out List<ImportListItemInfo> recommendations)
        {
            recommendations = null;

            if (_disposed)
            {
                return false;
            }

            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (entry.ExpiresAt > DateTime.UtcNow)
                {
                    recommendations = entry.Data;
                    _logger.Debug($"Cache hit for key: {cacheKey} ({entry.Data.Count} recommendations)");
                    return true;
                }
                else
                {
                    // Remove expired entry
                    _cache.TryRemove(cacheKey, out _);
                    _logger.Debug($"Cache expired for key: {cacheKey}");
                }
            }

            _logger.Debug($"Cache miss for key: {cacheKey}");
            return false;
        }

        public void Set(string cacheKey, List<ImportListItemInfo> recommendations, TimeSpan? duration = null)
        {
            if (_disposed)
            {
                return;
            }

            var actualDuration = duration ?? _defaultCacheDuration;
            var entry = new CacheEntry
            {
                Data = recommendations,
                ExpiresAt = DateTime.UtcNow.Add(actualDuration)
            };

            // Limit cache size by removing oldest entries if needed
            if (_cache.Count >= BrainarrConstants.MaxCacheEntries)
            {
                var oldestKey = _cache
                    .OrderBy(kvp => kvp.Value.ExpiresAt)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault();

                if (oldestKey != null)
                {
                    _cache.TryRemove(oldestKey, out _);
                }
            }

            _cache[cacheKey] = entry;
            _logger.Debug($"Cached {recommendations.Count} recommendations with key: {cacheKey} (expires in {actualDuration.TotalMinutes:F1} minutes)");
        }

        public void Clear()
        {
            if (_disposed)
            {
                return;
            }

            var count = _cache.Count;
            _cache.Clear();
            _logger.Info($"Cleared recommendation cache ({count} entries removed)");
        }

        public string GenerateCacheKey(string provider, int maxRecommendations, string libraryFingerprint)
        {
            var input = $"{provider}:{maxRecommendations}:{libraryFingerprint}";
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Convert.ToBase64String(hashBytes);
            }
        }

        private void TimerBasedCleanup(object state)
        {
            if (_disposed)
            {
                return;
            }

            lock (_cleanupLock)
            {
                if (_disposed)
                {
                    return;
                }

                var cacheSize = _cache.Count;
                
                // Early exit for small caches - they don't need aggressive cleanup
                if (cacheSize < SmallCacheThreshold)
                {
                    _logger.Trace($"Skipping cleanup for small cache ({cacheSize} entries)");
                    return;
                }

                var currentTime = DateTime.UtcNow;
                List<string> expiredKeys;

                // Use parallel processing for large caches
                if (cacheSize >= ParallelProcessingThreshold)
                {
                    expiredKeys = _cache
                        .AsParallel()
                        .Where(kvp => kvp.Value.ExpiresAt <= currentTime)
                        .Select(kvp => kvp.Key)
                        .ToList();
                }
                else
                {
                    expiredKeys = _cache
                        .Where(kvp => kvp.Value.ExpiresAt <= currentTime)
                        .Select(kvp => kvp.Key)
                        .ToList();
                }

                // Remove expired entries in parallel for large sets
                if (expiredKeys.Count >= ParallelProcessingThreshold)
                {
                    Parallel.ForEach(expiredKeys, key =>
                    {
                        _cache.TryRemove(key, out _);
                    });
                }
                else
                {
                    foreach (var key in expiredKeys)
                    {
                        _cache.TryRemove(key, out _);
                    }
                }

                if (expiredKeys.Any())
                {
                    _logger.Debug($"Timer-based cleanup removed {expiredKeys.Count} expired cache entries (cache size: {cacheSize} -> {_cache.Count})");
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _cleanupTimer?.Dispose();
                _disposed = true;
                _logger.Debug("RecommendationCache disposed");
            }
        }
    }
}