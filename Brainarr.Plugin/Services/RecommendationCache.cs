using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IRecommendationCache
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
        private readonly object _cleanupLock = new object();
        private DateTime _lastCleanup = DateTime.UtcNow;

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
        }

        public bool TryGet(string cacheKey, out List<ImportListItemInfo> recommendations)
        {
            recommendations = null;

            // Periodic cleanup
            CleanupExpiredEntries();

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

        private void CleanupExpiredEntries()
        {
            // Only cleanup every 5 minutes
            if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(5))
            {
                return;
            }

            lock (_cleanupLock)
            {
                if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(5))
                {
                    return;
                }

                var expiredKeys = _cache
                    .Where(kvp => kvp.Value.ExpiresAt <= DateTime.UtcNow)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _cache.TryRemove(key, out _);
                }

                if (expiredKeys.Any())
                {
                    _logger.Debug($"Cleaned up {expiredKeys.Count} expired cache entries");
                }

                _lastCleanup = DateTime.UtcNow;
            }
        }
    }
}