using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    public class ReviewQueueService
    {
        private readonly Logger _logger;
        private readonly string _queuePath;
        private readonly object _lock = new object();
        private QueueData _data;

        public ReviewQueueService(Logger logger, string dataPath = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _queuePath = Path.Combine(
                dataPath ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Brainarr",
                "review_queue.json");
            Load();
        }

        public void Enqueue(IEnumerable<Recommendation> items, string reason = null)
        {
            if (items == null) return;
            lock (_lock)
            {
                foreach (var r in items)
                {
                    var key = Key(r.Artist, r.Album);
                    if (_data.Items.ContainsKey(key)) continue;
                    _data.Items[key] = new ReviewItem
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
                        Notes = reason
                    };
                }
                Save();
                _logger.Info($"Queued {items.Count()} items for review");
            }
        }

        public List<ReviewItem> GetPending()
        {
            lock (_lock)
            {
                return _data.Items.Values.Where(i => i.Status == ReviewStatus.Pending)
                    .OrderByDescending(i => i.CreatedAt)
                    .ToList();
            }
        }

        public List<Recommendation> DequeueAccepted()
        {
            lock (_lock)
            {
                var accepted = _data.Items.Values.Where(i => i.Status == ReviewStatus.Accepted).ToList();
                var recs = accepted.Select(ToRecommendation).ToList();
                foreach (var item in accepted)
                {
                    _data.Items.Remove(Key(item.Artist, item.Album));
                }
                if (accepted.Count > 0) Save();
                return recs;
            }
        }

        public bool SetStatus(string artist, string album, ReviewStatus status, string notes = null)
        {
            lock (_lock)
            {
                var key = Key(artist, album);
                if (!_data.Items.TryGetValue(key, out var item)) return false;
                item.Status = status;
                item.Notes = notes;
                item.UpdatedAt = DateTime.UtcNow;
                Save();
                return true;
            }
        }

        public (int pending, int accepted, int rejected, int never) GetCounts()
        {
            lock (_lock)
            {
                return (
                    _data.Items.Values.Count(i => i.Status == ReviewStatus.Pending),
                    _data.Items.Values.Count(i => i.Status == ReviewStatus.Accepted),
                    _data.Items.Values.Count(i => i.Status == ReviewStatus.Rejected),
                    _data.Items.Values.Count(i => i.Status == ReviewStatus.Never)
                );
            }
        }

        public void Clear(ReviewStatus? status = null)
        {
            lock (_lock)
            {
                if (status == null)
                {
                    _data = new QueueData();
                }
                else
                {
                    var keys = _data.Items.Where(k => k.Value.Status == status.Value).Select(k => k.Key).ToList();
                    foreach (var key in keys) _data.Items.Remove(key);
                }
                Save();
            }
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
                MusicBrainzId = i.AlbumMusicBrainzId
            };
        }

        private void Load()
        {
            try
            {
                var dir = Path.GetDirectoryName(_queuePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(_queuePath))
                {
                    var json = File.ReadAllText(_queuePath);
                    _data = JsonSerializer.Deserialize<QueueData>(json) ?? new QueueData();
                }
                else
                {
                    _data = new QueueData();
                    Save();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to load review queue; starting empty");
                _data = new QueueData();
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_queuePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_queuePath, json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save review queue");
            }
        }

        private static string Key(string artist, string album) => ($"{artist}|{album}").ToLowerInvariant();

        private class QueueData
        {
            public Dictionary<string, ReviewItem> Items { get; set; } = new();
        }

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
            Never = 3
        }
    }
}

