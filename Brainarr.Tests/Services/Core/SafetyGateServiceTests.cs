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
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class SafetyGateServiceTests : IDisposable
    {
        private readonly SafetyGateService _service;
        private readonly Logger _logger;
        private readonly string _tempDir;
        private readonly ReviewQueueService _reviewQueue;
        private readonly RecommendationHistory _history;
        private readonly Mock<IPerformanceMetrics> _metricsMock;

        public SafetyGateServiceTests()
        {
            _service = new SafetyGateService();
            _logger = LogManager.GetLogger("test");
            _tempDir = Path.Combine(Path.GetTempPath(), $"brainarr_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _reviewQueue = new ReviewQueueService(_logger, _tempDir);
            _history = new RecommendationHistory(_logger, _tempDir);
            _metricsMock = new Mock<IPerformanceMetrics>();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        #region Basic Filtering Tests

        [Fact]
        public void ApplySafetyGates_returns_empty_list_when_input_is_null()
        {
            var settings = CreateSettings();
            var result = _service.ApplySafetyGates(null, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);
            result.Should().BeEmpty();
        }

        [Fact]
        public void ApplySafetyGates_returns_empty_list_when_input_is_empty()
        {
            var settings = CreateSettings();
            var result = _service.ApplySafetyGates(new List<Recommendation>(), settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);
            result.Should().BeEmpty();
        }

        [Fact]
        public void ApplySafetyGates_passes_recommendations_meeting_minimum_confidence()
        {
            var settings = CreateSettings(minConfidence: 0.5);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.8, artistMbid: "mbid1", albumMbid: "mbid2"),
                CreateRecommendation("Artist2", "Album2", 0.6, artistMbid: "mbid3", albumMbid: "mbid4"),
                CreateRecommendation("Artist3", "Album3", 0.3, artistMbid: "mbid5", albumMbid: "mbid6"), // Below threshold
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().HaveCount(2);
            result.Should().Contain(r => r.Artist == "Artist1");
            result.Should().Contain(r => r.Artist == "Artist2");
            result.Should().NotContain(r => r.Artist == "Artist3");
        }

        [Fact]
        public void ApplySafetyGates_clamps_minimum_confidence_between_zero_and_one()
        {
            // Test negative confidence (should be clamped to 0)
            var settings1 = CreateSettings(minConfidence: -0.5);
            var recommendations1 = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.0, artistMbid: "mbid1", albumMbid: "mbid2"),
            };
            var result1 = _service.ApplySafetyGates(recommendations1, settings1, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);
            result1.Should().HaveCount(1); // 0.0 >= 0.0 (clamped from -0.5)

            // Test confidence > 1 (should be clamped to 1)
            var settings2 = CreateSettings(minConfidence: 1.5);
            var recommendations2 = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.99, artistMbid: "mbid1", albumMbid: "mbid2"),
            };
            var result2 = _service.ApplySafetyGates(recommendations2, settings2, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);
            result2.Should().BeEmpty(); // 0.99 < 1.0 (clamped from 1.5)
        }

        #endregion

        #region MBID Requirement Tests

        [Fact]
        public void ApplySafetyGates_filters_items_without_mbids_when_required_in_album_mode()
        {
            var settings = CreateSettings(requireMbids: true, recommendationMode: RecommendationMode.SpecificAlbums);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.8, artistMbid: "mbid1", albumMbid: "mbid2"), // Has both
                CreateRecommendation("Artist2", "Album2", 0.8, artistMbid: "mbid3", albumMbid: null), // Missing album MBID
                CreateRecommendation("Artist3", "Album3", 0.8, artistMbid: null, albumMbid: "mbid4"), // Missing artist MBID
                CreateRecommendation("Artist4", "Album4", 0.8, artistMbid: null, albumMbid: null), // Missing both
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().HaveCount(1);
            result.Should().Contain(r => r.Artist == "Artist1");
        }

        [Fact]
        public void ApplySafetyGates_filters_items_without_artist_mbid_when_required_in_artist_mode()
        {
            var settings = CreateSettings(requireMbids: true, recommendationMode: RecommendationMode.Artists);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", null, 0.8, artistMbid: "mbid1", albumMbid: null), // Has artist MBID
                CreateRecommendation("Artist2", null, 0.8, artistMbid: null, albumMbid: null), // Missing artist MBID
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().HaveCount(1);
            result.Should().Contain(r => r.Artist == "Artist1");
        }

        [Fact]
        public void ApplySafetyGates_does_not_filter_mbids_when_not_required()
        {
            var settings = CreateSettings(requireMbids: false);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.8, artistMbid: null, albumMbid: null),
                CreateRecommendation("Artist2", "Album2", 0.8, artistMbid: "mbid1", albumMbid: null),
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().HaveCount(2);
        }

        #endregion

        #region Review Queue Tests

        [Fact]
        public void ApplySafetyGates_queues_borderline_items_when_enabled()
        {
            var settings = CreateSettings(minConfidence: 0.7, queueBorderline: true);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("HighConf", "Album1", 0.9, artistMbid: "mbid1", albumMbid: "mbid2"),
                CreateRecommendation("LowConf", "Album2", 0.3, artistMbid: "mbid3", albumMbid: "mbid4"),
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().HaveCount(1);
            result.Should().Contain(r => r.Artist == "HighConf");

            // Check that low confidence item was queued
            var pending = _reviewQueue.GetPending();
            pending.Should().Contain(p => p.Artist == "LowConf");
        }

        [Fact]
        public void ApplySafetyGates_does_not_queue_items_when_queueBorderline_is_disabled()
        {
            var settings = CreateSettings(minConfidence: 0.7, queueBorderline: false);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("LowConf", "Album1", 0.3, artistMbid: "mbid1", albumMbid: "mbid2"),
            };

            _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            var pending = _reviewQueue.GetPending();
            pending.Should().BeEmpty();
        }

        #endregion

        #region Artist Mode Promotion Tests

        [Fact]
        public void ApplySafetyGates_promotes_artists_without_mbids_when_all_filtered_in_artist_mode()
        {
            var settings = CreateSettings(
                requireMbids: true,
                recommendationMode: RecommendationMode.Artists,
                maxRecommendations: 5,
                queueBorderline: true);

            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", null, 0.8, artistMbid: null, albumMbid: null),
                CreateRecommendation("Artist2", null, 0.7, artistMbid: null, albumMbid: null),
                CreateRecommendation("Artist3", null, 0.6, artistMbid: null, albumMbid: null),
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            // Should promote items since all were filtered
            result.Should().NotBeEmpty();
            result.Count.Should().BeLessThanOrEqualTo(5);

            // Verify metrics were recorded
            _metricsMock.Verify(m => m.RecordArtistModePromotions(It.IsAny<int>()), Times.AtLeastOnce);
        }

        [Fact]
        public void ApplySafetyGates_does_not_promote_in_album_mode_when_all_filtered()
        {
            var settings = CreateSettings(
                requireMbids: true,
                recommendationMode: RecommendationMode.SpecificAlbums,
                queueBorderline: true);

            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.8, artistMbid: null, albumMbid: null),
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().BeEmpty(); // No promotion in album mode
        }

        #endregion

        #region Review Approval Tests

        [Fact]
        public void ApplySafetyGates_processes_review_approval_keys()
        {
            // First, queue some items
            var settings1 = CreateSettings(minConfidence: 0.9, queueBorderline: true);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.5, artistMbid: "mbid1", albumMbid: "mbid2"),
            };
            _service.ApplySafetyGates(recommendations, settings1, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            // Verify item is pending
            _reviewQueue.GetPending().Should().Contain(p => p.Artist == "Artist1");

            // Now approve via ReviewApproveKeys
            var settings2 = CreateSettings(minConfidence: 0.9);
            settings2.ReviewApproveKeys = new[] { "Artist1|Album1" };

            var result = _service.ApplySafetyGates(new List<Recommendation>(), settings2, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().Contain(r => r.Artist == "Artist1" && r.Album == "Album1");
        }

        [Fact]
        public void ApplySafetyGates_ignores_invalid_approval_keys()
        {
            var settings = CreateSettings();
            settings.ReviewApproveKeys = new[] { "InvalidKey", "", null, "Only|One|Part|Too|Many" };

            // Should not throw
            var result = _service.ApplySafetyGates(new List<Recommendation>(), settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);
            result.Should().BeEmpty();
        }

        #endregion

        #region Cancellation Tests

        [Fact]
        public void ApplySafetyGates_respects_cancellation_token()
        {
            var settings = CreateSettings();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.8, artistMbid: "mbid1", albumMbid: "mbid2"),
                CreateRecommendation("Artist2", "Album2", 0.8, artistMbid: "mbid3", albumMbid: "mbid4"),
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, cts.Token);

            // Should return early due to cancellation
            result.Should().BeEmpty();
        }

        #endregion

        #region Combined Filter Tests

        [Fact]
        public void ApplySafetyGates_applies_both_confidence_and_mbid_filters()
        {
            var settings = CreateSettings(minConfidence: 0.6, requireMbids: true);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("HighConfWithMbid", "Album1", 0.8, artistMbid: "mbid1", albumMbid: "mbid2"), // Pass
                CreateRecommendation("HighConfNoMbid", "Album2", 0.8, artistMbid: null, albumMbid: null), // Fail (no MBID)
                CreateRecommendation("LowConfWithMbid", "Album3", 0.4, artistMbid: "mbid3", albumMbid: "mbid4"), // Fail (low conf)
                CreateRecommendation("LowConfNoMbid", "Album4", 0.4, artistMbid: null, albumMbid: null), // Fail (both)
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().HaveCount(1);
            result.Should().Contain(r => r.Artist == "HighConfWithMbid");
        }

        [Fact]
        public void ApplySafetyGates_handles_whitespace_only_mbids_as_missing()
        {
            var settings = CreateSettings(requireMbids: true);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.8, artistMbid: "   ", albumMbid: "mbid1"),
                CreateRecommendation("Artist2", "Album2", 0.8, artistMbid: "mbid2", albumMbid: "  "),
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().BeEmpty();
        }

        #endregion

        #region Null Safety Tests

        [Fact]
        public void ApplySafetyGates_handles_null_logger_gracefully()
        {
            var settings = CreateSettings();
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.8, artistMbid: "mbid1", albumMbid: "mbid2"),
            };

            // Should not throw with null logger
            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, null, _metricsMock.Object, CancellationToken.None);
            result.Should().HaveCount(1);
        }

        [Fact]
        public void ApplySafetyGates_handles_null_metrics_gracefully()
        {
            var settings = CreateSettings(
                requireMbids: true,
                recommendationMode: RecommendationMode.Artists,
                queueBorderline: true);

            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", null, 0.8, artistMbid: null, albumMbid: null),
            };

            // Should not throw with null metrics
            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, null, CancellationToken.None);
            result.Should().NotBeEmpty();
        }

        #endregion

        #region Wave 11C Audit Gap Coverage

        // The Wave 11C audit flagged three coverage gaps on SafetyGateService:
        //   1. Content flag handling: how the gate classifies numeric/string flags
        //      (Confidence, MBIDs) at boundary values (NaN, +Inf, whitespace).
        //   2. Rate-limit / downstream-failure handling: does the gate retry,
        //      surface, or swallow exceptions thrown by ReviewQueueService or
        //      RecommendationHistory?
        //   3. Payload validation edge cases: overlong strings, embedded
        //      delimiters in keys, malformed JSON-as-string in ReviewApproveKeys.
        //
        // The gate is a pure filter — no HTTP/network — so "rate-limit" maps to
        // "transient failure from a dependency". These tests pin behaviour rather
        // than enforcing a desired retry contract.

        [Fact]
        public void Gate_Quirk_NaN_Confidence_Without_Queue_Is_Filtered_Silently()
        {
            // BUG: NaN.CompareTo any double via >= returns false, so the gate
            // routes a NaN-confidence item to the queue path. That's defensible.
            // The quirk is that when QueueBorderlineItems is OFF the item is
            // silently dropped with no log entry distinguishing "NaN payload"
            // from a normal low-confidence drop — an LLM that emits NaN
            // (string "NaN" coerced by Newtonsoft) is indistinguishable from
            // one that legitimately returned 0.0.
            var settings = CreateSettings(minConfidence: 0.5, requireMbids: false, queueBorderline: false);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("NaNArtist", "NaNAlbum", double.NaN, artistMbid: "mbid1", albumMbid: "mbid2"),
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().BeEmpty("NaN confidence must never pass the safety gate");
            _reviewQueue.GetPending().Should().BeEmpty("queueBorderline=false drops the item without a queue write");
        }

        [Fact]
        public void Gate_Quirk_NaN_Confidence_With_Queue_Crashes_JsonFileStore_Write()
        {
            // BUG: When QueueBorderlineItems is ON and a recommendation carries
            // NaN confidence, the gate calls reviewQueue.Enqueue which feeds
            // System.Text.Json — which throws on NaN/Infinity unless
            // JsonNumberHandling.AllowNamedFloatingPointLiterals is set. The
            // gate does NOT wrap the Enqueue call in try/catch, so a single
            // malformed LLM response can abort the entire sync rather than
            // being routed to the review queue as the safety gate intends.
            // The store's JsonSerializerOptions live in Lidarr.Plugin.Common.
            var settings = CreateSettings(minConfidence: 0.5, requireMbids: false, queueBorderline: true);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("NaNArtist", "NaNAlbum", double.NaN, artistMbid: "mbid1", albumMbid: "mbid2"),
            };

            Action act = () => _service.ApplySafetyGates(
                recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            act.Should().Throw<ArgumentException>()
                .WithMessage("*positive and negative infinity*",
                    "BUG: NaN/Infinity confidence from an LLM crashes JsonFileStore serialization, aborting the gate run");
        }

        [Fact]
        public void Gate_ContentFlag_PositiveInfinity_Confidence_Passes_When_Threshold_Is_One()
        {
            // After MinConfidence is clamped to 1.0, +Infinity >= 1.0 evaluates to
            // true, so positive infinity is accepted. Documents the math contract.
            var settings = CreateSettings(minConfidence: 1.5 /* clamped to 1.0 */, requireMbids: false);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("InfArtist", "InfAlbum", double.PositiveInfinity, artistMbid: "mbid1", albumMbid: "mbid2"),
            };

            var result = _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().HaveCount(1);
            result.Should().Contain(r => r.Artist == "InfArtist");
        }

        [Fact]
        public void Gate_PayloadValidation_OverLong_And_Empty_Artist_Album_Do_Not_Crash_Preview_Log()
        {
            // The gate builds a debug log preview from up to 3 queued items by
            // string-joining "Artist - Album". Verify it survives 64KB strings
            // and entirely-empty payloads without throwing.
            var huge = new string('x', 64 * 1024);
            var settings = CreateSettings(minConfidence: 0.99, queueBorderline: true);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation(huge, huge, 0.1, artistMbid: "mbid1", albumMbid: "mbid2"),
                CreateRecommendation(string.Empty, string.Empty, 0.1, artistMbid: "mbid3", albumMbid: "mbid4"),
                CreateRecommendation("  ", null, 0.1, artistMbid: "mbid5", albumMbid: "mbid6"),
            };

            Action act = () => _service.ApplySafetyGates(recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            act.Should().NotThrow("overlong, empty, and whitespace artist/album payloads must not crash the borderline preview log");
            _reviewQueue.GetPending().Should().HaveCount(3, "all three borderline items should be queued regardless of payload size");
        }

        [Fact]
        public void Gate_ReviewApproveKeys_Artist_With_Embedded_Pipe_Is_Misparsed()
        {
            // BUG: ReviewApproveKeys are split on '|' with no escape mechanism,
            // so an artist whose name contains '|' (e.g. "AC|DC") cannot be
            // approved by its real key. The split takes parts[0]/parts[1] which
            // refer to "AC" / "DC" instead of "AC|DC" / "Highway". The queued
            // item is never approved, and pending count stays at 1.
            var settings = CreateSettings(minConfidence: 0.9, queueBorderline: true);

            // First, queue an item whose artist contains a pipe character.
            _service.ApplySafetyGates(
                new List<Recommendation>
                {
                    CreateRecommendation("AC|DC", "Highway", 0.5, artistMbid: "mbid1", albumMbid: "mbid2"),
                },
                settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            _reviewQueue.GetPending().Should().Contain(p => p.Artist == "AC|DC", "precondition: item must be queued");

            // Now attempt to approve via the natural-looking key.
            var approveSettings = CreateSettings(minConfidence: 0.9);
            approveSettings.ReviewApproveKeys = new[] { "AC|DC|Highway" };

            var result = _service.ApplySafetyGates(
                new List<Recommendation>(), approveSettings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().NotContain(r => r.Artist == "AC|DC",
                "BUG: pipe-delimited approval keys cannot escape '|' in artist or album names, so the real item is never approved");
            _reviewQueue.GetPending().Should().Contain(p => p.Artist == "AC|DC",
                "BUG: the original queued item remains pending because the split parsed 'AC' / 'DC' instead of 'AC|DC' / 'Highway'");
        }

        [Fact]
        public void Gate_ReviewApproveKeys_Whitespace_Only_Parts_Do_Not_Approve_Real_Items()
        {
            // A whitespace-only key like "   |   " splits into two non-empty
            // parts, so the gate calls SetStatus("   ", "   ", Accepted).
            // No matching key exists, SetStatus returns false, and no real
            // item is approved. Pins the "fails closed" behaviour.
            var queueSettings = CreateSettings(minConfidence: 0.9, queueBorderline: true);
            _service.ApplySafetyGates(
                new List<Recommendation>
                {
                    CreateRecommendation("RealArtist", "RealAlbum", 0.5, artistMbid: "mbid1", albumMbid: "mbid2"),
                },
                queueSettings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            var approveSettings = CreateSettings(minConfidence: 0.9);
            approveSettings.ReviewApproveKeys = new[] { "   |   ", "|", "   |" };

            var result = _service.ApplySafetyGates(
                new List<Recommendation>(), approveSettings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            result.Should().BeEmpty("whitespace-only key parts must never approve a real queued item");
            _reviewQueue.GetPending().Should().Contain(p => p.Artist == "RealArtist", "RealArtist remains pending");
        }

        [Fact]
        public void Gate_RateLimitAnalog_Propagates_Exception_When_ReviewQueue_Is_Null()
        {
            // The gate does NOT defensively null-check reviewQueue, and it does
            // NOT wrap reviewQueue.Enqueue / reviewQueue.SetStatus in try/catch.
            // A transient downstream failure (e.g. JsonFileStore rate-limited
            // on disk I/O, simulated here by passing a null queue) therefore
            // surfaces to the caller rather than being silently swallowed or
            // retried. Pins the "no swallow, no retry" contract.
            var settings = CreateSettings(minConfidence: 0.9, queueBorderline: true);
            var recommendations = new List<Recommendation>
            {
                CreateRecommendation("Artist1", "Album1", 0.1, artistMbid: "mbid1", albumMbid: "mbid2"),
            };

            Action act = () => _service.ApplySafetyGates(
                recommendations, settings, reviewQueue: null!, _history, _logger, _metricsMock.Object, CancellationToken.None);

            act.Should().Throw<NullReferenceException>(
                "the gate surfaces (does not swallow or retry) exceptions from its downstream queue; this pins behaviour the audit asked about");
        }

        [Fact]
        public void Gate_RateLimitAnalog_Swallows_History_Exception_During_ArtistMode_Promotion()
        {
            // The artist-mode promotion path explicitly catches Exception from
            // history.MarkAsAccepted (see SafetyGateService line 62-63). We
            // verify that contract by passing a null Artist on the promoted
            // recommendation — history.MarkAsAccepted then calls
            // artist.ToLowerInvariant() and throws NRE, which the gate
            // catches and logs as non-critical. The promotion must still
            // succeed and the result must still contain the item.
            var settings = CreateSettings(
                requireMbids: true,
                recommendationMode: RecommendationMode.Artists,
                maxRecommendations: 5,
                queueBorderline: true);

            var recommendations = new List<Recommendation>
            {
                // Null artist triggers NRE inside RecommendationHistory.MarkAsAccepted.
                CreateRecommendation(artist: null!, album: null, confidence: 0.8, artistMbid: null, albumMbid: null),
            };

            Action act = () => _service.ApplySafetyGates(
                recommendations, settings, _reviewQueue, _history, _logger, _metricsMock.Object, CancellationToken.None);

            // The history exception must be swallowed by the inner try/catch.
            // ReviewQueueService.Enqueue is the outer dependency — it tolerates
            // a null artist (lowercase of null|null builds key "|"), so the
            // queue path itself doesn't throw.
            act.Should().NotThrow("history exceptions inside artist-mode promotion must be swallowed as non-critical");
            _metricsMock.Verify(m => m.RecordArtistModePromotions(It.IsAny<int>()), Times.AtLeastOnce,
                "metrics recording happens AFTER the swallowed history failure, so promotion still completes");
        }

        #endregion

        #region Helper Methods

        private static BrainarrSettings CreateSettings(
            double minConfidence = 0.5,
            bool requireMbids = false,
            bool queueBorderline = false,
            RecommendationMode recommendationMode = RecommendationMode.SpecificAlbums,
            int maxRecommendations = 10)
        {
            return new BrainarrSettings
            {
                MinConfidence = minConfidence,
                RequireMbids = requireMbids,
                QueueBorderlineItems = queueBorderline,
                RecommendationMode = recommendationMode,
                MaxRecommendations = maxRecommendations
            };
        }

        private static Recommendation CreateRecommendation(
            string artist,
            string album,
            double confidence,
            string artistMbid = null,
            string albumMbid = null)
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

        #endregion
    }
}
