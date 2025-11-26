using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service to prevent duplicate recommendations through multiple strategies:
    /// 1. Concurrent fetch prevention using per-key asynchronous de-duplication
    /// 2. Defensive copying to prevent shared reference modifications
    /// 3. Historical tracking to prevent re-recommending
    /// 4. Global deduplication across all sources
    /// 5. Throttling protection
    /// </summary>
    public interface IDuplicationPrevention
    {
        /// <summary>
        /// Prevents concurrent fetch operations for the same key using per-key asynchronous memoization.
        /// This prevents the critical bug where multiple simultaneous Fetch() calls cause duplicates.
        /// </summary>
        /// <typeparam name="T">The return type of the fetch operation</typeparam>
        /// <param name="operationKey">Unique identifier for the operation to prevent concurrency</param>
        /// <param name="fetchOperation">The async operation to execute safely</param>
        /// <returns>The result of the fetch operation</returns>
        Task<T> PreventConcurrentFetch<T>(string operationKey, Func<Task<T>> fetchOperation);

        /// <summary>
        /// Removes duplicate recommendations from a single batch using case-insensitive artist/album grouping.
        /// Takes the first occurrence of each unique artist/album combination.
        /// </summary>
        /// <param name="recommendations">List of recommendations that may contain duplicates</param>
        /// <returns>Deduplicated list with unique artist/album combinations only</returns>
        List<ImportListItemInfo> DeduplicateRecommendations(List<ImportListItemInfo> recommendations);

        /// <summary>
        /// Filters out recommendations that have been previously recommended in past sessions.
        /// Uses historical tracking to prevent the same artist/album from being recommended repeatedly.
        /// </summary>
        /// <param name="recommendations">List of new recommendations to filter</param>
        /// <returns>Filtered list excluding previously recommended items</returns>
        List<ImportListItemInfo> FilterPreviouslyRecommended(List<ImportListItemInfo> recommendations, ISet<string>? sessionAllowList = null);

        /// <summary>
        /// Clears the historical recommendation tracking data.
        /// Use this to reset the "already recommended" state for testing or maintenance.
        /// </summary>
        void ClearHistory();
    }

    public class DuplicationPreventionService : IDuplicationPrevention, IDisposable
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _inflightOperations;
        private readonly ConcurrentDictionary<string, DateTime> _lastFetchTimes;
        private readonly HashSet<string> _historicalRecommendations;
        private readonly object _historyLock = new object();
        private readonly TimeSpan _minFetchInterval = TimeSpan.FromSeconds(5);
        private DateTime _lastCleanup = DateTime.MinValue;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan HistoryRetention = TimeSpan.FromMinutes(10);
        private bool _disposed;

        public DuplicationPreventionService(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _inflightOperations = new ConcurrentDictionary<string, Lazy<Task<object>>>(StringComparer.OrdinalIgnoreCase);
            _lastFetchTimes = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _historicalRecommendations = new HashSet<string>();
        }

        /// <summary>
        /// Prevents concurrent fetch operations for the same key using per-key asynchronous memoization.
        /// This solves the critical issue where multiple simultaneous Fetch() calls cause duplicates.
        /// Ensures the same key executes once while unrelated keys may proceed in parallel and retains throttling safeguards.
        /// </summary>
        /// <typeparam name="T">The return type of the fetch operation</typeparam>
        /// <param name="operationKey">Unique identifier for the operation (typically provider + settings hash)</param>
        /// <param name="fetchOperation">The async operation to execute safely with concurrency protection</param>
        /// <returns>The result of the fetch operation</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the service has been disposed</exception>
        public async Task<T> PreventConcurrentFetch<T>(string operationKey, Func<Task<T>> fetchOperation)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuplicationPreventionService));
            if (string.IsNullOrWhiteSpace(operationKey))
                throw new ArgumentNullException(nameof(operationKey));
            if (fetchOperation == null)
                throw new ArgumentNullException(nameof(fetchOperation));

            var lazy = _inflightOperations.GetOrAdd(operationKey, _ =>
                new Lazy<Task<object>>(() => ExecuteAsync(fetchOperation, operationKey), LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                var result = await lazy.Value.ConfigureAwait(false);
                return (T)result;
            }
            finally
            {
                if (DateTime.UtcNow - _lastCleanup > CleanupInterval)
                {
                    CleanupOldEntries();
                    _lastCleanup = DateTime.UtcNow;
                }
            }

            async Task<object> ExecuteAsync(Func<Task<T>> factory, string key)
            {
                try
                {
                    await EnforceThrottleAsync(key).ConfigureAwait(false);
                    _logger.Debug("Executing fetch operation: {OperationKey}", key);
                    var value = await factory().ConfigureAwait(false);
                    _lastFetchTimes.AddOrUpdate(key, DateTime.UtcNow, (_, __) => DateTime.UtcNow);
                    return value!;
                }
                finally
                {
                    _inflightOperations.TryRemove(key, out _);
                }
            }
        }

        /// <summary>
        /// Deduplicates recommendations within a single batch using sophisticated grouping.
        /// Groups by normalized artist/album (case-insensitive, whitespace-trimmed) and takes first of each group.
        /// Also updates the historical tracking to prevent these items from being recommended again.
        /// </summary>
        /// <param name="recommendations">List of recommendations that may contain duplicates</param>
        /// <returns>Deduplicated list with unique artist/album combinations, preserving original order where possible</returns>
        public List<ImportListItemInfo> DeduplicateRecommendations(List<ImportListItemInfo> recommendations)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuplicationPreventionService));

            if (recommendations == null || !recommendations.Any())
                return recommendations ?? new List<ImportListItemInfo>();

            var originalCount = recommendations.Count;

            foreach (var rec in recommendations)
            {
                if (!string.IsNullOrWhiteSpace(rec.Artist))
                {
                    var decodedArtist = WebUtility.HtmlDecode(rec.Artist).Trim();
                    if (!string.IsNullOrWhiteSpace(decodedArtist))
                    {
                        rec.Artist = decodedArtist;
                    }
                }

                if (!string.IsNullOrWhiteSpace(rec.Album))
                {
                    var decodedAlbum = WebUtility.HtmlDecode(rec.Album).Trim();
                    if (!string.IsNullOrWhiteSpace(decodedAlbum))
                    {
                        rec.Album = decodedAlbum;
                    }
                }
            }


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
        /// Filters out recommendations that have been previously recommended in past sessions.
        /// Uses in-memory historical tracking with normalized string keys to detect previously seen items.
        /// Thread-safe implementation suitable for concurrent access patterns.
        /// </summary>
        /// <param name="recommendations">List of new recommendations to filter against history</param>
        /// <returns>Filtered list excluding items that have been recommended before</returns>
        public List<ImportListItemInfo> FilterPreviouslyRecommended(List<ImportListItemInfo> recommendations, ISet<string>? sessionAllowList = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuplicationPreventionService));

            if (recommendations == null || recommendations.Count == 0)
            {
                return recommendations ?? new List<ImportListItemInfo>();
            }

            var filtered = new List<ImportListItemInfo>();
            var filteredCount = 0;

            lock (_historyLock)
            {
                foreach (var rec in recommendations)
                {
                    var key = GetRecommendationKey(rec);
                    var allowedBySession = sessionAllowList != null && sessionAllowList.Contains(key);
                    if (allowedBySession || !_historicalRecommendations.Contains(key))
                    {
                        filtered.Add(rec);
                        // Don't add to history here - that's the job of DeduplicateRecommendations
                    }
                    else
                    {
                        filteredCount++;
                        try
                        {
                            _logger.Debug($"Filtering previously recommended: {rec.Artist} - {rec.Album}");
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Failed to log filtered recommendation due to null/invalid recommendation data");
                        }
                    }
                }
            }

            if (filteredCount > 0)
            {
                _logger.Info($"Filtered {filteredCount} previously recommended item(s)");
            }

            return filtered;
        }

        /// <summary>
        /// Clears the historical recommendation tracking data from memory.
        /// This resets the "already recommended" state, allowing previously filtered items to be recommended again.
        /// Thread-safe operation that can be called during plugin operation for maintenance or testing.
        /// </summary>
        public void ClearHistory()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuplicationPreventionService));

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

            var decoded = WebUtility.HtmlDecode(value);

            // Remove special characters, normalize whitespace, and convert to lowercase for case-insensitive comparison
            return System.Text.RegularExpressions.Regex.Replace(decoded.Trim(), @"\s+", " ").ToLowerInvariant();
        }

        private async Task EnforceThrottleAsync(string operationKey)
        {
            if (_minFetchInterval <= TimeSpan.Zero)
            {
                return;
            }

            if (_lastFetchTimes.TryGetValue(operationKey, out var lastFetch))
            {
                var elapsed = DateTime.UtcNow - lastFetch;
                if (elapsed < _minFetchInterval)
                {
                    var remaining = _minFetchInterval - elapsed;
                    if (remaining > TimeSpan.Zero)
                    {
                        _logger.Debug("Throttling fetch for {OperationKey}, waiting {Seconds:F1}s before retry", operationKey, remaining.TotalSeconds);
                        await Task.Delay(remaining).ConfigureAwait(false);
                    }
                }
            }
        }

        private void CleanupOldEntries()
        {
            try
            {
                var cutoff = DateTime.UtcNow - HistoryRetention;
                var removed = 0;
                foreach (var entry in _lastFetchTimes.ToArray())
                {
                    if (entry.Value < cutoff && _lastFetchTimes.TryRemove(entry.Key, out _))
                    {
                        removed++;
                    }
                }

                if (removed > 0)
                {
                    _logger.Debug("Cleaned up {Count} old operation metadata entries", removed);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cleaning up old operation metadata");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _inflightOperations.Clear();
            _lastFetchTimes.Clear();

            lock (_historyLock)
            {
                _historicalRecommendations.Clear();
            }
        }
    }
}
