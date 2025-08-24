using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;

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
        private readonly object _cleanupLock;
        private DateTime _lastCleanup;

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
            _cleanupLock = new object();
            _lastCleanup = DateTime.UtcNow;
        }

        public bool TryGet(string cacheKey, out List<ImportListItemInfo> recommendations)
        {
            CleanupExpiredEntries();
            
            if (_cache.TryGetValue(cacheKey, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            {
                recommendations = entry.Data;
                _logger.Debug($"Cache hit for key: {cacheKey} ({entry.Data.Count} recommendations)");
                return true;
            }

            recommendations = null;
            _logger.Debug($"Cache miss for key: {cacheKey}");
            return false;
        }

        public void Set(string cacheKey, List<ImportListItemInfo> recommendations, TimeSpan? duration = null)
        {
            var expiration = duration ?? _defaultCacheDuration;
            var entry = new CacheEntry
            {
                Data = recommendations ?? new List<ImportListItemInfo>(),
                ExpiresAt = DateTime.UtcNow.Add(expiration)
            };

            _cache.AddOrUpdate(cacheKey, entry, (key, oldEntry) => entry);
            
            // Limit cache size by removing oldest entries if needed
            if (_cache.Count > BrainarrConstants.MaxCacheEntries)
            {
                CleanupExpiredEntries(force: true);
            }
            
            var count = recommendations?.Count ?? 0;
            _logger.Info($"Cached {count} recommendations with key: {cacheKey} (expires in {expiration.TotalMinutes} minutes)");
        }

        public void Clear()
        {
            _cache.Clear();
            _logger.Info("Recommendation cache cleared");
        }

        public string GenerateCacheKey(string provider, int maxRecommendations, string libraryFingerprint)
        {
            // Create a unique cache key based on provider settings and library state
            var keyData = $"{provider}|{maxRecommendations}|{libraryFingerprint}";
            
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyData));
                var shortHash = Convert.ToBase64String(hash).Substring(0, 8);
                return $"brainarr_recs_{provider}_{maxRecommendations}_{shortHash}";
            }
        }

        private void CleanupExpiredEntries(bool force = false)
        {
            var now = DateTime.UtcNow;
            
            // Only cleanup every 5 minutes unless forced
            if (!force && now - _lastCleanup < TimeSpan.FromMinutes(5))
                return;

            lock (_cleanupLock)
            {
                if (!force && now - _lastCleanup < TimeSpan.FromMinutes(5))
                    return;

                var expiredKeys = new List<string>();
                foreach (var kvp in _cache)
                {
                    if (kvp.Value.ExpiresAt <= now)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                // If forced cleanup and still too many entries, remove oldest ones
                if (force && _cache.Count > BrainarrConstants.MaxCacheEntries)
                {
                    var allEntries = new List<KeyValuePair<string, CacheEntry>>(_cache);
                    var sortedEntries = allEntries.OrderBy(e => e.Value.ExpiresAt).ToList();
                    var toRemove = sortedEntries.Take(_cache.Count - BrainarrConstants.MaxCacheEntries + 1);
                    foreach (var entry in toRemove)
                    {
                        if (!expiredKeys.Contains(entry.Key))
                        {
                            expiredKeys.Add(entry.Key);
                        }
                    }
                }

                foreach (var key in expiredKeys)
                {
                    if (_cache.TryRemove(key, out _))
                    {
                        _logger.Debug($"Cache entry expired/evicted: {key}");
                    }
                }

                if (expiredKeys.Count > 0)
                {
                    _logger.Debug($"Cleaned up {expiredKeys.Count} expired cache entries");
                }

                _lastCleanup = now;
            }
        }
    }
}