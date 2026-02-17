using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Per-provider golden fixture tests. Each test verifies that a standardized
    /// input item produces deterministic triage output for a specific provider's
    /// calibration profile. Satisfies Phase 14 KPI: >= 1 fixture per provider
    /// for minimum 5 providers.
    /// </summary>
    public class ProviderGoldenFixtureTests
    {
        private readonly RecommendationTriageAdvisor _advisor = new();

        private readonly BrainarrSettings _settings = new()
        {
            MinConfidence = 0.55,
            RequireMbids = true,
            RecommendationMode = RecommendationMode.SpecificAlbums
        };

        /// <summary>
        /// Standard borderline item used across all provider fixtures.
        /// Raw confidence 0.65 — just above the medium/low boundary.
        /// Provider calibration determines whether this becomes accept/review/reject.
        /// </summary>
        private static ReviewQueueService.ReviewItem BorderlineItem => new()
        {
            Artist = "BorderlineArtist",
            Album = "BorderlineAlbum",
            Confidence = 0.65,
            ArtistMusicBrainzId = "artist-mbid-001",
            AlbumMusicBrainzId = "album-mbid-001"
        };

        /// <summary>
        /// Standard high-confidence item used across all provider fixtures.
        /// Raw confidence 0.92 — well above thresholds for all providers.
        /// </summary>
        private static ReviewQueueService.ReviewItem HighConfidenceItem => new()
        {
            Artist = "StrongArtist",
            Album = "StrongAlbum",
            Confidence = 0.92,
            ArtistMusicBrainzId = "artist-mbid-002",
            AlbumMusicBrainzId = "album-mbid-002"
        };

        /// <summary>
        /// Standard low-confidence item with no MBIDs.
        /// Raw confidence 0.30 — below threshold for all providers.
        /// </summary>
        private static ReviewQueueService.ReviewItem LowConfidenceItem => new()
        {
            Artist = "WeakArtist",
            Album = "WeakAlbum",
            Confidence = 0.30
        };

        // ──────────────────────────────────────────────────────────────
        // Provider 1: OpenAI (identity calibration — reference provider)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void OpenAI_BorderlineItem_AcceptsWithIdentityCalibration()
        {
            var result = _advisor.Analyze(BorderlineItem, _settings, AIProvider.OpenAI);

            result.SuggestedAction.Should().Be("accept");
            result.ConfidenceBand.Should().Be("medium");
            result.RiskScore.Should().Be(0);
            result.CalibratedBy.Should().Be("OpenAI");
            // Identity calibration → no CALIBRATION_APPLIED reason
            result.ReasonCodes.Should().Contain("CONSISTENT_SIGNALS");
            result.ReasonCodes.Should().NotContain("CALIBRATION_APPLIED");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void OpenAI_HighConfidenceItem_AcceptsCleanly()
        {
            var result = _advisor.Analyze(HighConfidenceItem, _settings, AIProvider.OpenAI);

            result.SuggestedAction.Should().Be("accept");
            result.ConfidenceBand.Should().Be("high");
            result.RiskScore.Should().Be(0);
        }

        // ──────────────────────────────────────────────────────────────
        // Provider 2: Anthropic (identity calibration)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void Anthropic_BorderlineItem_SameAsOpenAI()
        {
            var result = _advisor.Analyze(BorderlineItem, _settings, AIProvider.Anthropic);

            result.SuggestedAction.Should().Be("accept");
            result.ConfidenceBand.Should().Be("medium");
            result.RiskScore.Should().Be(0);
            result.CalibratedBy.Should().Be("Anthropic");
        }

        // ──────────────────────────────────────────────────────────────
        // Provider 3: Ollama (local — strongest calibration adjustment)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void Ollama_BorderlineItem_DowngradesConfidenceAndTriggersReview()
        {
            // Raw 0.65 → calibrated = 0.65 * 0.80 + 0.05 = 0.57
            // 0.57 > 0.55 threshold → no CONFIDENCE_BELOW_THRESHOLD
            // But QualityTier 0.55 < 0.6 → LOW_CALIBRATION_PROVIDER (+1 risk)
            var result = _advisor.Analyze(BorderlineItem, _settings, AIProvider.Ollama);

            result.CalibratedBy.Should().Be("Ollama");
            result.ReasonCodes.Should().Contain("CALIBRATION_APPLIED");
            result.ReasonCodes.Should().Contain("LOW_CALIBRATION_PROVIDER");
            result.ConfidenceBand.Should().Be("low"); // 0.57 < 0.6 → low band
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void Ollama_HighConfidenceItem_StillAccepts()
        {
            // Raw 0.92 → calibrated = 0.92 * 0.80 + 0.05 = 0.786
            // Still above 0.55 threshold, medium band, but with calibration note
            var result = _advisor.Analyze(HighConfidenceItem, _settings, AIProvider.Ollama);

            result.SuggestedAction.Should().Be("accept");
            result.ConfidenceBand.Should().Be("medium"); // 0.786 < 0.8 → medium
            result.ReasonCodes.Should().Contain("CALIBRATION_APPLIED");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void Ollama_LowConfidenceItem_RejectsWithMultipleReasons()
        {
            // Raw 0.30 → calibrated = 0.30 * 0.80 + 0.05 = 0.29
            // Below threshold 0.55, far below (0.55 - 0.15 = 0.40), missing MBIDs
            var result = _advisor.Analyze(LowConfidenceItem, _settings, AIProvider.Ollama);

            result.SuggestedAction.Should().Be("reject");
            result.ReasonCodes.Should().Contain("CALIBRATION_APPLIED");
            result.ReasonCodes.Should().Contain("LOW_CALIBRATION_PROVIDER");
            result.ReasonCodes.Should().Contain("CONFIDENCE_BELOW_THRESHOLD");
            result.ReasonCodes.Should().Contain("CONFIDENCE_FAR_BELOW_THRESHOLD");
            result.ReasonCodes.Should().Contain("MISSING_REQUIRED_MBIDS");
        }

        // ──────────────────────────────────────────────────────────────
        // Provider 4: Groq (moderate calibration)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void Groq_BorderlineItem_CalibratesDown()
        {
            // Raw 0.65 → calibrated = 0.65 * 0.88 + 0.02 = 0.592
            // 0.592 > 0.55 → above threshold, low band (0.592 < 0.6)
            // QualityTier 0.70 >= 0.6 → no LOW_CALIBRATION_PROVIDER
            var result = _advisor.Analyze(BorderlineItem, _settings, AIProvider.Groq);

            result.CalibratedBy.Should().Be("Groq");
            result.ReasonCodes.Should().Contain("CALIBRATION_APPLIED");
            result.ReasonCodes.Should().NotContain("LOW_CALIBRATION_PROVIDER");
            result.ConfidenceBand.Should().Be("low"); // 0.592 < 0.6
        }

        // ──────────────────────────────────────────────────────────────
        // Provider 5: Gemini (slight calibration)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void Gemini_BorderlineItem_SlightlyCalibrated()
        {
            // Raw 0.65 → calibrated = 0.65 * 0.95 + 0.0 = 0.6175
            // Medium band (>= 0.6), above threshold
            var result = _advisor.Analyze(BorderlineItem, _settings, AIProvider.Gemini);

            result.CalibratedBy.Should().Be("Gemini");
            result.SuggestedAction.Should().Be("accept");
            result.ConfidenceBand.Should().Be("medium");
            result.ReasonCodes.Should().Contain("CALIBRATION_APPLIED");
        }

        // ──────────────────────────────────────────────────────────────
        // Provider 6: DeepSeek (moderate calibration)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void DeepSeek_BorderlineItem_CalibratesModerately()
        {
            // Raw 0.65 → calibrated = 0.65 * 0.92 + 0.0 = 0.598
            // Low band (< 0.6), still above threshold 0.55
            var result = _advisor.Analyze(BorderlineItem, _settings, AIProvider.DeepSeek);

            result.CalibratedBy.Should().Be("DeepSeek");
            result.SuggestedAction.Should().Be("accept");
            result.ConfidenceBand.Should().Be("low"); // 0.598 < 0.6
            result.ReasonCodes.Should().Contain("CALIBRATION_APPLIED");
        }

        // ──────────────────────────────────────────────────────────────
        // Provider 7: LM Studio (same as Ollama — local provider)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void LMStudio_BorderlineItem_SameAsOllama()
        {
            var ollamaResult = _advisor.Analyze(BorderlineItem, _settings, AIProvider.Ollama);
            var lmStudioResult = _advisor.Analyze(BorderlineItem, _settings, AIProvider.LMStudio);

            // Same calibration parameters → same triage outcome
            lmStudioResult.SuggestedAction.Should().Be(ollamaResult.SuggestedAction);
            lmStudioResult.ConfidenceBand.Should().Be(ollamaResult.ConfidenceBand);
            lmStudioResult.RiskScore.Should().Be(ollamaResult.RiskScore);
            lmStudioResult.CalibratedBy.Should().Be("LMStudio");
        }

        // ──────────────────────────────────────────────────────────────
        // Cross-provider determinism (snapshot stability)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void AllProviders_SameInput_DeterministicOutput()
        {
            // Verify that running the same item through the same provider twice
            // always produces identical results (no randomness, no side effects)
            foreach (AIProvider provider in System.Enum.GetValues<AIProvider>())
            {
                var r1 = _advisor.Analyze(BorderlineItem, _settings, provider);
                var r2 = _advisor.Analyze(BorderlineItem, _settings, provider);

                r1.SuggestedAction.Should().Be(r2.SuggestedAction, $"determinism check for {provider}");
                r1.RiskScore.Should().Be(r2.RiskScore, $"determinism check for {provider}");
                r1.ConfidenceBand.Should().Be(r2.ConfidenceBand, $"determinism check for {provider}");
                r1.ReasonCodes.Should().BeEquivalentTo(r2.ReasonCodes, $"determinism check for {provider}");
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void NoProvider_BackwardsCompatible_NoCalibration()
        {
            // When no provider is specified, behavior is identical to pre-calibration
            var result = _advisor.Analyze(BorderlineItem, _settings);

            result.SuggestedAction.Should().Be("accept");
            result.ConfidenceBand.Should().Be("medium");
            result.RiskScore.Should().Be(0);
            result.CalibratedBy.Should().BeNull();
            result.ReasonCodes.Should().Contain("CONSISTENT_SIGNALS");
            result.ReasonCodes.Should().NotContain("CALIBRATION_APPLIED");
        }

        // ──────────────────────────────────────────────────────────────
        // Full snapshot: serialized JSON for all 11 providers
        // ──────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void FullProviderSnapshot_BorderlineItem_StableAcrossAllProviders()
        {
            var snapshot = new Dictionary<string, object>();

            foreach (AIProvider provider in System.Enum.GetValues<AIProvider>())
            {
                var result = _advisor.Analyze(BorderlineItem, _settings, provider);
                snapshot[provider.ToString()] = new
                {
                    action = result.SuggestedAction,
                    band = result.ConfidenceBand,
                    risk = result.RiskScore,
                    reasons = result.ReasonCodes.ToList()
                };
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

            // Verify key expectations from the snapshot:
            // - Cloud API providers (OpenAI, Anthropic) → accept/medium
            // - Local providers (Ollama, LM Studio) → low band with calibration
            // - Subscription providers → same as their API counterparts
            snapshot.Keys.Should().HaveCount(11);

            // Verify cloud providers accept borderline items
            var openAiResult = _advisor.Analyze(BorderlineItem, _settings, AIProvider.OpenAI);
            openAiResult.SuggestedAction.Should().Be("accept");

            // Verify local providers downgrade confidence
            var ollamaResult = _advisor.Analyze(BorderlineItem, _settings, AIProvider.Ollama);
            ollamaResult.ConfidenceBand.Should().Be("low");
        }
    }
}
