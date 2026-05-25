using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class SafetyGateService : ISafetyGateService
    {
        public List<Recommendation> ApplySafetyGates(
            List<Recommendation> enriched,
            BrainarrSettings settings,
            ReviewQueueService reviewQueue,
            RecommendationHistory history,
            Logger logger,
            NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics metrics,
            CancellationToken ct)
        {
            enriched ??= new List<Recommendation>();
            var minConf = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
            var requireMbids = settings.RequireMbids;
            var queueBorderline = settings.QueueBorderlineItems;
            var recommendArtists = settings.RecommendationMode == RecommendationMode.Artists;

            var passNow = new List<Recommendation>();
            var toQueue = new List<Recommendation>();
            foreach (var r in enriched)
            {
                if (ct.IsCancellationRequested) break;
                // Wave 17M: sanitize NaN/Infinity confidence before any downstream use.
                // A malformed provider response with Confidence=NaN previously triggered an
                // NRE chain via reviewQueue.Enqueue → JsonFileStore.SetAsync, which can't
                // serialize NaN/Infinity per System.Text.Json. NaN compares false to any
                // threshold so it would normally route to the review queue and then crash
                // the whole sync. Coerce non-finite values to 0.0 (lowest possible — flags
                // the item as borderline without aborting the run).
                if (!double.IsFinite(r.Confidence))
                {
                    r.Confidence = 0.0;
                }
                var hasMbids = recommendArtists
                    ? !string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId)
                    : (!string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) && !string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId));
                var confOk = r.Confidence >= minConf;
                if (confOk && (!requireMbids || hasMbids)) passNow.Add(r); else toQueue.Add(r);
            }

            if (toQueue.Count > 0 && queueBorderline)
            {
                try
                {
                    var preview = string.Join(", ", toQueue.Take(3).Select(r => string.IsNullOrWhiteSpace(r.Album) ? r.Artist : $"{r.Artist} - {r.Album}"));
                    logger?.Debug($"Queueing {toQueue.Count} borderline item(s) due to safety gates; sample: {preview}");
                }
                catch (Exception ex) { logger?.Debug(ex, "Non-critical: Failed to log borderline item preview"); }
                reviewQueue.Enqueue(toQueue, reason: "Safety gate (confidence/MBID)");
            }

            if (recommendArtists && requireMbids && passNow.Count == 0)
            {
                logger?.Warn("Artist-mode MBID requirement filtered all items. Consider disabling 'Require MusicBrainz IDs' or ensure network access for MusicBrainz lookups.");
                if (toQueue.Count > 0)
                {
                    var targetCount = Math.Max(1, settings.MaxRecommendations);
                    var promoted = toQueue.Take(targetCount).ToList();
                    passNow.AddRange(promoted);
                    foreach (var pr in promoted)
                    {
                        reviewQueue.SetStatus(pr.Artist, pr.Album ?? string.Empty, ReviewQueueService.ReviewStatus.Accepted);
                        try { history?.MarkAsAccepted(pr.Artist, pr.Album); }
                        catch (Exception ex) { logger?.Debug(ex, "Non-critical: Failed to mark artist as accepted in history"); }
                    }
                    metrics?.RecordArtistModePromotions(promoted.Count);
                    logger?.Warn($"Promoted {promoted.Count} artist(s) without MBIDs for downstream mapping");
                }
            }

            // Do not auto-release previously accepted items here.
            // Releasing is triggered explicitly either via settings (Approve Suggestions)
            // or the orchestrator's review/apply action to avoid leaking state across runs/tests.

            if (settings.ReviewApproveKeys != null)
            {
                int applied = 0;
                foreach (var key in settings.ReviewApproveKeys)
                {
                    if (ct.IsCancellationRequested) break;
                    // Wave 17M: split on the LAST '|' so artists that contain '|' (e.g. "AC|DC")
                    // can be approved through the natural-looking key "AC|DC|Highway". The
                    // previous Split('|') treated the first occurrence as the separator, so
                    // "AC|DC|Highway" parsed as artist="AC", album="DC", and the real item
                    // never matched any pending entry. Album with '|' is still ambiguous but
                    // is a far rarer real-world case.
                    var k = key ?? "";
                    var lastPipe = k.LastIndexOf('|');
                    if (lastPipe > 0)
                    {
                        var artist = k.Substring(0, lastPipe);
                        var album = k.Substring(lastPipe + 1);
                        if (reviewQueue.SetStatus(artist, album, ReviewQueueService.ReviewStatus.Accepted)) applied++;
                    }
                }
                if (applied > 0)
                {
                    var approvedNow = reviewQueue.DequeueAccepted();
                    passNow.AddRange(approvedNow);
                    try
                    {
                        foreach (var item in approvedNow)
                        {
                            history?.MarkAsAccepted(item.Artist, item.Album);
                        }
                    }
                    catch (Exception ex) { logger?.Debug(ex, "Non-critical: Failed to mark approved items in history"); }
                }
            }

            return passNow;
        }
    }
}
