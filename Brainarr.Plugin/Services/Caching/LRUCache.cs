using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Caching
{
    /// <summary>
    /// LRU (Least Recently Used) cache implementation.
    /// </summary>
    public class LRUCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<LRUCacheItem>> _cache;
        private readonly LinkedList<LRUCacheItem> _lruList;
        private readonly ReaderWriterLockSlim _lock;

        public LRUCache(int capacity)
        {
            _capacity = capacity;
            _cache = new Dictionary<TKey, LinkedListNode<LRUCacheItem>>(capacity);
            _lruList = new LinkedList<LRUCacheItem>();
            _lock = new ReaderWriterLockSlim();
        }

        public int Count => _cache.Count;

        public bool TryGet(TKey key, out TValue value)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        // Move to front (most recently used)
                        _lruList.Remove(node);
                        _lruList.AddFirst(node);
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }

                    value = node.Value.Value;
                    return true;
                }

                value = default;
                return false;
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }

        public void Set(TKey key, TValue value)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_cache.TryGetValue(key, out var existingNode))
                {
                    // Update existing
                    _lruList.Remove(existingNode);
                    existingNode.Value.Value = value;
                    _lruList.AddFirst(existingNode);
                }
                else
                {
                    // Add new
                    if (_cache.Count >= _capacity)
                    {
                        // Evict least recently used
                        var lru = _lruList.Last;
                        _cache.Remove(lru.Value.Key);
                        _lruList.RemoveLast();
                    }

                    var cacheItem = new LRUCacheItem { Key = key, Value = value };
                    var node = _lruList.AddFirst(cacheItem);
                    _cache[key] = node;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Remove(TKey key)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _cache.Remove(key);
                    _lruList.Remove(node);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _cache.Clear();
                _lruList.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IEnumerable<TKey> GetExpiredKeys()
        {
            _lock.EnterReadLock();
            try
            {
                // This would check for expired entries based on TTL
                // For now, return empty as expiration is handled in CacheEntry
                return Enumerable.Empty<TKey>();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public List<KeyValuePair<TKey, int>> GetTopKeys(int count)
        {
            _lock.EnterReadLock();
            try
            {
                return _lruList
                    .Take(count)
                    .Select((item, index) => new KeyValuePair<TKey, int>(item.Key, _lruList.Count - index))
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public int Sum(Func<KeyValuePair<TKey, TValue>, int> selector)
        {
            _lock.EnterReadLock();
            try
            {
                return _cache.Sum(kvp => selector(new KeyValuePair<TKey, TValue>(kvp.Key, kvp.Value.Value.Value)));
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private class LRUCacheItem
        {
            public TKey Key { get; set; }
            public TValue Value { get; set; }
        }
    }
}
