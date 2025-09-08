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
                catch { }
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
                        try { history?.MarkAsAccepted(pr.Artist, pr.Album); } catch { }
                    }
                    metrics?.RecordArtistModePromotions(promoted.Count);
                    logger?.Warn($"Promoted {promoted.Count} artist(s) without MBIDs for downstream mapping");
                }
            }

            var acceptedFromQueue = reviewQueue.DequeueAccepted();
            if (acceptedFromQueue.Count > 0) passNow.AddRange(acceptedFromQueue);
            try
            {
                foreach (var item in acceptedFromQueue)
                {
                    history?.MarkAsAccepted(item.Artist, item.Album);
                }
            }
            catch { }

            if (settings.ReviewApproveKeys != null)
            {
                int applied = 0;
                foreach (var key in settings.ReviewApproveKeys)
                {
                    if (ct.IsCancellationRequested) break;
                    var parts = (key ?? "").Split('|');
                    if (parts.Length >= 2)
                    {
                        if (reviewQueue.SetStatus(parts[0], parts[1], ReviewQueueService.ReviewStatus.Accepted)) applied++;
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
                    catch { }
                }
            }

            return passNow;
        }
    }
}
