using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                var existing = _store.GetAsync(key).GetAwaiter().GetResult();
                if (existing is not null)
                    continue;

                _store.SetAsync(key, new ReviewItem
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
                }).GetAwaiter().GetResult();
            }
            _logger.Info($"Queued {list.Count} items for review");
        }

        public List<ReviewItem> GetPending()
        {
            var all = new List<ReviewItem>();
            var enumTask = _store.EnumerateAsync();
            var enumerator = enumTask.GetAsyncEnumerator();
            try
            {
                while (enumerator.MoveNextAsync().GetAwaiter().GetResult())
                {
                    all.Add(enumerator.Current.Value);
                }
            }
            finally
            {
                enumerator.DisposeAsync().GetAwaiter().GetResult();
            }

            return all
                .Where(i => i.Status == ReviewStatus.Pending)
                .OrderByDescending(i => i.CreatedAt)
                .ToList();
        }

        public List<Recommendation> DequeueAccepted()
        {
            var all = new List<ReviewItem>();
            var enumTask = _store.EnumerateAsync();
            var enumerator = enumTask.GetAsyncEnumerator();
            try
            {
                while (enumerator.MoveNextAsync().GetAwaiter().GetResult())
                {
                    all.Add(enumerator.Current.Value);
                }
            }
            finally
            {
                enumerator.DisposeAsync().GetAwaiter().GetResult();
            }

            var accepted = all.Where(i => i.Status == ReviewStatus.Accepted).ToList();
            var recs = accepted.Select(ToRecommendation).ToList();

            foreach (var item in accepted)
            {
                _store.RemoveAsync(Key(item.Artist, item.Album)).GetAwaiter().GetResult();
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
            var item = _store.GetAsync(key).GetAwaiter().GetResult();
            if (item is null)
                return false;

            item.Status = status;
            item.Notes = notes;
            item.UpdatedAt = DateTime.UtcNow;
            _store.SetAsync(key, item).GetAwaiter().GetResult();
            return true;
        }

        public (int pending, int accepted, int rejected, int never) GetCounts()
        {
            var all = new List<ReviewItem>();
            var enumTask = _store.EnumerateAsync();
            var enumerator = enumTask.GetAsyncEnumerator();
            try
            {
                while (enumerator.MoveNextAsync().GetAwaiter().GetResult())
                {
                    all.Add(enumerator.Current.Value);
                }
            }
            finally
            {
                enumerator.DisposeAsync().GetAwaiter().GetResult();
            }

            return (
                all.Count(i => i.Status == ReviewStatus.Pending),
                all.Count(i => i.Status == ReviewStatus.Accepted),
                all.Count(i => i.Status == ReviewStatus.Rejected),
                all.Count(i => i.Status == ReviewStatus.Never));
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
