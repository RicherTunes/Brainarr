using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Triage;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationTriageAdvisorTests
    {
        [Fact]
        public void Analyze_ShouldSuggestReject_ForLowConfidenceDuplicateSignals()
        {
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings
            {
                MinConfidence = 0.5,
                RequireMbids = true,
                RecommendationMode = RecommendationMode.SpecificAlbums
            };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.2,
                Reason = "already in library duplicate"
            };

            var result = advisor.Analyze(item, settings);

            result.SuggestedAction.Should().Be("reject");
            result.RiskScore.Should().BeGreaterOrEqualTo(6);
            result.Reasons.Should().Contain(x => x.Contains("duplicate"));
            result.ReasonCodes.Should().Contain(TriageReasonCodes.DuplicateSignal);
            result.DetailedReasons.Should().Contain(x => x.Weight > 0);
        }

        [Fact]
        public void Analyze_ShouldSuggestAccept_ForHighConfidenceWithMbids()
        {
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings
            {
                MinConfidence = 0.6,
                RequireMbids = true,
                RecommendationMode = RecommendationMode.SpecificAlbums
            };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.93,
                ArtistMusicBrainzId = "artist-mbid",
                AlbumMusicBrainzId = "album-mbid"
            };

            var result = advisor.Analyze(item, settings);

            result.SuggestedAction.Should().Be("accept");
            result.ConfidenceBand.Should().Be("high");
            result.ReasonCodes.Should().Contain(TriageReasonCodes.ConsistentSignals);
            result.RiskScore.Should().Be(0);
        }

        [Fact]
        public void Analyze_ShouldIncludeNegativeWeightReason_WhenHighConfidenceOffsetsRisk()
        {
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings
            {
                MinConfidence = 0.95,
                RequireMbids = false
            };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.91,
                ArtistMusicBrainzId = "artist-mbid"
            };

            var result = advisor.Analyze(item, settings);

            result.SuggestedAction.Should().Be("accept");
            result.ReasonCodes.Should().Contain(TriageReasonCodes.HighConfidenceWithMbid);
            result.DetailedReasons.Should().Contain(x => x.Weight < 0);
        }

        [Fact]
        public void Analyze_UnscoredItem_NotPenalizedBelowThreshold_AndBandIsUnscored()
        {
            // Provenance: the model never reported a confidence; item.Confidence (0.7) is a parser
            // placeholder. Even with the floor raised to 0.85, it must NOT be flagged below-threshold
            // (mirrors SafetyGateService's !ConfidenceProvided gate), and the band must not fabricate
            // a High/Medium/Low label.
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings { MinConfidence = 0.85, RequireMbids = false };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.7,
                ConfidenceProvided = false,
            };

            var result = advisor.Analyze(item, settings);

            result.ReasonCodes.Should().NotContain(TriageReasonCodes.ConfidenceBelowThreshold);
            result.ReasonCodes.Should().NotContain(TriageReasonCodes.ConfidenceFarBelowThreshold);
            result.ReasonCodes.Should().Contain(RecommendationTriageAdvisor.ConfidenceNotProvidedCode);
            result.ConfidenceBand.Should().Be(RecommendationTriageAdvisor.UnscoredBand);
            result.RiskScore.Should().Be(0);
            result.SuggestedAction.Should().Be("accept");
        }

        [Fact]
        public void Analyze_UnscoredItem_SkipsProviderCalibration()
        {
            // Calibrating a placeholder score is meaningless and its CALIBRATION_APPLIED reason
            // would mislead, so the whole calibration block is gated on provenance.
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings { MinConfidence = 0.7, RequireMbids = false };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.7,
                ConfidenceProvided = false,
                ArtistMusicBrainzId = "artist-mbid",
            };

            var result = advisor.Analyze(item, settings, provider: AIProvider.Ollama);

            result.ReasonCodes.Should().NotContain(TriageReasonCodes.CalibrationApplied);
            result.ReasonCodes.Should().NotContain(TriageReasonCodes.LowCalibrationProvider);
            result.ConfidenceBand.Should().Be(RecommendationTriageAdvisor.UnscoredBand);
        }

        [Fact]
        public void Analyze_UnscoredItem_StillEnforcesIndependentSignals()
        {
            // Provenance only gates CONFIDENCE-derived scoring. MBID-required and duplicate-signal
            // checks are independent and must still fire for a score-less item.
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings
            {
                MinConfidence = 0.7,
                RequireMbids = true,
                RecommendationMode = RecommendationMode.SpecificAlbums,
            };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.7,
                ConfidenceProvided = false,
                Reason = "already in library duplicate",
            };

            var result = advisor.Analyze(item, settings);

            result.ReasonCodes.Should().Contain(TriageReasonCodes.MissingRequiredMbids);
            result.ReasonCodes.Should().Contain(TriageReasonCodes.DuplicateSignal);
            result.ReasonCodes.Should().Contain(RecommendationTriageAdvisor.ConfidenceNotProvidedCode);
        }

        [Fact]
        public void Analyze_ProvidedLowConfidence_StillPenalized()
        {
            // Regression: a model-REPORTED low score must still be penalized — the provenance gate
            // must not blunt the real floor.
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings { MinConfidence = 0.7, RequireMbids = false };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.3,
                ConfidenceProvided = true,
            };

            var result = advisor.Analyze(item, settings);

            result.ReasonCodes.Should().Contain(TriageReasonCodes.ConfidenceBelowThreshold);
            result.ReasonCodes.Should().Contain(TriageReasonCodes.ConfidenceFarBelowThreshold);
            result.ConfidenceBand.Should().Be("low");
        }

        [Fact]
        public void CalibrationDisabled_ReturnsRawScores()
        {
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings
            {
                MinConfidence = 0.7,
                RequireMbids = false
            };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.75,
                ArtistMusicBrainzId = "artist-mbid"
            };

            // With calibration disabled (provider=null), raw scores are used
            var rawResult = advisor.Analyze(item, settings, provider: null);

            // With calibration enabled (Ollama has Scale=0.80, Bias=0.05), scores differ
            var calibratedResult = advisor.Analyze(item, settings, provider: AIProvider.Ollama);

            // Calibrated result should have the CalibrationApplied reason code
            calibratedResult.ReasonCodes.Should().Contain(TriageReasonCodes.CalibrationApplied);

            // Raw result should NOT have the CalibrationApplied reason code
            rawResult.ReasonCodes.Should().NotContain(TriageReasonCodes.CalibrationApplied);

            // The calibrated result should also flag the low-quality provider
            calibratedResult.ReasonCodes.Should().Contain(TriageReasonCodes.LowCalibrationProvider);
        }
    }
}
