using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Thread-safe cache with configurable size limits and expiration
    /// </summary>
    public class ConcurrentCache<TKey, TValue>
    {
        private readonly ConcurrentDictionary<TKey, CacheEntry> _cache;
        private readonly int _maxSize;
        private readonly TimeSpan _defaultExpiration;
        private readonly Logger _logger;
        private readonly Timer _cleanupTimer;

        public ConcurrentCache(int maxSize = 1000, TimeSpan? defaultExpiration = null, Logger logger = null)
        {
            _cache = new ConcurrentDictionary<TKey, CacheEntry>();
            _maxSize = maxSize;
            _defaultExpiration = defaultExpiration ?? TimeSpan.FromHours(1);
            _logger = logger;

            // Setup cleanup timer to run every 5 minutes
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                entry.LastAccessed = DateTime.UtcNow;
                value = entry.Value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        public void Set(TKey key, TValue value, TimeSpan? expiration = null)
        {
            var exp = expiration ?? _defaultExpiration;
            var entry = new CacheEntry
            {
                Value = value,
                CreatedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(exp)
            };

            _cache.AddOrUpdate(key, entry, (k, old) => entry);

            // Check if we need to evict entries
            if (_cache.Count > _maxSize)
            {
                EvictLeastRecentlyUsed();
            }
        }

        public void Clear()
        {
            _cache.Clear();
            _logger?.Debug("Cache cleared");
        }

        public int Count => _cache.Count;

        private void EvictLeastRecentlyUsed()
        {
            var toRemove = _cache.Values
                .OrderBy(e => e.LastAccessed)
                .Take(_cache.Count - _maxSize + 1)
                .ToList();

            foreach (var entry in toRemove)
            {
                var keyToRemove = _cache.FirstOrDefault(kv => kv.Value == entry).Key;
                if (keyToRemove != null)
                {
                    _cache.TryRemove(keyToRemove, out _);
                }
            }
        }

        private void CleanupExpiredEntries(object state)
        {
            var expiredKeys = new List<TKey>();
            foreach (var kvp in _cache)
            {
                if (kvp.Value.IsExpired)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                _logger?.Debug($"Cleaned up {expiredKeys.Count} expired cache entries");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _cache.Clear();
        }

        private class CacheEntry
        {
            public TValue Value { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessed { get; set; }
            public DateTime ExpiresAt { get; set; }
            public bool IsExpired => DateTime.UtcNow > ExpiresAt;
        }
    }
}