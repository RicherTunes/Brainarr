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
    /// <summary>
    /// The confidence floor must only filter recommendations the model EXPLICITLY scored below it.
    /// Recommendations whose score was fabricated by the parser (model gave none) carry
    /// ConfidenceProvided=false and must NOT be silently dropped when the user raises the floor above
    /// the fabricated default — that was the "raise to 0.85 → zero results" cliff.
    /// </summary>
    public class SafetyGateConfidenceTests
    {
        private static List<Recommendation> RunGate(double floor, params Recommendation[] recs)
        {
            var logger = LogManager.GetCurrentClassLogger();
            var tmp = Path.Combine(Path.GetTempPath(), "brainarr-gate-" + Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new BrainarrSettings
                {
                    MinConfidence = floor,
                    RequireMbids = false,                       // isolate the confidence gate
                    QueueBorderlineItems = true,
                    RecommendationMode = RecommendationMode.SpecificAlbums
                };
                var gate = new SafetyGateService();
                return gate.ApplySafetyGates(
                    recs.ToList(), settings,
                    new ReviewQueueService(logger, tmp),
                    new RecommendationHistory(logger, tmp),
                    logger, new PerformanceMetrics(logger), CancellationToken.None);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public void HighFloor_DoesNotDrop_ScorelessRecommendation()
        {
            var unscored = new Recommendation { Artist = "Unscored", Album = "X", Confidence = 0.85, ConfidenceProvided = false };
            var pass = RunGate(0.9, unscored);
            pass.Should().Contain(r => r.Artist == "Unscored",
                "a recommendation the model didn't score must not be filtered by the floor");
        }

        [Fact]
        public void HighFloor_Drops_ExplicitlyLowScoredRecommendation()
        {
            var lowScored = new Recommendation { Artist = "LowScored", Album = "X", Confidence = 0.85, ConfidenceProvided = true };
            var pass = RunGate(0.9, lowScored);
            pass.Should().NotContain(r => r.Artist == "LowScored",
                "a model-scored item below the floor is correctly filtered");
        }

        [Fact]
        public void HighFloor_Keeps_ExplicitlyHighScoredRecommendation()
        {
            var highScored = new Recommendation { Artist = "HighScored", Album = "X", Confidence = 0.95, ConfidenceProvided = true };
            var pass = RunGate(0.9, highScored);
            pass.Should().Contain(r => r.Artist == "HighScored");
        }

        [Fact]
        public void MixedBatch_FloorFiltersOnlyExplicitlyLowScored()
        {
            var unscored = new Recommendation { Artist = "Unscored", Album = "X", Confidence = 0.85, ConfidenceProvided = false };
            var low = new Recommendation { Artist = "Low", Album = "X", Confidence = 0.6, ConfidenceProvided = true };
            var high = new Recommendation { Artist = "High", Album = "X", Confidence = 0.95, ConfidenceProvided = true };

            var pass = RunGate(0.9, unscored, low, high);

            pass.Select(r => r.Artist).Should().BeEquivalentTo(new[] { "Unscored", "High" });
        }

        [Fact]
        public void DefaultFloor_PassesEverything()
        {
            // At the 0.7 default, a fabricated-0.85 unscored item and a 0.7 scored item both pass.
            var unscored = new Recommendation { Artist = "Unscored", Album = "X", Confidence = 0.85, ConfidenceProvided = false };
            var scored = new Recommendation { Artist = "Scored", Album = "X", Confidence = 0.7, ConfidenceProvided = true };
            var pass = RunGate(0.7, unscored, scored);
            pass.Select(r => r.Artist).Should().BeEquivalentTo(new[] { "Unscored", "Scored" });
        }
    }
}
