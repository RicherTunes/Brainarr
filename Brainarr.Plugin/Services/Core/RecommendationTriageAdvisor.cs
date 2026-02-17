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
            public const string CalibrationApplied = "CALIBRATION_APPLIED";
            public const string LowCalibrationProvider = "LOW_CALIBRATION_PROVIDER";
        }

        private static readonly string[] DuplicateSignals =
        {
            "duplicate", "already", "exists", "in library", "owned", "seen before"
        };

        /// <summary>
        /// Analyze item without provider calibration (backwards-compatible).
        /// </summary>
        public ReviewTriageResult Analyze(ReviewQueueService.ReviewItem item, BrainarrSettings settings)
        {
            return Analyze(item, settings, provider: null);
        }

        /// <summary>
        /// Analyze item with optional provider-specific confidence calibration.
        /// When a provider is specified, the raw confidence is adjusted using
        /// the provider's calibration profile before triage scoring.
        /// </summary>
        public ReviewTriageResult Analyze(ReviewQueueService.ReviewItem item, BrainarrSettings settings, AIProvider? provider)
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

            // Apply provider-specific calibration if provider is known
            var confidence = item.Confidence;
            var profile = ProviderCalibrationRegistry.GetProfileOrNull(provider);
            if (profile != null && !profile.IsIdentity)
            {
                var calibrated = profile.Calibrate(confidence);
                AddReason(
                    ReasonCodes.CalibrationApplied,
                    $"provider {profile.ProviderName} calibration: {confidence:F2} -> {calibrated:F2} (scale={profile.Scale:F2}, bias={profile.Bias:F2})",
                    0);
                confidence = calibrated;

                if (profile.QualityTier < 0.6)
                {
                    AddReason(
                        ReasonCodes.LowCalibrationProvider,
                        $"provider {profile.ProviderName} has low quality tier ({profile.QualityTier:F2})",
                        1);
                }
            }

            var minConfidence = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
            if (confidence < minConfidence)
            {
                AddReason(
                    ReasonCodes.ConfidenceBelowThreshold,
                    $"confidence {confidence:F2} below threshold {minConfidence:F2}",
                    2);
            }

            if (confidence < (minConfidence - 0.15))
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

            if (confidence >= 0.9 && !string.IsNullOrWhiteSpace(item.ArtistMusicBrainzId))
            {
                var reducedBy = Math.Min(1, riskScore);
                if (reducedBy > 0)
                {
                    AddReason(ReasonCodes.HighConfidenceWithMbid, "high confidence with artist MBID present", -reducedBy);
                }
            }

            var suggestedAction = riskScore >= 6 ? "reject" : riskScore >= 3 ? "review" : "accept";
            var confidenceBand = confidence >= 0.8 ? "high" : confidence >= 0.6 ? "medium" : "low";
            if (detailedReasons.Count == 0)
            {
                detailedReasons.Add(new ReviewTriageReason(ReasonCodes.ConsistentSignals, "signals look consistent for queue approval", 0));
            }

            return new ReviewTriageResult(suggestedAction, confidenceBand, riskScore, detailedReasons, provider?.ToString());
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
        IReadOnlyList<ReviewTriageReason> DetailedReasons,
        string? CalibratedBy = null)
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
