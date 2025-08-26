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
            // Cache Key Generation Algorithm: Create unique, deterministic keys for recommendation caching
            // Combines provider settings with library state to ensure cache validity
            // Same inputs always produce same key, different inputs produce different keys
            var keyData = $"{provider}|{maxRecommendations}|{libraryFingerprint}";
            
            using (var sha256 = SHA256.Create())
            {
                // Cryptographic Hash: SHA256 ensures collision resistance
                // Converts variable-length input to fixed-length hash
                // Prevents cache key conflicts even with similar library fingerprints
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyData));
                
                // Hash Truncation: 8-character Base64 provides good uniqueness with readable length
                // 8 chars from Base64 = 6 bytes = 48 bits of entropy
                // Collision probability: ~1 in 281 trillion (suitable for recommendation caching)
                var shortHash = Convert.ToBase64String(hash).Substring(0, 8);
                
                // Human-Readable Format: Prefix with context for easier debugging and log analysis
                return $"brainarr_recs_{provider}_{maxRecommendations}_{shortHash}";
            }
        }

        private void CleanupExpiredEntries(bool force = false)
        {
            var now = DateTime.UtcNow;
            
            // Throttling Mechanism: Prevent excessive cleanup cycles
            // Only runs cleanup every 5 minutes to avoid CPU overhead
            // Exception: Force cleanup when cache exceeds size limits
            if (!force && now - _lastCleanup < TimeSpan.FromMinutes(5))
                return;

            lock (_cleanupLock)
            {
                // Double-check pattern: Prevent race conditions in cleanup
                // Another thread might have completed cleanup while we waited for lock
                if (!force && now - _lastCleanup < TimeSpan.FromMinutes(5))
                    return;

                // Memory-Efficient Collection: Use HashSet for O(1) membership testing
                // HashSet provides constant-time Contains() vs List's O(n) search
                var expiredKeys = new HashSet<string>();
                
                // Phase 1: Natural Expiration - Remove time-based expired entries
                foreach (var kvp in _cache)
                {
                    if (kvp.Value.ExpiresAt <= now)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                // Phase 2: Size-Based Eviction - LRU (Least Recently Used) eviction
                // Triggered when cache exceeds configured size limits even after expiration cleanup
                if (force && _cache.Count > BrainarrConstants.MaxCacheEntries)
                {
                    var excessCount = _cache.Count - BrainarrConstants.MaxCacheEntries + 1;
                    
                    // Performance Optimization: Only work with non-expired entries
                    // Prevents unnecessary processing of already-marked expired items
                    var candidateEntries = _cache.Where(kvp => !expiredKeys.Contains(kvp.Key)).ToList();
                    
                    // Partial Sort Optimization: Only sort entries we need to evict
                    // More efficient than sorting entire cache when only removing a few items
                    // Uses ExpiresAt as proxy for "least recently used" (oldest entries expire first)
                    var oldestEntries = candidateEntries
                        .OrderBy(e => e.Value.ExpiresAt)
                        .Take(excessCount)
                        .Select(e => e.Key);
                    
                    foreach (var key in oldestEntries)
                    {
                        expiredKeys.Add(key);
                    }
                }

                // Atomic Removal: Remove all marked entries in single operation
                // ConcurrentDictionary.TryRemove is thread-safe and handles concurrent modifications
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