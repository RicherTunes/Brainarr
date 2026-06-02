using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Common.Abstractions.Triage;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using CommonBand = Lidarr.Plugin.Common.Abstractions.Triage.ConfidenceBand;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    internal sealed class RecommendationTriageAdvisor
    {
        /// <summary>
        /// Reason code for an item whose confidence the model never reported (the parser
        /// fabricated a placeholder). Brainarr-local until promoted to Common's
        /// <c>TriageReasonCodes</c> (queued — touches the Lidarr.Plugin.Common submodule).
        /// </summary>
        internal const string ConfidenceNotProvidedCode = "CONFIDENCE_NOT_PROVIDED";

        /// <summary>Band label for an item with no model-reported confidence (not High/Medium/Low).</summary>
        internal const string UnscoredBand = "unscored";

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

            // Provider calibration and confidence-threshold scoring are meaningful ONLY when the
            // model actually reported a confidence. When it didn't (ConfidenceProvided == false),
            // item.Confidence is a fabricated placeholder (parser default) — calibrating or
            // threshold-penalizing it mislabels a score-less item (e.g. flagging it "below
            // threshold" purely because the model omitted a self-rating, the same cliff
            // SafetyGateService closed with its `!ConfidenceProvided || ...` gate). Skip all
            // confidence-derived signals and surface the provenance explicitly instead.
            var confidence = item.Confidence;
            var confidenceProvided = item.ConfidenceProvided;

            if (confidenceProvided)
            {
                var profile = ProviderCalibrationRegistry.GetProfileOrNull(provider);
                if (profile != null && !profile.IsIdentity)
                {
                    var calibrated = profile.Calibrate(confidence);
                    AddReason(
                        TriageReasonCodes.CalibrationApplied,
                        $"provider {profile.ProviderName} calibration: {confidence:F2} -> {calibrated:F2} (scale={profile.Scale:F2}, bias={profile.Bias:F2})",
                        0);
                    confidence = calibrated;

                    if (profile.QualityTier < 0.6)
                    {
                        AddReason(
                            TriageReasonCodes.LowCalibrationProvider,
                            $"provider {profile.ProviderName} has low quality tier ({profile.QualityTier:F2})",
                            1);
                    }
                }

                var minConfidence = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
                if (confidence < minConfidence)
                {
                    AddReason(
                        TriageReasonCodes.ConfidenceBelowThreshold,
                        $"confidence {confidence:F2} below threshold {minConfidence:F2}",
                        2);
                }

                if (confidence < (minConfidence - 0.15))
                {
                    AddReason(TriageReasonCodes.ConfidenceFarBelowThreshold, "confidence substantially below threshold", 2);
                }
            }
            else
            {
                AddReason(ConfidenceNotProvidedCode, "model did not report a confidence score; confidence-based triage skipped", 0);
            }

            if (settings.RequireMbids)
            {
                var artistMissing = string.IsNullOrWhiteSpace(item.ArtistMusicBrainzId);
                var albumMissing = string.IsNullOrWhiteSpace(item.AlbumMusicBrainzId);
                var needsAlbumMbid = settings.RecommendationMode != RecommendationMode.Artists;

                if (artistMissing || (needsAlbumMbid && albumMissing))
                {
                    AddReason(TriageReasonCodes.MissingRequiredMbids, "missing required MusicBrainz identifiers", 2);
                }
            }

            if (ContainsDuplicateSignal(item.Reason) || ContainsDuplicateSignal(item.Notes))
            {
                AddReason(TriageReasonCodes.DuplicateSignal, "duplicate-like signal in recommendation rationale", 3);
            }

            if (confidenceProvided && confidence >= 0.9 && !string.IsNullOrWhiteSpace(item.ArtistMusicBrainzId))
            {
                var reducedBy = Math.Min(1, riskScore);
                if (reducedBy > 0)
                {
                    AddReason(TriageReasonCodes.HighConfidenceWithMbid, "high confidence with artist MBID present", -reducedBy);
                }
            }

            var suggestedAction = riskScore >= 6 ? "reject" : riskScore >= 3 ? "review" : "accept";
            string confidenceBand;
            if (!confidenceProvided)
            {
                // Don't fabricate a High/Medium/Low band from a placeholder score.
                confidenceBand = UnscoredBand;
            }
            else
            {
                var band = confidence >= 0.8 ? CommonBand.High : confidence >= 0.6 ? CommonBand.Medium : CommonBand.Low;
                confidenceBand = band.ToString().ToLowerInvariant();
            }
            if (detailedReasons.Count == 0)
            {
                detailedReasons.Add(new ReviewTriageReason(TriageReasonCodes.ConsistentSignals, "signals look consistent for queue approval", 0));
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
