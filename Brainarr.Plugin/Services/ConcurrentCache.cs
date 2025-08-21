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
    public class ConcurrentCache<TKey, TValue> : IDisposable
    {
        private readonly ConcurrentDictionary<TKey, CacheEntry> _cache;
        private readonly int _maxSize;
        private readonly TimeSpan _defaultExpiration;
        private readonly Logger _logger;
        private readonly Timer _cleanupTimer;
        private bool _disposed = false;

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
            // PERFORMANCE FIX: Optimized LRU eviction to O(n log n) from O(n²)
            var itemsToEvict = Math.Max(1, _cache.Count - _maxSize + 1);
            var oldestEntries = _cache
                .OrderBy(kv => kv.Value.LastAccessed)
                .Take(itemsToEvict)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in oldestEntries)
            {
                _cache.TryRemove(key, out _);
            }
            
            if (oldestEntries.Count > 0)
            {
                _logger?.Debug($"Evicted {oldestEntries.Count} cache entries");
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cleanupTimer?.Dispose();
                    _cache.Clear();
                }
                _disposed = true;
            }
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