using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Hosting;
using Lidarr.Plugin.Common.Services.Storage;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// File-backed review queue backed by Common's <see cref="JsonFileStore{TKey,TValue}"/>.
    /// Keys are <c>"&lt;artist&gt;|&lt;album&gt;"</c> lowercased; each entry is a <see cref="ReviewItem"/>.
    /// Atomic-write and crash-safe semantics are now provided by the store (temp-file + rename).
    /// Public API is unchanged from the hand-rolled predecessor.
    /// </summary>
    /// <remarks>
    /// Sync bridge strategy (Wave 16C, Path 2):
    /// All public methods are synchronous because callers are deeply sync (Lidarr UI
    /// action handlers, DI-resolved services with no async chain).  Direct
    /// <c>.GetAwaiter().GetResult()</c> on <see cref="JsonFileStore{TKey,TValue}"/>'s
    /// <see cref="System.Threading.SemaphoreSlim"/>-guarded async methods risks deadlock
    /// under a captured <see cref="System.Threading.SynchronizationContext"/>.
    /// <para>
    /// Fix: Each call site wraps the async store method in
    /// <c>Task.Run(async () => ...).GetAwaiter().GetResult()</c>.  <c>Task.Run</c> dispatches
    /// the work to a thread-pool thread that has no captured context, so
    /// <c>ConfigureAwait(false)</c> inside the store never has to fight the original context.
    /// This eliminates the deadlock risk without requiring async propagation up the call chain.
    /// </para>
    /// </remarks>
    public class ReviewQueueService
    {
        private readonly Logger _logger;
        private readonly JsonFileStore<string, ReviewItem> _store;

        public ReviewQueueService(Logger logger, string dataPath = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            var root = dataPath ?? PluginConfigRoots.Resolve("Brainarr");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "review_queue.json");
            _store = new JsonFileStore<string, ReviewItem>(
                path,
                new JsonFileStoreOptions<string>
                {
                    KeyNormalizer = static s => (s ?? string.Empty).ToLowerInvariant(),
                    KeyComparer = StringComparer.OrdinalIgnoreCase,
                });
        }

        public void Enqueue(IEnumerable<Recommendation> items, string reason = null)
        {
            if (items == null) return;
            var list = items.ToList();
            foreach (var r in list)
            {
                var key = Key(r.Artist, r.Album);
                // Only insert; do not overwrite existing items.
                // Task.Run decouples from caller's sync context — avoids SemaphoreSlim deadlock.
                var existing = Task.Run(async () => await _store.GetAsync(key).ConfigureAwait(false)).GetAwaiter().GetResult();
                if (existing is not null)
                    continue;

                Task.Run(async () => await _store.SetAsync(key, new ReviewItem
                {
                    Artist = r.Artist,
                    Album = r.Album,
                    Genre = r.Genre,
                    Confidence = r.Confidence,
                    Reason = r.Reason,
                    Year = r.Year ?? r.ReleaseYear,
                    ArtistMusicBrainzId = r.ArtistMusicBrainzId,
                    AlbumMusicBrainzId = r.AlbumMusicBrainzId,
                    Status = ReviewStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    Notes = reason,
                }).ConfigureAwait(false)).GetAwaiter().GetResult();
            }
            _logger.Info($"Queued {list.Count} items for review");
        }

        public List<ReviewItem> GetPending()
        {
            var all = EnumerateAll();
            return all
                .Where(i => i.Status == ReviewStatus.Pending)
                .OrderByDescending(i => i.CreatedAt)
                .ToList();
        }

        public List<Recommendation> DequeueAccepted()
        {
            var all = EnumerateAll();
            var accepted = all.Where(i => i.Status == ReviewStatus.Accepted).ToList();
            var recs = accepted.Select(ToRecommendation).ToList();

            foreach (var item in accepted)
            {
                // Task.Run decouples from caller's sync context.
                Task.Run(async () => await _store.RemoveAsync(Key(item.Artist, item.Album)).ConfigureAwait(false))
                    .GetAwaiter().GetResult();
            }

            if (accepted.Count > 0)
            {
                _logger.Info($"Released {accepted.Count} accepted items from review queue");
            }

            return recs;
        }

        public bool SetStatus(string artist, string album, ReviewStatus status, string notes = null)
        {
            var key = Key(artist, album);
            // Task.Run decouples from caller's sync context.
            var item = Task.Run(async () => await _store.GetAsync(key).ConfigureAwait(false)).GetAwaiter().GetResult();
            if (item is null)
                return false;

            item.Status = status;
            item.Notes = notes;
            item.UpdatedAt = DateTime.UtcNow;
            Task.Run(async () => await _store.SetAsync(key, item).ConfigureAwait(false)).GetAwaiter().GetResult();
            return true;
        }

        public (int pending, int accepted, int rejected, int never) GetCounts()
        {
            var all = EnumerateAll();
            return (
                all.Count(i => i.Status == ReviewStatus.Pending),
                all.Count(i => i.Status == ReviewStatus.Accepted),
                all.Count(i => i.Status == ReviewStatus.Rejected),
                all.Count(i => i.Status == ReviewStatus.Never));
        }

        /// <summary>
        /// Reads all items from the store.
        /// Uses <c>Task.Run</c> to avoid deadlock when called from a sync context
        /// that may have a captured <see cref="System.Threading.SynchronizationContext"/>.
        /// </summary>
        private List<ReviewItem> EnumerateAll()
        {
            return Task.Run(async () =>
            {
                var all = new List<ReviewItem>();
                var enumerator = _store.EnumerateAsync().GetAsyncEnumerator();
                try
                {
                    while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        all.Add(enumerator.Current.Value);
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
                return all;
            }).GetAwaiter().GetResult();
        }

        private static Recommendation ToRecommendation(ReviewItem i)
        {
            return new Recommendation
            {
                Artist = i.Artist,
                Album = i.Album,
                Genre = i.Genre,
                Confidence = i.Confidence,
                Reason = i.Reason,
                Year = i.Year,
                ArtistMusicBrainzId = i.ArtistMusicBrainzId,
                AlbumMusicBrainzId = i.AlbumMusicBrainzId,
                MusicBrainzId = i.AlbumMusicBrainzId,
            };
        }

        private static string Key(string artist, string album) => ($"{artist}|{album}").ToLowerInvariant();

        public class ReviewItem
        {
            public string Artist { get; set; }
            public string Album { get; set; }
            public string Genre { get; set; }
            public double Confidence { get; set; }
            public string Reason { get; set; }
            public int? Year { get; set; }
            public string ArtistMusicBrainzId { get; set; }
            public string AlbumMusicBrainzId { get; set; }
            public ReviewStatus Status { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public string Notes { get; set; }
        }

        public enum ReviewStatus
        {
            Pending = 0,
            Accepted = 1,
            Rejected = 2,
            Never = 3,
        }
    }
}
