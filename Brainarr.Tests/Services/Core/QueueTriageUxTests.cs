using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Phase 16 tests for Queue Triage UX + Safety Controls:
    /// explainer endpoints, batch caps, cooldowns, and operator identity.
    /// </summary>
    public class QueueTriageUxTests : IDisposable
    {
        private readonly Logger _logger = LogManager.GetLogger("test");
        private readonly string _tempDir;

        public QueueTriageUxTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"brainarr-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private ReviewQueueActionHandler CreateHandler(ReviewQueueService queue = null)
        {
            queue ??= new ReviewQueueService(_logger);
            var history = new RecommendationHistory(_logger);
            var styleCatalog = new StyleCatalogService(_logger, httpClient: null);
            var advisor = new RecommendationTriageAdvisor();
            var audit = new ReviewActionAuditService(_logger, _tempDir);
            return new ReviewQueueActionHandler(queue, history, styleCatalog, advisor, null, _logger, audit);
        }

        private static BrainarrSettings DefaultSettings(
            double minConfidence = 0.55,
            bool requireMbids = true,
            bool enableTriage = true,
            int maxActions = 25,
            int cooldownMinutes = 15)
        {
            return new BrainarrSettings
            {
                MinConfidence = minConfidence,
                RequireMbids = requireMbids,
                RecommendationMode = RecommendationMode.SpecificAlbums,
                EnableAutoReviewTriageActions = enableTriage,
                MaxAutoReviewActionsPerRun = maxActions,
                ReviewActionCooldownMinutes = cooldownMinutes
            };
        }

        private static Recommendation MakeRec(string artist, string album, double confidence, string artistMbid = null, string albumMbid = null)
        {
            return new Recommendation
            {
                Artist = artist,
                Album = album,
                Confidence = confidence,
                ArtistMusicBrainzId = artistMbid,
                AlbumMusicBrainzId = albumMbid
            };
        }

        private static void EnqueueItem(ReviewQueueService queue, string artist, string album, double confidence, string artistMbid = null, string albumMbid = null)
        {
            queue.Enqueue(new[] { MakeRec(artist, album, confidence, artistMbid, albumMbid) });
        }

        // ── Explainer Endpoint Tests ─────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void ExplainItem_ReturnsWhyThisAndWhyNot()
        {
            var queue = new ReviewQueueService(_logger);
            EnqueueItem(queue, "TestArtist", "TestAlbum", 0.80, "m1", "m2");
            var handler = CreateHandler(queue);
            var settings = DefaultSettings();

            var result = handler.ExplainItem(settings, new Dictionary<string, string>
            {
                ["artist"] = "TestArtist",
                ["album"] = "TestAlbum"
            });

            dynamic r = result;
            ((bool)r.ok).Should().BeTrue();
            ((string)r.artist).Should().Be("TestArtist");
            ((string)r.album).Should().Be("TestAlbum");
            ((string)r.suggestedAction).Should().NotBeNullOrWhiteSpace();
            ((string)r.confidenceBand).Should().NotBeNullOrWhiteSpace();
            ((string)r.explanation).Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ExplainItem_MissingArtist_ReturnsError()
        {
            var handler = CreateHandler();
            var settings = DefaultSettings();

            var result = handler.ExplainItem(settings, new Dictionary<string, string>());

            dynamic r = result;
            ((bool)r.ok).Should().BeFalse();
            ((string)r.error).Should().Contain("artist");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ExplainItem_ItemNotInQueue_ReturnsError()
        {
            var handler = CreateHandler();
            var settings = DefaultSettings();

            var result = handler.ExplainItem(settings, new Dictionary<string, string>
            {
                ["artist"] = "NonExistent"
            });

            dynamic r = result;
            ((bool)r.ok).Should().BeFalse();
            ((string)r.error).Should().Contain("not found");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ExplainItem_HighConfidence_SuggestsAccept()
        {
            var queue = new ReviewQueueService(_logger);
            EnqueueItem(queue, "GoodArtist", "GoodAlbum", 0.92, "m1", "m2");
            var handler = CreateHandler(queue);
            var settings = DefaultSettings();

            var result = handler.ExplainItem(settings, new Dictionary<string, string>
            {
                ["artist"] = "GoodArtist",
                ["album"] = "GoodAlbum"
            });

            dynamic r = result;
            ((string)r.suggestedAction).Should().Be("accept");
            ((string)r.confidenceBand).Should().Be("high");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ExplainItem_LowConfidenceNoMbids_SuggestsReject()
        {
            var queue = new ReviewQueueService(_logger);
            EnqueueItem(queue, "WeakArtist", "WeakAlbum", 0.20);
            var handler = CreateHandler(queue);
            var settings = DefaultSettings();

            var result = handler.ExplainItem(settings, new Dictionary<string, string>
            {
                ["artist"] = "WeakArtist",
                ["album"] = "WeakAlbum"
            });

            dynamic r = result;
            ((string)r.suggestedAction).Should().Be("reject");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ExplainItem_WithProvider_IncludesCalibratedBy()
        {
            var queue = new ReviewQueueService(_logger);
            EnqueueItem(queue, "A", "B", 0.75, "m1", "m2");
            var handler = CreateHandler(queue);
            var settings = DefaultSettings();

            var result = handler.ExplainItem(settings, new Dictionary<string, string>
            {
                ["artist"] = "A",
                ["album"] = "B"
            }, AIProvider.Ollama);

            dynamic r = result;
            ((string)r.calibratedBy).Should().Be("Ollama");
        }

        // ── Batch Cap Tests ──────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void DefaultBatchCap_Is25()
        {
            var settings = new BrainarrSettings();
            settings.MaxAutoReviewActionsPerRun.Should().Be(25);
        }

        // ── Cooldown Tests ───────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void DefaultCooldown_Is15Minutes()
        {
            var settings = new BrainarrSettings();
            settings.ReviewActionCooldownMinutes.Should().Be(15);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CheckCooldown_NoRecentApply_ReturnsNull()
        {
            var handler = CreateHandler();
            var settings = DefaultSettings(cooldownMinutes: 15);

            var result = handler.CheckCooldown(settings);
            result.Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CheckCooldown_ZeroCooldown_ReturnsNull()
        {
            var handler = CreateHandler();
            var settings = DefaultSettings(cooldownMinutes: 0);

            var result = handler.CheckCooldown(settings);
            result.Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ApplyTriageSuggestions_DisabledAutoTriage_ReturnsDisabled()
        {
            var handler = CreateHandler();
            var settings = DefaultSettings(enableTriage: false);

            var result = handler.ApplyTriageSuggestions(settings, new Dictionary<string, string>());

            dynamic r = result;
            ((bool)r.ok).Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ApplyTriageSuggestions_RespectsMaxCap()
        {
            var queue = new ReviewQueueService(_logger);
            // Add many high-confidence items
            for (int i = 0; i < 10; i++)
            {
                EnqueueItem(queue, $"Artist{i}", $"Album{i}", 0.92, $"a{i}", $"b{i}");
            }

            var handler = CreateHandler(queue);
            var settings = DefaultSettings(maxActions: 3);

            var result = handler.ApplyTriageSuggestions(settings, new Dictionary<string, string>
            {
                ["idempotencyKey"] = Guid.NewGuid().ToString("N")
            });

            dynamic r = result;
            ((bool)r.ok).Should().BeTrue();
            ((int)r.approved).Should().BeLessOrEqualTo(3);
        }

        // ── Operator Identity Tests ──────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void ApplyTriageSuggestions_CapturesActorInAudit()
        {
            var queue = new ReviewQueueService(_logger);
            EnqueueItem(queue, "A", "B", 0.90, "m1", "m2");
            var handler = CreateHandler(queue);
            var settings = DefaultSettings();

            var result = handler.ApplyTriageSuggestions(settings, new Dictionary<string, string>
            {
                ["idempotencyKey"] = Guid.NewGuid().ToString("N"),
                ["actor"] = "test-operator"
            });

            dynamic r = result;
            ((bool)r.ok).Should().BeTrue();
            ((string)r.actor).Should().Be("test-operator");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ApplyTriageSuggestions_NoActor_DefaultsToSystem()
        {
            var queue = new ReviewQueueService(_logger);
            EnqueueItem(queue, "A", "B", 0.90, "m1", "m2");
            var handler = CreateHandler(queue);
            var settings = DefaultSettings();

            var result = handler.ApplyTriageSuggestions(settings, new Dictionary<string, string>
            {
                ["idempotencyKey"] = Guid.NewGuid().ToString("N")
            });

            dynamic r = result;
            ((string)r.actor).Should().Be("system");
        }

        // ── Safety Policy Tests ──────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void ApplyTriageSuggestions_AllActionsRollbackable()
        {
            var queue = new ReviewQueueService(_logger);
            EnqueueItem(queue, "A", "B", 0.90, "m1", "m2");
            var handler = CreateHandler(queue);
            var settings = DefaultSettings();

            var applyResult = handler.ApplyTriageSuggestions(settings, new Dictionary<string, string>
            {
                ["idempotencyKey"] = Guid.NewGuid().ToString("N")
            });

            dynamic apply = applyResult;
            ((bool)apply.ok).Should().BeTrue();

            // The applied batch should be rollbackable
            var auditId = (string)apply.audit.id;
            auditId.Should().NotBeNullOrWhiteSpace();

            // Rollback should succeed
            var rollbackResult = handler.RollbackTriageApplication(new Dictionary<string, string>
            {
                ["id"] = auditId,
                ["idempotencyKey"] = Guid.NewGuid().ToString("N")
            });

            dynamic rollback = rollbackResult;
            ((bool)rollback.ok).Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ApplyTriageSuggestions_IdempotentReplay_ReturnsSameResult()
        {
            var queue = new ReviewQueueService(_logger);
            EnqueueItem(queue, "A", "B", 0.90, "m1", "m2");
            var handler = CreateHandler(queue);
            var settings = DefaultSettings();

            var key = Guid.NewGuid().ToString("N");

            var result1 = handler.ApplyTriageSuggestions(settings, new Dictionary<string, string>
            {
                ["idempotencyKey"] = key
            });

            var result2 = handler.ApplyTriageSuggestions(settings, new Dictionary<string, string>
            {
                ["idempotencyKey"] = key
            });

            dynamic r1 = result1;
            dynamic r2 = result2;
            ((bool)r1.ok).Should().BeTrue();
            ((bool)r2.ok).Should().BeTrue();
            ((bool)r2.replay).Should().BeTrue(); // Second call is replay
        }
    }
}
