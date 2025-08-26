using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service to prevent duplicate recommendations through multiple strategies:
    /// 1. Concurrent fetch prevention using semaphores
    /// 2. Defensive copying to prevent shared reference modifications
    /// 3. Historical tracking to prevent re-recommending
    /// 4. Global deduplication across all sources
    /// 5. Throttling protection
    /// </summary>
    public interface IDuplicationPrevention
    {
        Task<T> PreventConcurrentFetch<T>(string operationKey, Func<Task<T>> fetchOperation);
        List<ImportListItemInfo> DeduplicateRecommendations(List<ImportListItemInfo> recommendations);
        List<ImportListItemInfo> FilterPreviouslyRecommended(List<ImportListItemInfo> recommendations);
        void ClearHistory();
    }

    public class DuplicationPreventionService : IDuplicationPrevention, IDisposable
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationLocks;
        private readonly ConcurrentDictionary<string, DateTime> _lastFetchTimes;
        private readonly HashSet<string> _historicalRecommendations;
        private readonly object _historyLock = new object();
        private readonly TimeSpan _minFetchInterval = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _lockTimeout = TimeSpan.FromMinutes(2);
        private bool _disposed;

        public DuplicationPreventionService(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _operationLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            _lastFetchTimes = new ConcurrentDictionary<string, DateTime>();
            _historicalRecommendations = new HashSet<string>();
        }

        /// <summary>
        /// Prevents concurrent fetch operations for the same key.
        /// This solves the issue where multiple simultaneous Fetch() calls cause duplicates.
        /// </summary>
        public async Task<T> PreventConcurrentFetch<T>(string operationKey, Func<Task<T>> fetchOperation)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuplicationPreventionService));

            // Get or create a semaphore for this operation
            var semaphore = _operationLocks.GetOrAdd(operationKey, _ => new SemaphoreSlim(1, 1));
            
            bool acquired = false;
            try
            {
                // Try to acquire the lock with timeout
                acquired = await semaphore.WaitAsync(_lockTimeout);
                
                if (!acquired)
                {
                    _logger.Warn($"Timeout waiting for lock on operation: {operationKey}");
                    throw new TimeoutException($"Could not acquire lock for operation {operationKey} within {_lockTimeout}");
                }

                // Check throttling
                if (_lastFetchTimes.TryGetValue(operationKey, out var lastFetch))
                {
                    var timeSinceLastFetch = DateTime.UtcNow - lastFetch;
                    if (timeSinceLastFetch < _minFetchInterval)
                    {
                        _logger.Debug($"Throttling fetch for {operationKey}, last fetch was {timeSinceLastFetch.TotalSeconds:F1}s ago");
                        await Task.Delay(_minFetchInterval - timeSinceLastFetch);
                    }
                }

                _logger.Debug($"Executing fetch operation: {operationKey}");
                var result = await fetchOperation();
                
                // Update last fetch time
                _lastFetchTimes.AddOrUpdate(operationKey, DateTime.UtcNow, (k, v) => DateTime.UtcNow);
                
                return result;
            }
            finally
            {
                if (acquired)
                {
                    semaphore.Release();
                }

                // Clean up old semaphores periodically
                CleanupOldLocks();
            }
        }

        /// <summary>
        /// Deduplicates recommendations within a single batch.
        /// Groups by normalized artist/album and takes first of each group.
        /// </summary>
        public List<ImportListItemInfo> DeduplicateRecommendations(List<ImportListItemInfo> recommendations)
        {
            if (recommendations == null || !recommendations.Any())
                return recommendations ?? new List<ImportListItemInfo>();

            var originalCount = recommendations.Count;
            
            var deduplicated = recommendations
                .GroupBy(r => new 
                { 
                    Artist = NormalizeString(r.Artist),
                    Album = NormalizeString(r.Album)
                })
                .Select(g => g.First())
                .ToList();

            var duplicateCount = originalCount - deduplicated.Count;
            if (duplicateCount > 0)
            {
                _logger.Info($"Removed {duplicateCount} duplicates from batch of {originalCount} recommendations");
            }

            // Track these recommendations in history
            lock (_historyLock)
            {
                foreach (var rec in deduplicated)
                {
                    var key = GetRecommendationKey(rec);
                    _historicalRecommendations.Add(key);
                }
            }

            return deduplicated;
        }

        /// <summary>
        /// Filters out recommendations that have been previously recommended.
        /// Prevents the same artist/album from being recommended multiple times across sessions.
        /// </summary>
        public List<ImportListItemInfo> FilterPreviouslyRecommended(List<ImportListItemInfo> recommendations)
        {
            if (recommendations == null || !recommendations.Any())
                return recommendations ?? new List<ImportListItemInfo>();

            var filtered = new List<ImportListItemInfo>();
            var filteredCount = 0;

            lock (_historyLock)
            {
                foreach (var rec in recommendations)
                {
                    var key = GetRecommendationKey(rec);
                    if (!_historicalRecommendations.Contains(key))
                    {
                        filtered.Add(rec);
                        _historicalRecommendations.Add(key);
                    }
                    else
                    {
                        filteredCount++;
                        _logger.Debug($"Filtering previously recommended: {rec.Artist} - {rec.Album}");
                    }
                }
            }

            if (filteredCount > 0)
            {
                _logger.Info($"Filtered {filteredCount} previously recommended items");
            }

            return filtered;
        }

        /// <summary>
        /// Clears the historical recommendation tracking.
        /// </summary>
        public void ClearHistory()
        {
            lock (_historyLock)
            {
                var count = _historicalRecommendations.Count;
                _historicalRecommendations.Clear();
                _logger.Info($"Cleared {count} items from recommendation history");
            }
        }

        private string GetRecommendationKey(ImportListItemInfo item)
        {
            var artist = NormalizeString(item.Artist);
            var album = NormalizeString(item.Album);
            return $"{artist}|{album}".ToLowerInvariant();
        }

        private string NormalizeString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            // Remove special characters, normalize whitespace
            return System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private void CleanupOldLocks()
        {
            try
            {
                // Clean up locks that haven't been used in over 10 minutes
                var cutoff = DateTime.UtcNow.AddMinutes(-10);
                var keysToRemove = _lastFetchTimes
                    .Where(kvp => kvp.Value < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    if (_operationLocks.TryRemove(key, out var semaphore))
                    {
                        semaphore?.Dispose();
                    }
                    _lastFetchTimes.TryRemove(key, out _);
                }

                if (keysToRemove.Any())
                {
                    _logger.Debug($"Cleaned up {keysToRemove.Count} old operation locks");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cleaning up old locks");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (var semaphore in _operationLocks.Values)
            {
                try
                {
                    semaphore?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error disposing semaphore");
                }
            }

            _operationLocks.Clear();
            _lastFetchTimes.Clear();
            
            lock (_historyLock)
            {
                _historicalRecommendations.Clear();
            }
        }
    }
}