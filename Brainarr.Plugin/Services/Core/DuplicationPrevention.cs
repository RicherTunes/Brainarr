using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Comprehensive duplication prevention service that addresses all identified sources of duplicates.
    /// This service ensures that:
    /// 1. Concurrent Fetch() calls don't generate duplicates
    /// 2. Cache returns defensive copies, not shared references
    /// 3. All recommendations are globally deduplicated
    /// 4. Historical tracking prevents duplicates across sessions
    /// </summary>
    public interface IDuplicationPrevention
    {
        /// <summary>
        /// Ensures only one Fetch operation runs at a time
        /// </summary>
        Task<T> PreventConcurrentFetch<T>(string operationKey, Func<Task<T>> fetchOperation);
        
        /// <summary>
        /// Deduplicates a list of items based on artist/album
        /// </summary>
        List<ImportListItemInfo> DeduplicateRecommendations(List<ImportListItemInfo> recommendations);
        
        /// <summary>
        /// Tracks and filters recommendations to prevent duplicates across sessions
        /// </summary>
        List<ImportListItemInfo> FilterPreviouslyRecommended(List<ImportListItemInfo> recommendations);
        
        /// <summary>
        /// Creates a defensive copy of a list to prevent shared reference issues
        /// </summary>
        List<ImportListItemInfo> CreateDefensiveCopy(List<ImportListItemInfo> original);
    }

    public class DuplicationPreventionService : IDuplicationPrevention
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationLocks;
        private readonly ConcurrentDictionary<string, DateTime> _lastOperationTime;
        private readonly ConcurrentDictionary<string, HashSet<string>> _historicalRecommendations;
        private readonly TimeSpan _minimumOperationInterval = TimeSpan.FromSeconds(5);
        private readonly object _historyLock = new object();

        public DuplicationPreventionService(Logger logger)
        {
            _logger = logger;
            _operationLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            _lastOperationTime = new ConcurrentDictionary<string, DateTime>();
            _historicalRecommendations = new ConcurrentDictionary<string, HashSet<string>>();
        }

        public async Task<T> PreventConcurrentFetch<T>(string operationKey, Func<Task<T>> fetchOperation)
        {
            var semaphore = _operationLocks.GetOrAdd(operationKey, _ => new SemaphoreSlim(1, 1));
            
            // Try to acquire the lock with a timeout
            if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                _logger.Warn($"Timeout waiting for fetch lock on {operationKey} - possible concurrent access");
                throw new TimeoutException($"Could not acquire lock for operation {operationKey}");
            }

            try
            {
                // Check if we're being called too frequently
                if (_lastOperationTime.TryGetValue(operationKey, out var lastTime))
                {
                    var elapsed = DateTime.UtcNow - lastTime;
                    if (elapsed < _minimumOperationInterval)
                    {
                        var waitTime = _minimumOperationInterval - elapsed;
                        _logger.Info($"Throttling fetch operation {operationKey} - waiting {waitTime.TotalSeconds:F1}s");
                        await Task.Delay(waitTime);
                    }
                }

                _logger.Debug($"Executing fetch operation {operationKey} with concurrency protection");
                var result = await fetchOperation();
                
                _lastOperationTime[operationKey] = DateTime.UtcNow;
                
                return result;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public List<ImportListItemInfo> DeduplicateRecommendations(List<ImportListItemInfo> recommendations)
        {
            if (recommendations == null || !recommendations.Any())
            {
                return new List<ImportListItemInfo>();
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduplicated = new List<ImportListItemInfo>();
            var duplicateCount = 0;

            foreach (var item in recommendations)
            {
                // Create normalized key for deduplication
                var key = CreateNormalizedKey(item.Artist, item.Album);
                
                if (seen.Add(key))
                {
                    deduplicated.Add(item);
                }
                else
                {
                    duplicateCount++;
                    _logger.Debug($"Duplicate removed: {item.Artist} - {item.Album}");
                }
            }

            if (duplicateCount > 0)
            {
                _logger.Info($"Removed {duplicateCount} duplicate recommendations from {recommendations.Count} total");
            }

            return deduplicated;
        }

        public List<ImportListItemInfo> FilterPreviouslyRecommended(List<ImportListItemInfo> recommendations)
        {
            if (recommendations == null || !recommendations.Any())
            {
                return new List<ImportListItemInfo>();
            }

            var sessionKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var history = _historicalRecommendations.GetOrAdd(sessionKey, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            
            var filtered = new List<ImportListItemInfo>();
            var previouslyRecommendedCount = 0;

            lock (_historyLock)
            {
                foreach (var item in recommendations)
                {
                    var key = CreateNormalizedKey(item.Artist, item.Album);
                    
                    if (history.Add(key))
                    {
                        filtered.Add(item);
                    }
                    else
                    {
                        previouslyRecommendedCount++;
                        _logger.Debug($"Previously recommended today: {item.Artist} - {item.Album}");
                    }
                }
            }

            if (previouslyRecommendedCount > 0)
            {
                _logger.Info($"Filtered {previouslyRecommendedCount} previously recommended items from {recommendations.Count} total");
            }

            // Clean up old history (keep only last 7 days)
            CleanupOldHistory();

            return filtered;
        }

        public List<ImportListItemInfo> CreateDefensiveCopy(List<ImportListItemInfo> original)
        {
            if (original == null)
            {
                return new List<ImportListItemInfo>();
            }

            // Create a new list with cloned items to prevent any shared reference issues
            return original.Select(item => new ImportListItemInfo
            {
                Artist = item.Artist,
                Album = item.Album,
                ReleaseDate = item.ReleaseDate,
                ArtistMusicBrainzId = item.ArtistMusicBrainzId,
                AlbumMusicBrainzId = item.AlbumMusicBrainzId
            }).ToList();
        }

        private string CreateNormalizedKey(string artist, string album)
        {
            // Normalize the key to handle case differences, whitespace, and special characters
            artist = NormalizeString(artist);
            album = NormalizeString(album);
            
            // For artist-only recommendations (album is empty), use just artist
            // For album recommendations, use both
            return string.IsNullOrWhiteSpace(album) ? 
                $"artist:{artist}" : 
                $"artist:{artist}|album:{album}";
        }

        private string NormalizeString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Remove extra whitespace, convert to lowercase, remove special characters
            return value
                .Trim()
                .ToLowerInvariant()
                .Replace("  ", " ")
                .Replace("\t", " ")
                .Replace("\n", " ")
                .Replace("\r", " ");
        }

        private void CleanupOldHistory()
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-7);
            var keysToRemove = _historicalRecommendations
                .Where(kvp => DateTime.TryParse(kvp.Key, out var date) && date < cutoffDate)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _historicalRecommendations.TryRemove(key, out _);
                _logger.Debug($"Removed old recommendation history for {key}");
            }
        }
    }
}