using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Manages the review queue operations including accepting, rejecting, and marking items as "never again".
    /// Handles batch operations and integrates with settings persistence.
    /// </summary>
    public class ReviewQueueManager : IReviewQueueManager
    {
        private readonly Logger _logger;
        private readonly ReviewQueueService _reviewQueue;
        private readonly RecommendationHistory _history;
        private readonly Action _persistSettingsCallback;

        public ReviewQueueManager(
            Logger logger,
            ReviewQueueService reviewQueue,
            RecommendationHistory history,
            Action persistSettingsCallback = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _reviewQueue = reviewQueue ?? new ReviewQueueService(logger);
            _history = history ?? new RecommendationHistory(logger);
            _persistSettingsCallback = persistSettingsCallback;
        }

        /// <summary>
        /// Handles review status updates (accept/reject).
        /// </summary>
        /// <param name="artist">Artist name.</param>
        /// <param name="album">Album name (can be empty for artist-only).</param>
        /// <param name="status">Review status to set.</param>
        /// <param name="notes">Optional notes for the action.</param>
        /// <returns>Result object with ok flag and optional error message.</returns>
        public object HandleReviewUpdate(string artist, string album, ReviewQueueService.ReviewStatus status, string notes = null)
        {
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

        /// <summary>
        /// Handles marking an item as "never again" (strong negative constraint).
        /// </summary>
        /// <param name="artist">Artist name.</param>
        /// <param name="album">Album name (optional, can be empty).</param>
        /// <param name="notes">Optional notes for the action.</param>
        /// <returns>Result object with ok flag and optional error message.</returns>
        public object HandleReviewNever(string artist, string album, string notes = null)
        {
            if (string.IsNullOrWhiteSpace(artist))
            {
                return new { ok = false, error = "artist is required" };
            }

            var ok = _reviewQueue.SetStatus(artist, album, ReviewQueueService.ReviewStatus.Never, notes);
            // Strong negative constraint
            _history.MarkAsDisliked(artist, album, RecommendationHistory.DislikeLevel.NeverAgain);
            return new { ok };
        }

        /// <summary>
        /// Applies approvals from selected keys or settings, releasing accepted items to import.
        /// </summary>
        /// <param name="settings">Settings containing approval keys.</param>
        /// <param name="keysCsv">Optional comma-separated list of keys to approve.</param>
        /// <returns>Result with approval counts and cleared flag.</returns>
        public object ApplyApprovalsNow(BrainarrSettings settings, string keysCsv = null)
        {
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
            // Clear approval selections in memory
            settings.ReviewApproveKeys = Array.Empty<string>();

            // Attempt to persist the cleared selections
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

        /// <summary>
        /// Clears approval selections from settings.
        /// </summary>
        /// <returns>Result object with cleared flag.</returns>
        public object ClearApprovalSelections(BrainarrSettings settings)
        {
            settings.ReviewApproveKeys = Array.Empty<string>();
            TryPersistSettings();
            return new { ok = true, cleared = true, note = "Selections cleared and persisted (if supported)." };
        }

        /// <summary>
        /// Batch rejects or marks items as "never" based on selected keys.
        /// </summary>
        /// <param name="settings">Settings containing approval keys.</param>
        /// <param name="keysCsv">Optional comma-separated list of keys.</param>
        /// <param name="status">Status to apply (Rejected or Never).</param>
        /// <returns>Result object with updated count and cleared flag.</returns>
        public object RejectOrNeverSelected(BrainarrSettings settings, string keysCsv, ReviewQueueService.ReviewStatus status)
        {
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

            // Clear selection in memory after action
            settings.ReviewApproveKeys = Array.Empty<string>();
            TryPersistSettings();

            return new { ok = true, updated = applied, cleared = true, note = "Selections cleared and persisted (if supported)." };
        }

        /// <summary>
        /// Gets pending review queue items as options for UI display.
        /// </summary>
        /// <returns>Object with options array containing { value, name } pairs.</returns>
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

        /// <summary>
        /// Gets review summary statistics as read-only options.
        /// </summary>
        /// <returns>Object with options array containing status summaries.</returns>
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

        /// <summary>
        /// Gets the pending review queue items.
        /// </summary>
        public object GetPendingItems()
        {
            return new { items = _reviewQueue.GetPending() };
        }

        /// <summary>
        /// Gets review queue counts.
        /// </summary>
        public (int pending, int accepted, int rejected, int never) GetCounts()
        {
            return _reviewQueue.GetCounts();
        }

        /// <summary>
        /// Dequeues accepted items from the review queue.
        /// </summary>
        public List<Recommendation> DequeueAccepted()
        {
            return _reviewQueue.DequeueAccepted();
        }

        /// <summary>
        /// Sets the status of a review queue item.
        /// </summary>
        public bool SetStatus(string artist, string album, ReviewQueueService.ReviewStatus status, string notes = null)
        {
            return _reviewQueue.SetStatus(artist, album, status, notes);
        }

        /// <summary>
        /// Attempts to persist settings using the registered callback.
        /// </summary>
        private void TryPersistSettings()
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
