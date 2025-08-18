using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly IMemoryCache _cache;
        private readonly Logger _logger;
        private readonly TimeSpan _defaultCacheDuration;
        private readonly ConcurrentDictionary<string, byte> _cacheKeys;

        public RecommendationCache(Logger logger, TimeSpan? defaultDuration = null)
        {
            _logger = logger;
            _defaultCacheDuration = defaultDuration ?? TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes);
            _cacheKeys = new ConcurrentDictionary<string, byte>();
            
            var cacheOptions = new MemoryCacheOptions
            {
                SizeLimit = BrainarrConstants.MaxCacheEntries,
                ExpirationScanFrequency = TimeSpan.FromMinutes(5)
            };
            
            _cache = new MemoryCache(cacheOptions);
        }

        public bool TryGet(string cacheKey, out List<ImportListItemInfo> recommendations)
        {
            if (_cache.TryGetValue(cacheKey, out object cached) && cached is List<ImportListItemInfo> items)
            {
                recommendations = items;
                _logger.Debug($"Cache hit for key: {cacheKey} ({items.Count} recommendations)");
                return true;
            }

            recommendations = null;
            _logger.Debug($"Cache miss for key: {cacheKey}");
            return false;
        }

        public void Set(string cacheKey, List<ImportListItemInfo> recommendations, TimeSpan? duration = null)
        {
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSize(1) // Each entry counts as 1 toward the size limit
                .SetSlidingExpiration(duration ?? _defaultCacheDuration)
                .SetAbsoluteExpiration(DateTime.UtcNow.Add((duration ?? _defaultCacheDuration) * 2)) // Max 2x sliding expiration
                .RegisterPostEvictionCallback(OnCacheEviction);

            _cache.Set(cacheKey, recommendations, cacheEntryOptions);
            _cacheKeys.TryAdd(cacheKey, 0); // Use 0 as dummy value
            
            var count = recommendations?.Count ?? 0;
            _logger.Info($"Cached {count} recommendations with key: {cacheKey} (expires in {(duration ?? _defaultCacheDuration).TotalMinutes} minutes)");
        }

        public void Clear()
        {
            foreach (var key in _cacheKeys.Keys)
            {
                _cache.Remove(key);
            }
            _cacheKeys.Clear();
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

        private void OnCacheEviction(object key, object value, EvictionReason reason, object state)
        {
            if (key is string cacheKey)
            {
                _cacheKeys.TryRemove(cacheKey, out _);
                _logger.Debug($"Cache entry evicted: {cacheKey} (Reason: {reason})");
            }
        }

        public void Dispose()
        {
            _cache?.Dispose();
        }
    }
}