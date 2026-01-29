using System;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Registry
{
    /// <summary>
    /// Abstraction for ModelRegistryLoader's shared cache to enable test isolation.
    /// Production uses a singleton with cross-instance sharing; tests inject isolated instances.
    /// </summary>
    public interface IModelRegistryCache
    {
        /// <summary>
        /// Attempts to retrieve a cached registry result.
        /// </summary>
        /// <param name="key">Cache key (typically cacheFilePath::registryUrl)</param>
        /// <param name="result">The cached result if found and not expired</param>
        /// <param name="ttl">Time-to-live for cache entries</param>
        /// <returns>True if a valid (non-expired) entry was found</returns>
        bool TryGet(string key, TimeSpan ttl, out ModelRegistryLoadResult? result);

        /// <summary>
        /// Stores a registry result in the cache.
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="result">The result to cache</param>
        void Set(string key, ModelRegistryLoadResult result);

        /// <summary>
        /// Invalidates a specific cache entry.
        /// </summary>
        /// <param name="key">Cache key to invalidate</param>
        void Invalidate(string key);

        /// <summary>
        /// Invalidates all cache entries, or those matching a pattern.
        /// </summary>
        /// <param name="keyPattern">Optional pattern to match (null clears all)</param>
        void InvalidateAll(string? keyPattern = null);

        /// <summary>
        /// Acquires an exclusive lock for a cache key to prevent concurrent loads.
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="action">Action to execute while holding the lock</param>
        /// <returns>Result from the action</returns>
        System.Threading.Tasks.Task<T> WithLockAsync<T>(string key, Func<System.Threading.Tasks.Task<T>> action, System.Threading.CancellationToken cancellationToken = default);
    }
}
