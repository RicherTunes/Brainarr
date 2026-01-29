using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Registry
{
    /// <summary>
    /// Per-instance implementation of IModelRegistryCache for test isolation.
    /// Each test can create its own instance without cross-test pollution.
    /// </summary>
    public sealed class InMemoryModelRegistryCache : IModelRegistryCache
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

        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

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
