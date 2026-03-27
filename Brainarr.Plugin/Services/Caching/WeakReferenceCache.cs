using System;
using System.Collections.Concurrent;
using System.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Caching
{
    /// <summary>
    /// Weak reference cache for GC-recoverable items.
    /// Used by <see cref="EnhancedRecommendationCache"/> as a secondary tier.
    /// </summary>
    public class WeakReferenceCache<TKey, TValue> where TKey : notnull where TValue : class
    {
        private readonly ConcurrentDictionary<TKey, WeakReference> _cache = new();

        // Note: O(n) — enumerates all entries to count only live references
        public int Count => _cache.Count(kvp => kvp.Value.IsAlive);

        public bool TryGet(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var weakRef) && weakRef.IsAlive)
            {
                value = weakRef.Target as TValue;
                return value != null;
            }

            value = null;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            _cache[key] = new WeakReference(value);
        }

        public void Remove(TKey key)
        {
            _cache.TryRemove(key, out _);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public void Compact()
        {
            // Remove dead references
            var deadKeys = _cache.Where(kvp => !kvp.Value.IsAlive).Select(kvp => kvp.Key).ToList();
            foreach (var key in deadKeys)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }
}
