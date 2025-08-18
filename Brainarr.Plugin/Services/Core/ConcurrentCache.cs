using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Brainarr.Plugin.Services.Core
{
    /// <summary>
    /// High-performance thread-safe cache with memory-efficient storage
    /// </summary>
    public class ConcurrentCache<TKey, TValue> : IDisposable
    {
        private readonly ConcurrentDictionary<TKey, CacheEntry> _cache;
        private readonly int _maxSize;
        private readonly TimeSpan _defaultExpiration;
        private readonly ILogger _logger;
        private readonly Timer _cleanupTimer;
        private readonly ReaderWriterLockSlim _sizeLock;
        private long _currentSize;
        private long _hits;
        private long _misses;
        private long _evictions;

        public ConcurrentCache(
            int maxSize = 1000,
            TimeSpan? defaultExpiration = null,
            ILogger logger = null)
        {
            _maxSize = maxSize > 0 ? maxSize : throw new ArgumentException("Max size must be positive", nameof(maxSize));
            _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(30);
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _cache = new ConcurrentDictionary<TKey, CacheEntry>();
            _sizeLock = new ReaderWriterLockSlim();
            
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
                    entry.UpdateLastAccess();
                    return entry.Value;
                }
                
                // Remove expired entry
                _cache.TryRemove(key, out _);
                DecrementSize();
            }

            Interlocked.Increment(ref _misses);

            // Slow path: Create new entry
            // Use async lazy to prevent cache stampede
            var lazy = new AsyncLazy<TValue>(() => factory(key));
            var newEntry = new CacheEntry(
                await lazy.GetValueAsync(cancellationToken).ConfigureAwait(false),
                expiration ?? _defaultExpiration);

            // Ensure we don't exceed max size
            await EnsureCapacityAsync().ConfigureAwait(false);

            var addedEntry = _cache.AddOrUpdate(key, newEntry, (k, existing) =>
            {
                if (!existing.IsExpired)
                {
                    // Another thread added valid entry
                    return existing;
                }
                return newEntry;
            });

            if (ReferenceEquals(addedEntry, newEntry))
            {
                IncrementSize();
            }

            return addedEntry.Value;
        }

        public bool TryGet(TKey key, out TValue value)
        {
            value = default;
            
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                Interlocked.Increment(ref _hits);
                entry.UpdateLastAccess();
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
            
            _cache.AddOrUpdate(key, k =>
            {
                IncrementSize();
                return entry;
            }, (k, existing) =>
            {
                // Only increment if we're actually adding new entry
                if (existing == null)
                    IncrementSize();
                return entry;
            });

            // Don't wait for async cleanup
            _ = EnsureCapacityAsync();
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
            if (Interlocked.Read(ref _currentSize) <= _maxSize)
                return;

            await Task.Run(() =>
            {
                _sizeLock.EnterWriteLock();
                try
                {
                    if (_currentSize <= _maxSize)
                        return;

                    // Remove least recently used entries
                    var toRemove = _cache
                        .OrderBy(kvp => kvp.Value.LastAccess)
                        .Take((int)(_currentSize - _maxSize + _maxSize / 10)) // Remove 10% more for efficiency
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in toRemove)
                    {
                        if (_cache.TryRemove(key, out _))
                        {
                            DecrementSize();
                            Interlocked.Increment(ref _evictions);
                        }
                    }

                    _logger.Debug($"Cache evicted {toRemove.Count} entries (size: {_currentSize}/{_maxSize})");
                }
                finally
                {
                    _sizeLock.ExitWriteLock();
                }
            }).ConfigureAwait(false);
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
                Size = Interlocked.Read(ref _currentSize),
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
            
            public bool IsExpired => DateTime.UtcNow > Expiration;

            public CacheEntry(TValue value, TimeSpan expiration)
            {
                Value = value;
                Expiration = DateTime.UtcNow + expiration;
                LastAccess = DateTime.UtcNow;
            }

            public void UpdateLastAccess()
            {
                LastAccess = DateTime.UtcNow;
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