using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Handles review queue UI actions extracted from BrainarrOrchestrator.
    /// Manages review accept/reject/never workflows, approval application,
    /// and styles option retrieval.
    /// </summary>
    internal sealed class ReviewQueueActionHandler
    {
        private readonly ReviewQueueService _reviewQueue;
        private readonly RecommendationHistory _history;
        private readonly IStyleCatalogService _styleCatalog;
        private readonly Action _persistSettingsCallback;
        private readonly Logger _logger;

        public ReviewQueueActionHandler(
            ReviewQueueService reviewQueue,
            RecommendationHistory history,
            IStyleCatalogService styleCatalog,
            Action persistSettingsCallback,
            Logger logger)
        {
            _reviewQueue = reviewQueue ?? throw new ArgumentNullException(nameof(reviewQueue));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _styleCatalog = styleCatalog ?? throw new ArgumentNullException(nameof(styleCatalog));
            _persistSettingsCallback = persistSettingsCallback;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public object GetStylesOptions(IDictionary<string, string> query)
        {
            try
            {
                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var q = Get(query, "query") ?? string.Empty;
                var items = _styleCatalog.Search(q, 50)
                    .Select(s => new { value = s.Slug, name = s.Name })
                    .ToList();
                return new { options = items };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "styles/getoptions failed");
                return new { options = Array.Empty<object>() };
            }
        }

        public object HandleReviewUpdate(IDictionary<string, string> query, ReviewQueueService.ReviewStatus status)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            var notes = query.TryGetValue("notes", out var n) ? n : null;
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album))
            {
                return new { ok = false, error = "artist and album are required" };
            }

            var ok = _reviewQueue.SetStatus(artist, album, status, notes);
            if (ok && status == ReviewQueueService.ReviewStatus.Rejected)
            {
                // Record rejection in history (soft negative feedback)
                _history.MarkAsRejected(artist, album, reason: notes);
            }
            return new { ok };
        }

        public object HandleReviewNever(IDictionary<string, string> query)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            var notes = query.TryGetValue("notes", out var n) ? n : null;
            if (string.IsNullOrWhiteSpace(artist))
            {
                return new { ok = false, error = "artist is required" };
            }
            var ok = _reviewQueue.SetStatus(artist, album, ReviewQueueService.ReviewStatus.Never, notes);
            // Strong negative constraint
            _history.MarkAsDisliked(artist, album, RecommendationHistory.DislikeLevel.NeverAgain);
            return new { ok };
        }

        public object GetReviewOptions()
        {
            var items = _reviewQueue.GetPending();
            var options = items
                .Select(i => new
                {
                    value = $"{i.Artist}|{i.Album}",
                    name = string.IsNullOrWhiteSpace(i.Album)
                        ? i.Artist
                        : $"{i.Artist} — {i.Album}{(i.Year.HasValue ? " (" + i.Year.Value + ")" : string.Empty)}"
                })
                .OrderBy(o => o.name, StringComparer.InvariantCultureIgnoreCase)
                .ToList();
            return new { options };
        }

        public object GetReviewSummaryOptions()
        {
            var (pending, accepted, rejected, never) = _reviewQueue.GetCounts();
            var options = new List<object>
            {
                new { value = $"pending:{pending}", name = $"Pending: {pending}" },
                new { value = $"accepted:{accepted}", name = $"Accepted (released): {accepted}" },
                new { value = $"rejected:{rejected}", name = $"Rejected: {rejected}" },
                new { value = $"never:{never}", name = $"Never Again: {never}" }
            };
            return new { options };
        }

        public object ApplyApprovalsNow(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var keysCsv = query.TryGetValue("keys", out var k) ? k : null;
            var keys = new List<string>();
            if (!string.IsNullOrWhiteSpace(keysCsv))
            {
                keys.AddRange(keysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (settings.ReviewApproveKeys != null)
            {
                keys.AddRange(settings.ReviewApproveKeys);
            }

            int applied = 0;
            foreach (var key in keys)
            {
                var parts = (key ?? "").Split('|');
                if (parts.Length >= 2)
                {
                    if (_reviewQueue.SetStatus(parts[0], parts[1], ReviewQueueService.ReviewStatus.Accepted))
                    {
                        applied++;
                    }
                }
            }

            var accepted = _reviewQueue.DequeueAccepted();
            // Clear approval selections in memory (persist by saving settings from UI)
            settings.ReviewApproveKeys = Array.Empty<string>();

            // Attempt to persist the cleared selections, if a persistence callback was provided
            TryPersistSettings();

            return new
            {
                ok = true,
                approved = applied,
                released = accepted.Count,
                cleared = true,
                note = "Selections cleared in memory; click Save to persist clearing in settings"
            };
        }

        public object ClearApprovalSelections(BrainarrSettings settings)
        {
            settings.ReviewApproveKeys = Array.Empty<string>();
            TryPersistSettings();
            return new { ok = true, cleared = true, note = "Selections cleared and persisted (if supported)." };
        }

        public object RejectOrNeverSelected(BrainarrSettings settings, IDictionary<string, string> query, ReviewQueueService.ReviewStatus status)
        {
            var keysCsv = query.TryGetValue("keys", out var k) ? k : null;
            var keys = new List<string>();
            if (!string.IsNullOrWhiteSpace(keysCsv))
            {
                keys.AddRange(keysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (settings.ReviewApproveKeys != null)
            {
                keys.AddRange(settings.ReviewApproveKeys);
            }

            int applied = 0;
            foreach (var key in keys)
            {
                var parts = (key ?? "").Split('|');
                if (parts.Length >= 2)
                {
                    if (_reviewQueue.SetStatus(parts[0], parts[1], status))
                    {
                        applied++;
                        if (status == ReviewQueueService.ReviewStatus.Rejected)
                        {
                            _history.MarkAsRejected(parts[0], parts[1], reason: "Batch reject");
                        }
                        else if (status == ReviewQueueService.ReviewStatus.Never)
                        {
                            _history.MarkAsDisliked(parts[0], parts[1], RecommendationHistory.DislikeLevel.NeverAgain);
                        }
                    }
                }
            }

            // For symmetry, clear selection in memory after action
            settings.ReviewApproveKeys = Array.Empty<string>();
            TryPersistSettings();
            return new { ok = true, updated = applied, cleared = true, note = "Selections cleared and persisted (if supported)." };
        }

        internal void TryPersistSettings()
        {
            try
            {
                _persistSettingsCallback?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to persist Brainarr settings automatically");
            }
        }
    }
}
