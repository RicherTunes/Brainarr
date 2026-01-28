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
