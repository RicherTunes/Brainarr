using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    internal sealed class RecommendationTriageAdvisor
    {
        internal static class ReasonCodes
        {
            public const string ConfidenceBelowThreshold = "CONFIDENCE_BELOW_THRESHOLD";
            public const string ConfidenceFarBelowThreshold = "CONFIDENCE_FAR_BELOW_THRESHOLD";
            public const string MissingRequiredMbids = "MISSING_REQUIRED_MBIDS";
            public const string DuplicateSignal = "DUPLICATE_SIGNAL";
            public const string HighConfidenceWithMbid = "HIGH_CONFIDENCE_WITH_MBID";
            public const string ConsistentSignals = "CONSISTENT_SIGNALS";
        }

        private static readonly string[] DuplicateSignals =
        {
            "duplicate", "already", "exists", "in library", "owned", "seen before"
        };

        public ReviewTriageResult Analyze(ReviewQueueService.ReviewItem item, BrainarrSettings settings)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            settings ??= new BrainarrSettings();

            var detailedReasons = new List<ReviewTriageReason>();
            var riskScore = 0;

            void AddReason(string code, string message, int weight)
            {
                riskScore += weight;
                detailedReasons.Add(new ReviewTriageReason(code, message, weight));
            }

            var minConfidence = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
            if (item.Confidence < minConfidence)
            {
                AddReason(
                    ReasonCodes.ConfidenceBelowThreshold,
                    $"confidence {item.Confidence:F2} below threshold {minConfidence:F2}",
                    2);
            }

            if (item.Confidence < (minConfidence - 0.15))
            {
                AddReason(ReasonCodes.ConfidenceFarBelowThreshold, "confidence substantially below threshold", 2);
            }

            if (settings.RequireMbids)
            {
                var artistMissing = string.IsNullOrWhiteSpace(item.ArtistMusicBrainzId);
                var albumMissing = string.IsNullOrWhiteSpace(item.AlbumMusicBrainzId);
                var needsAlbumMbid = settings.RecommendationMode != RecommendationMode.Artists;

                if (artistMissing || (needsAlbumMbid && albumMissing))
                {
                    AddReason(ReasonCodes.MissingRequiredMbids, "missing required MusicBrainz identifiers", 2);
                }
            }

            if (ContainsDuplicateSignal(item.Reason) || ContainsDuplicateSignal(item.Notes))
            {
                AddReason(ReasonCodes.DuplicateSignal, "duplicate-like signal in recommendation rationale", 3);
            }

            if (item.Confidence >= 0.9 && !string.IsNullOrWhiteSpace(item.ArtistMusicBrainzId))
            {
                var reducedBy = Math.Min(1, riskScore);
                if (reducedBy > 0)
                {
                    AddReason(ReasonCodes.HighConfidenceWithMbid, "high confidence with artist MBID present", -reducedBy);
                }
            }

            var suggestedAction = riskScore >= 6 ? "reject" : riskScore >= 3 ? "review" : "accept";
            var confidenceBand = item.Confidence >= 0.8 ? "high" : item.Confidence >= 0.6 ? "medium" : "low";
            if (detailedReasons.Count == 0)
            {
                detailedReasons.Add(new ReviewTriageReason(ReasonCodes.ConsistentSignals, "signals look consistent for queue approval", 0));
            }

            return new ReviewTriageResult(suggestedAction, confidenceBand, riskScore, detailedReasons);
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
        IReadOnlyList<ReviewTriageReason> DetailedReasons)
    {
        public IReadOnlyList<string> Reasons => DetailedReasons == null
            ? Array.Empty<string>()
            : DetailedReasons.Select(reason => reason.Message).ToList();

        public IReadOnlyList<string> ReasonCodes => DetailedReasons == null
            ? Array.Empty<string>()
            : DetailedReasons.Select(reason => reason.Code).ToList();
    }

    internal sealed record ReviewTriageReason(
        string Code,
        string Message,
        int Weight);
}
