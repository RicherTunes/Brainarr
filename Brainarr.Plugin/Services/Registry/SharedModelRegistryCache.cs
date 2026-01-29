using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Registry
{
    /// <summary>
    /// Production singleton implementation of IModelRegistryCache.
    /// Register as Singleton in DI; avoid static state to prevent cross-container divergence.
    /// For test isolation, use <see cref="InMemoryModelRegistryCache"/> instead.
    /// </summary>
    public sealed class SharedModelRegistryCache : IModelRegistryCache
    {
        private sealed class CacheEntry
        {
            public CacheEntry()
            {
                Lock = new SemaphoreSlim(1, 1);
            }

            public SemaphoreSlim Lock { get; }
            public ModelRegistryLoadResult? Value { get; set; }
            public DateTime TimestampUtc { get; set; }
        }

        // Instance field, not static - registered as Singleton in DI
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Default instance for backward compatibility. Prefer DI registration as Singleton.
        /// </summary>
        public static SharedModelRegistryCache Instance { get; } = new SharedModelRegistryCache();

        /// <summary>
        /// Creates a new cache instance. For production, register as Singleton in DI.
        /// </summary>
        public SharedModelRegistryCache()
        {
        }

        public bool TryGet(string key, TimeSpan ttl, out ModelRegistryLoadResult? result)
        {
            result = null;

            if (!_cache.TryGetValue(key, out var entry))
            {
                return false;
            }

            if (entry.Value == null)
            {
                return false;
            }

            if (IsExpired(entry.TimestampUtc, ttl))
            {
                return false;
            }

            result = entry.Value;
            return true;
        }

        public void Set(string key, ModelRegistryLoadResult result)
        {
            var entry = _cache.GetOrAdd(key, _ => new CacheEntry());
            entry.Value = result;
            entry.TimestampUtc = DateTime.UtcNow;
        }

        public void Invalidate(string key)
        {
            _cache.TryRemove(key, out _);
        }

        public void InvalidateAll(string? keyPattern = null)
        {
            if (string.IsNullOrWhiteSpace(keyPattern))
            {
                _cache.Clear();
                return;
            }

            foreach (var key in _cache.Keys)
            {
                if (key.Contains(keyPattern, StringComparison.OrdinalIgnoreCase))
                {
                    _cache.TryRemove(key, out _);
                }
            }
        }

        public async Task<T> WithLockAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            var entry = _cache.GetOrAdd(key, _ => new CacheEntry());

            await entry.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await action().ConfigureAwait(false);
            }
            finally
            {
                entry.Lock.Release();
            }
        }

        private static bool IsExpired(DateTime timestampUtc, TimeSpan ttl)
        {
            if (ttl <= TimeSpan.Zero)
            {
                return false;
            }

            if (timestampUtc == default)
            {
                return true;
            }

            return DateTime.UtcNow - timestampUtc > ttl;
        }
    }
}
