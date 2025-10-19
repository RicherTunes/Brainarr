using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace Brainarr.Plugin.Services.Core
{
    /// <summary>
    /// High-performance thread-safe cache with memory-efficient storage
    /// </summary>
    public class ConcurrentCache<TKey, TValue> : IDisposable where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, CacheEntry> _cache;
        private readonly ConcurrentDictionary<TKey, AsyncLazy<TValue>> _pending;
        private readonly int _maxSize;
        private readonly TimeSpan _defaultExpiration;
        private readonly ILogger _logger;
        private readonly Timer _cleanupTimer;
        private readonly ReaderWriterLockSlim _sizeLock;
        private long _currentSize;
        private long _hits;
        private long _misses;
        private long _evictions;
        private long _sequenceCounter;
        private readonly IMetrics? _metrics;

        public ConcurrentCache(
            int maxSize = 1000,
            TimeSpan? defaultExpiration = null,
            ILogger? logger = null,
            IMetrics? metrics = null)
        {
            _maxSize = maxSize > 0 ? maxSize : throw new ArgumentException("Max size must be positive", nameof(maxSize));
            _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(30);
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _cache = new ConcurrentDictionary<TKey, CacheEntry>();
            _pending = new ConcurrentDictionary<TKey, AsyncLazy<TValue>>();
            _sizeLock = new ReaderWriterLockSlim();
            _metrics = metrics;

            // Periodic cleanup every minute
            _cleanupTimer = new Timer(
                _ => CleanupExpired(),
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));
        }

        public async Task<TValue> GetOrAddAsync(
            TKey key,
            Func<TKey, Task<TValue>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            // Fast path: Check if already cached
            if (_cache.TryGetValue(key, out var entry))
            {
                if (!entry.IsExpired)
                {
                    Interlocked.Increment(ref _hits);
                    _metrics?.Record(MetricsNames.CacheHit, 1);
                    entry.UpdateLastAccess(Interlocked.Increment(ref _sequenceCounter));
                    return entry.Value;
                }

                // Remove expired entry
                _cache.TryRemove(key, out _);
                DecrementSize();
            }

            Interlocked.Increment(ref _misses);
            _metrics?.Record(MetricsNames.CacheMiss, 1);

            // Prevent cache stampede by using a single factory per key
            var lazy = _pending.GetOrAdd(key, k => new AsyncLazy<TValue>(() => factory(k)));

            try
            {
                var value = await lazy.GetValueAsync(cancellationToken).ConfigureAwait(false);
                var newEntry = new CacheEntry(value, expiration ?? _defaultExpiration);

                bool wasAdded = false;

                // Add to cache if not already there
                var addedEntry = _cache.AddOrUpdate(key, k =>
                {
                    wasAdded = true;
                    newEntry.SetSequence(Interlocked.Increment(ref _sequenceCounter));
                    IncrementSize();
                    return newEntry;
                }, (k, existing) =>
                {
                    if (!existing.IsExpired)
                    {
                        return existing; // Keep existing valid entry
                    }
                    newEntry.SetSequence(Interlocked.Increment(ref _sequenceCounter));
                    return newEntry; // Replace expired entry (size already counted)
                });

                // Trigger eviction after adding if we added a new entry
                if (wasAdded)
                {
                    await EnsureCapacityAsync().ConfigureAwait(false);
                }

                return addedEntry.Value;
            }
            finally
            {
                // Always remove from pending
                _pending.TryRemove(key, out _);
            }
        }

        public bool TryGet(TKey key, out TValue value)
        {
            value = default;

            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                Interlocked.Increment(ref _hits);
                entry.UpdateLastAccess(Interlocked.Increment(ref _sequenceCounter));
                value = entry.Value;
                return true;
            }

            Interlocked.Increment(ref _misses);
            return false;
        }

        public void Set(TKey key, TValue value, TimeSpan? expiration = null)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var entry = new CacheEntry(value, expiration ?? _defaultExpiration);
            bool isNewEntry = false;

            _cache.AddOrUpdate(key, k =>
            {
                isNewEntry = true;
                entry.SetSequence(Interlocked.Increment(ref _sequenceCounter));
                return entry;
            }, (k, existing) =>
            {
                entry.SetSequence(Interlocked.Increment(ref _sequenceCounter));
                return entry; // Replace existing
            });

            if (isNewEntry)
            {
                IncrementSize();
                // Enforce capacity synchronously to guarantee max size bound
                EnsureCapacitySync();
            }
        }

        public bool Remove(TKey key)
        {
            if (_cache.TryRemove(key, out _))
            {
                DecrementSize();
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _cache.Clear();
            Interlocked.Exchange(ref _currentSize, 0);
        }

        private async Task EnsureCapacityAsync()
        {
            var currentSize = _cache.Count;
            if (currentSize <= _maxSize)
                return;

            await Task.Run(() =>
            {
                _sizeLock.EnterWriteLock();
                try
                {
                    currentSize = _cache.Count;
                    if (currentSize <= _maxSize)
                        return;

                    var toRemoveCount = (int)(currentSize - _maxSize);

                    // Snapshot to avoid concurrent enumeration exceptions
                    var snapshot = _cache.ToArray();
                    var toRemove = snapshot
                        .OrderBy(e => e.Value.LastAccessSequence)
                        .Take(toRemoveCount)
                        .Select(e => e.Key)
                        .ToList();

                    foreach (var key in toRemove)
                    {
                        if (_cache.TryRemove(key, out _))
                        {
                            DecrementSize();
                            Interlocked.Increment(ref _evictions);
                            _metrics?.Record(MetricsNames.CacheEviction, 1);
                        }
                    }
                }
                finally
                {
                    _sizeLock.ExitWriteLock();
                }
            }).ConfigureAwait(false);
        }

        private void EnsureCapacitySync()
        {
            var currentSize = _cache.Count;
            if (currentSize <= _maxSize) return;

            _sizeLock.EnterWriteLock();
            try
            {
                currentSize = _cache.Count;
                if (currentSize <= _maxSize) return;

                var toRemoveCount = (int)(currentSize - _maxSize);
                var snapshot = _cache.ToArray();
                var toRemove = snapshot
                    .OrderBy(e => e.Value.LastAccessSequence)
                    .Take(toRemoveCount)
                    .Select(e => e.Key)
                    .ToList();
                foreach (var key in toRemove)
                {
                    if (_cache.TryRemove(key, out _))
                    {
                        DecrementSize();
                        Interlocked.Increment(ref _evictions);
                        _metrics?.Record(MetricsNames.CacheEviction, 1);
                    }
                }
            }
            finally
            {
                _sizeLock.ExitWriteLock();
            }
        }

        private void CleanupExpired()
        {
            try
            {
                var expiredKeys = _cache
                    .Where(kvp => kvp.Value.IsExpired)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    if (_cache.TryRemove(key, out _))
                    {
                        DecrementSize();
                    }
                }

                if (expiredKeys.Count > 0)
                {
                    _logger.Debug($"Cache cleanup removed {expiredKeys.Count} expired entries");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during cache cleanup");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void IncrementSize() => Interlocked.Increment(ref _currentSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DecrementSize() => Interlocked.Decrement(ref _currentSize);

        public CacheStatistics GetStatistics()
        {
            var totalRequests = _hits + _misses;
            return new CacheStatistics
            {
                Size = _cache.Count,
                MaxSize = _maxSize,
                Hits = _hits,
                Misses = _misses,
                Evictions = _evictions,
                HitRate = totalRequests > 0 ? (double)_hits / totalRequests * 100 : 0,
                MemoryUsage = EstimateMemoryUsage()
            };
        }

        private long EstimateMemoryUsage()
        {
            // Rough estimation:
            // Dictionary overhead + (key size + value size + entry overhead) * count
            const int entryOverhead = 48; // Object header + fields
            const int avgKeySize = 32;
            const int avgValueSize = 1024; // Assuming recommendation objects

            return 1024 + (_currentSize * (avgKeySize + avgValueSize + entryOverhead));
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _sizeLock?.Dispose();
            Clear();
        }

        private class CacheEntry
        {
            public TValue Value { get; }
            public DateTime Expiration { get; }
            public DateTime LastAccess { get; private set; }
            public long LastAccessSequence { get; private set; }

            public bool IsExpired => DateTime.UtcNow > Expiration;

            public CacheEntry(TValue value, TimeSpan expiration)
            {
                Value = value;
                Expiration = DateTime.UtcNow + expiration;
                LastAccess = DateTime.UtcNow;
                LastAccessSequence = 0; // Will be set when added to cache
            }

            public void UpdateLastAccess(long sequence)
            {
                LastAccess = DateTime.UtcNow;
                LastAccessSequence = sequence;
            }

            public void SetSequence(long sequence)
            {
                LastAccessSequence = sequence;
            }
        }

        private class AsyncLazy<T>
        {
            private readonly Lazy<Task<T>> _lazy;

            public AsyncLazy(Func<Task<T>> taskFactory)
            {
                _lazy = new Lazy<Task<T>>(() => Task.Run(taskFactory));
            }

            public Task<T> GetValueAsync(CancellationToken cancellationToken = default)
            {
                return _lazy.Value.WaitAsync(cancellationToken);
            }
        }
    }

    public class CacheStatistics
    {
        public long Size { get; set; }
        public int MaxSize { get; set; }
        public long Hits { get; set; }
        public long Misses { get; set; }
        public long Evictions { get; set; }
        public double HitRate { get; set; }
        public long MemoryUsage { get; set; }

        public override string ToString()
        {
            return $"Cache Stats: Size={Size}/{MaxSize}, HitRate={HitRate:F1}%, " +
                   $"Hits={Hits}, Misses={Misses}, Evictions={Evictions}, " +
                   $"Memory={MemoryUsage / 1024}KB";
        }
    }
}
