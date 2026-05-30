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
            var belowFloorCount = 0;   // model EXPLICITLY scored below the floor (provenance-aware)
            var missingMbidCount = 0;  // passed confidence but gated for missing required MBID(s)
            for (int i = 0; i < enriched.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                var r = enriched[i];
                // Wave 17M: sanitize NaN/Infinity confidence before any downstream use.
                // A malformed provider response with Confidence=NaN previously triggered a
                // System.Text.Json crash via reviewQueue.Enqueue → JsonFileStore.SetAsync
                // (the serializer rejects non-finite doubles unless AllowNamedFloatingPointLiterals
                // is set). NaN compares false to any threshold so the item would normally route
                // to the review queue and then crash the whole sync. Recommendation.Confidence
                // is `init`, so swap the record in-place via a with-expression rather than
                // mutating (record `with` clones with the override; init-only property is
                // honored). Coerced value: 0.0 (lowest possible — flags as borderline without
                // aborting the run).
                if (!double.IsFinite(r.Confidence))
                {
                    r = r with { Confidence = 0.0 };
                    enriched[i] = r;
                }
                var hasMbids = recommendArtists
                    ? !string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId)
                    : (!string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) && !string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId));
                // The floor only filters items the model EXPLICITLY scored below it. An item whose
                // confidence was fabricated (model gave none) wasn't "scored below" — gating it out
                // would silently drop every score-less recommendation once the user raises the floor
                // above the parser's fabricated default. Such items bypass the confidence gate (the
                // MBID gate below still applies independently).
                var confOk = !r.ConfidenceProvided || r.Confidence >= minConf;
                var mbidOk = !requireMbids || hasMbids;
                if (confOk && mbidOk)
                {
                    passNow.Add(r);
                }
                else
                {
                    toQueue.Add(r);
                    // Attribute the drop: confidence floor is the primary reason when it fails; an
                    // item that cleared the floor but lacks required MBIDs is an MBID-gate drop.
                    if (!confOk) belowFloorCount++; else missingMbidCount++;
                }
            }

            // Observability: per-run, reason-segregated gate summary so an under-target run can be
            // attributed to the confidence floor (vs dedup/timeout/MBID). belowFloorCount is
            // provenance-aware — score-less items bypass the floor and are never counted here.
            if (belowFloorCount > 0)
            {
                // Per-run, race-free accumulation (see GateMetricsContext) so the orchestrator can
                // attribute an under-target run to the confidence floor. NOT a process-wide counter.
                GateMetricsContext.AddConfidenceFloorDrops(belowFloorCount);
            }
            if (belowFloorCount > 0 || missingMbidCount > 0)
            {
                var disposition = queueBorderline ? $"{toQueue.Count} queued for review" : $"{toQueue.Count} dropped";
                logger?.Info($"Safety gate: {passNow.Count} passed, {belowFloorCount} below Minimum Confidence {minConf:F2}, {missingMbidCount} missing required MBID(s) ({disposition}).");
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
