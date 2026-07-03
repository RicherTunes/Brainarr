using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Performance;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.Parser.Model;
using Xunit;
using static NzbDrone.Core.ImportLists.Brainarr.Services.Support.RecommendationHistory;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Feature B8 (enforcement). "Never again" (MarkAsDisliked(..., NeverAgain)) was previously an INERT
    /// record — nothing in the recommendation pipeline read the exclusion store, so a disliked artist was
    /// still eligible for re-suggestion. These tests prove the now-live deterministic enforcement:
    ///   - SafetyGateService drops hard-excluded (Strong/NeverAgain) items from a run's output (and never
    ///     enqueues them to the review queue), and
    ///   - RecommendationPipeline.FilterHardExcluded closes the top-up path that bypasses the gate.
    /// Together with exclusions/remove this is the full re-enablement proof that was impossible before
    /// enforcement existed: excluded → run does NOT surface the artist → remove → run CAN surface it again.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ExclusionEnforcementTests : IDisposable
    {
        private readonly SafetyGateService _service = new();
        private readonly Logger _logger = LogManager.GetLogger("test");
        private readonly string _tempDir;
        private readonly ReviewQueueService _reviewQueue;
        private readonly RecommendationHistory _history;
        private readonly Mock<IPerformanceMetrics> _metrics = new();

        public ExclusionEnforcementTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"brainarr_excl_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _reviewQueue = new ReviewQueueService(_logger, _tempDir);
            _history = new RecommendationHistory(_logger, _tempDir);
        }

        private static BrainarrSettings Settings(bool queueBorderline = false, double minConfidence = 0.5) => new()
        {
            MinConfidence = minConfidence,
            RequireMbids = false,
            QueueBorderlineItems = queueBorderline,
            RecommendationMode = RecommendationMode.SpecificAlbums,
            MaxRecommendations = 10
        };

        private List<Recommendation> ApplyGate(List<Recommendation> input, BrainarrSettings settings)
            => _service.ApplySafetyGates(input, settings, _reviewQueue, _history, _logger, _metrics.Object, CancellationToken.None);

        // ---- The core round-trip: enforce -> undo -> re-enable ----

        [Fact]
        public void NeverAgain_IsEnforced_ThenRemove_ReEnables()
        {
            _history.MarkAsDisliked("Blocked Artist", null, DislikeLevel.NeverAgain);

            var input = () => new List<Recommendation>
            {
                new Recommendation { Artist = "Blocked Artist", Album = "Any Album", Confidence = 0.95 },
                new Recommendation { Artist = "Allowed Artist", Album = "Good Album", Confidence = 0.95 }
            };

            // 1. ENFORCEMENT: a run does NOT surface the excluded artist.
            var gatedWhileExcluded = ApplyGate(input(), Settings());
            gatedWhileExcluded.Select(r => r.Artist).Should().NotContain("Blocked Artist");
            gatedWhileExcluded.Select(r => r.Artist).Should().Contain("Allowed Artist");

            // 2. UNDO re-enables: after RemoveDislike the same run surfaces the artist again.
            _history.RemoveDislike("Blocked Artist").Should().BeTrue();
            var gatedAfterRemove = ApplyGate(input(), Settings());
            gatedAfterRemove.Select(r => r.Artist).Should().Contain("Blocked Artist");
            gatedAfterRemove.Select(r => r.Artist).Should().Contain("Allowed Artist");
        }

        [Fact]
        public void ExcludedBorderlineItem_IsNotEnqueuedToReviewQueue()
        {
            // A hard-excluded item that is ALSO borderline (low confidence) must be dropped BEFORE the
            // review-queue enqueue, not staged for later approval.
            _history.MarkAsDisliked("Blocked Artist", null, DislikeLevel.NeverAgain);

            var input = new List<Recommendation>
            {
                new Recommendation { Artist = "Blocked Artist", Album = "Low Conf", Confidence = 0.10 }
            };

            var gated = ApplyGate(input, Settings(queueBorderline: true));

            gated.Should().BeEmpty();
            _reviewQueue.GetPending().Should().NotContain(i => i.Artist == "Blocked Artist");
        }

        [Fact]
        public void AlbumSpecificNeverAgain_BlocksOnlyThatAlbum_NotWholeArtist()
        {
            _history.MarkAsDisliked("Partial Artist", "Bad Album", DislikeLevel.NeverAgain);

            var gated = ApplyGate(new List<Recommendation>
            {
                new Recommendation { Artist = "Partial Artist", Album = "Bad Album", Confidence = 0.95 },
                new Recommendation { Artist = "Partial Artist", Album = "Fine Album", Confidence = 0.95 }
            }, Settings());

            gated.Should().ContainSingle(r => r.Album == "Fine Album");
            gated.Should().NotContain(r => r.Album == "Bad Album");
        }

        [Fact]
        public void StrongDislike_IsEnforcedBySafetyGate()
        {
            _history.MarkAsDisliked("Strong Block", null, DislikeLevel.Strong);

            var gated = ApplyGate(new List<Recommendation>
            {
                new Recommendation { Artist = "Strong Block", Album = "Do Not Surface", Confidence = 0.95 },
                new Recommendation { Artist = "Allowed Artist", Album = "Good Album", Confidence = 0.95 }
            }, Settings());

            gated.Should().NotContain(r => r.Artist == "Strong Block");
            gated.Should().ContainSingle(r => r.Artist == "Allowed Artist");
        }

        // ---- IsHardExcluded predicate correctness (over-filter + normalization) ----

        [Fact]
        public void IsHardExcluded_ExactKeyOnly_DoesNotOverFilterSimilarArtist()
        {
            _history.MarkAsDisliked("Artist A", null, DislikeLevel.NeverAgain);
            var ex = _history.GetExclusions();

            RecommendationHistory.IsHardExcluded(ex, "Artist A", "X").Should().BeTrue();
            // "Artist AB" shares a prefix with "Artist A" — must NOT be dropped.
            RecommendationHistory.IsHardExcluded(ex, "Artist AB", "X").Should().BeFalse();
            RecommendationHistory.IsHardExcluded(ex, "Artist A Band", "X").Should().BeFalse();
        }

        [Theory]
        [InlineData("blocked artist")]      // lowercase
        [InlineData("BLOCKED ARTIST")]      // uppercase
        [InlineData("  Blocked   Artist ")] // leading/trailing + collapsed internal whitespace
        public void IsHardExcluded_MatchesAcrossCaseAndWhitespace(string variant)
        {
            _history.MarkAsDisliked("Blocked Artist", null, DislikeLevel.NeverAgain);
            var ex = _history.GetExclusions();

            // Every variant normalizes to the same key as the stored "Blocked Artist".
            RecommendationHistory.IsHardExcluded(ex, variant, null).Should().BeTrue();
        }

        [Fact]
        public void IsHardExcluded_MatchesAcrossHtmlEntities()
        {
            // Stored with a raw '&'; a model may emit the HTML-entity-encoded form — both must match
            // (same HtmlDecode normalization the dedup keys use).
            _history.MarkAsDisliked("Blocked & Artist", null, DislikeLevel.NeverAgain);
            var ex = _history.GetExclusions();

            RecommendationHistory.IsHardExcluded(ex, "Blocked &amp; Artist", null).Should().BeTrue();
            RecommendationHistory.IsHardExcluded(ex, "blocked & artist", null).Should().BeTrue();
            RecommendationHistory.IsHardExcluded(ex, "Blocked Artist", null).Should().BeFalse();
        }

        [Fact]
        public void IsHardExcluded_EmptyExclusions_ReturnsFalse()
        {
            var ex = _history.GetExclusions();
            RecommendationHistory.IsHardExcluded(ex, "Anyone", "Anything").Should().BeFalse();
            RecommendationHistory.IsHardExcluded(null, "Anyone", "Anything").Should().BeFalse();
        }

        [Fact]
        public void IsHardExcluded_NormalDislikeIsNotHardExcluded()
        {
            // Level.Normal lands in Disliked (soft), NOT StronglyDisliked — it is a prompt-level hint,
            // never a hard drop. Only Strong/NeverAgain are enforced here.
            _history.MarkAsDisliked("Soft Artist", null, DislikeLevel.Normal);
            var ex = _history.GetExclusions();
            RecommendationHistory.IsHardExcluded(ex, "Soft Artist", null).Should().BeFalse();
        }

        // ---- FilterHardExcluded: the top-up safety net over the final import list ----

        [Fact]
        public void FilterHardExcluded_DropsExcluded_KeepsOthers_NoOverFilter()
        {
            _history.MarkAsDisliked("Blocked Artist", null, DislikeLevel.NeverAgain);
            var ex = _history.GetExclusions();

            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Blocked Artist", Album = "A" },
                new ImportListItemInfo { Artist = "Blocked Artist Two", Album = "B" }, // prefix-similar, must stay
                new ImportListItemInfo { Artist = "Allowed Artist", Album = "C" }
            };

            var result = RecommendationPipeline.FilterHardExcluded(items, ex, _logger);

            result.Select(i => i.Artist).Should().BeEquivalentTo(new[] { "Blocked Artist Two", "Allowed Artist" });
        }

        [Fact]
        public void FilterHardExcludedRecommendations_DropsExcludedBeforeEnrichment()
        {
            _history.MarkAsDisliked("Blocked Artist", null, DislikeLevel.NeverAgain);
            var ex = _history.GetExclusions();

            var items = new List<Recommendation>
            {
                new Recommendation { Artist = "Blocked Artist", Album = "A" },
                new Recommendation { Artist = "Allowed Artist", Album = "B" }
            };

            var result = RecommendationPipeline.FilterHardExcludedRecommendations(items, ex, _logger);

            result.Should().ContainSingle(r => r.Artist == "Allowed Artist");
            result.Should().NotContain(r => r.Artist == "Blocked Artist");
        }

        [Fact]
        public void FilterHardExcludedRecommendations_PreservesNullEntries()
        {
            _history.MarkAsDisliked("Blocked Artist", null, DislikeLevel.NeverAgain);
            var ex = _history.GetExclusions();

            var items = new List<Recommendation>
            {
                null,
                new Recommendation { Artist = "Blocked Artist", Album = "A" },
                new Recommendation { Artist = "Allowed Artist", Album = "B" }
            };

            var result = RecommendationPipeline.FilterHardExcludedRecommendations(items, ex, _logger);

            result.Should().HaveCount(2);
            result.Should().Contain(i => i == null);
            result.Should().Contain(i => i != null && i.Artist == "Allowed Artist");
        }

        [Fact]
        public void FilterHardExcluded_PreservesNullEntries()
        {
            _history.MarkAsDisliked("Blocked Artist", null, DislikeLevel.NeverAgain);
            var ex = _history.GetExclusions();

            var items = new List<ImportListItemInfo>
            {
                null,
                new ImportListItemInfo { Artist = "Blocked Artist", Album = "A" },
                new ImportListItemInfo { Artist = "Allowed Artist", Album = "B" }
            };

            var result = RecommendationPipeline.FilterHardExcluded(items, ex, _logger);

            result.Should().HaveCount(2);
            result.Should().Contain(i => i == null);
            result.Should().Contain(i => i != null && i.Artist == "Allowed Artist");
        }

        [Fact]
        public void FilterHardExcluded_EmptyExclusionSet_IsPassThrough()
        {
            var ex = _history.GetExclusions();
            var items = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "X", Album = "Y" } };
            RecommendationPipeline.FilterHardExcluded(items, ex, _logger).Should().HaveCount(1);
            RecommendationPipeline.FilterHardExcluded(null, ex, _logger).Should().BeEmpty();
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
