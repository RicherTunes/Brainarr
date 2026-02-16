using System;
using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    internal sealed class RecommendationTriageAdvisor
    {
        private static readonly string[] DuplicateSignals =
        {
            "duplicate", "already", "exists", "in library", "owned", "seen before"
        };

        public ReviewTriageResult Analyze(ReviewQueueService.ReviewItem item, BrainarrSettings settings)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            settings ??= new BrainarrSettings();

            var reasons = new List<string>();
            var riskScore = 0;

            var minConfidence = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
            if (item.Confidence < minConfidence)
            {
                riskScore += 2;
                reasons.Add($"confidence {item.Confidence:F2} below threshold {minConfidence:F2}");
            }

            if (item.Confidence < (minConfidence - 0.15))
            {
                riskScore += 2;
                reasons.Add("confidence substantially below threshold");
            }

            if (settings.RequireMbids)
            {
                var artistMissing = string.IsNullOrWhiteSpace(item.ArtistMusicBrainzId);
                var albumMissing = string.IsNullOrWhiteSpace(item.AlbumMusicBrainzId);
                var needsAlbumMbid = settings.RecommendationMode != RecommendationMode.Artists;

                if (artistMissing || (needsAlbumMbid && albumMissing))
                {
                    riskScore += 2;
                    reasons.Add("missing required MusicBrainz identifiers");
                }
            }

            if (ContainsDuplicateSignal(item.Reason) || ContainsDuplicateSignal(item.Notes))
            {
                riskScore += 3;
                reasons.Add("duplicate-like signal in recommendation rationale");
            }

            if (item.Confidence >= 0.9 && !string.IsNullOrWhiteSpace(item.ArtistMusicBrainzId))
            {
                riskScore = Math.Max(0, riskScore - 1);
                reasons.Add("high confidence with artist MBID present");
            }

            var suggestedAction = riskScore >= 6 ? "reject" : riskScore >= 3 ? "review" : "accept";
            var confidenceBand = item.Confidence >= 0.8 ? "high" : item.Confidence >= 0.6 ? "medium" : "low";
            if (reasons.Count == 0)
            {
                reasons.Add("signals look consistent for queue approval");
            }

            return new ReviewTriageResult(suggestedAction, confidenceBand, riskScore, reasons);
        }

        private static bool ContainsDuplicateSignal(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var normalized = value.ToLowerInvariant();
            foreach (var token in DuplicateSignals)
            {
                if (normalized.Contains(token))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed record ReviewTriageResult(
        string SuggestedAction,
        string ConfidenceBand,
        int RiskScore,
        IReadOnlyList<string> Reasons);
}
