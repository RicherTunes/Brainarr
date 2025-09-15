using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
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
    public class SafetyGateServiceTests
    {
        private static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "brainarr-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        [Fact]
        public void ApplySafetyGates_queues_borderline_and_returns_pass()
        {
            var logger = LogManager.GetLogger("test");
            var svc = new SafetyGateService();
            var tmp = TempDir();
            var review = new ReviewQueueService(logger, tmp);
            var history = new RecommendationHistory(logger, tmp);
            var metrics = new PerformanceMetrics();
            var settings = new BrainarrSettings
            {
                RecommendationMode = RecommendationMode.SpecificAlbums,
                MinConfidence = 0.5,
                RequireMbids = false,
                QueueBorderlineItems = true,
                MaxRecommendations = 5
            };
            var enriched = new List<Recommendation>
            {
                new Recommendation { Artist = "ArtistA", Album = "A1", Confidence = 0.6, ArtistMusicBrainzId = "x", AlbumMusicBrainzId = "y" },
                new Recommendation { Artist = "ArtistB", Album = "B1", Confidence = 0.4 }
            };

            var result = svc.ApplySafetyGates(enriched, settings, review, history, logger, metrics, CancellationToken.None);

            result.Should().ContainSingle(r => r.Artist == "ArtistA");
            review.GetPending().Any(i => i.Artist == "ArtistB").Should().BeTrue();
        }

        [Fact]
        public void ApplySafetyGates_artist_mode_promotes_when_require_mbids_filters_all()
        {
            var logger = LogManager.GetLogger("test");
            var svc = new SafetyGateService();
            var tmp = TempDir();
            var review = new ReviewQueueService(logger, tmp);
            var history = new RecommendationHistory(logger, tmp);
            var metrics = new PerformanceMetrics();
            var settings = new BrainarrSettings
            {
                RecommendationMode = RecommendationMode.Artists,
                MinConfidence = 0.0,
                RequireMbids = true,
                QueueBorderlineItems = true,
                MaxRecommendations = 1
            };
            var enriched = new List<Recommendation>
            {
                new Recommendation { Artist = "ArtistOnlyNoMbid", Confidence = 0.9 }
            };

            var result = svc.ApplySafetyGates(enriched, settings, review, history, logger, metrics, CancellationToken.None);

            // Should promote one item despite missing MBIDs
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("ArtistOnlyNoMbid");
            // Metrics should reflect promotion event
            metrics.GetSnapshot().ArtistModePromotedRecommendations.Should().BeGreaterThan(0);
        }
    }
}

