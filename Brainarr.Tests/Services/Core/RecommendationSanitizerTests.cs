using System.Collections.Generic;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class RecommendationSanitizerTests
    {
        [Fact]
        public void SanitizeRecommendations_filters_malicious_and_clamps_confidence()
        {
            var logger = LogManager.GetLogger("test");
            var sanitizer = new RecommendationSanitizer(logger);

            var list = new List<Recommendation>
            {
                new Recommendation { Artist = "<script>alert(1)</script>", Album = "A" }, // malicious -> filtered
                new Recommendation { Artist = " Valid ", Album = "  ", Confidence = 2.0, Reason = "ok" }, // clamped + trims
            };

            var result = sanitizer.SanitizeRecommendations(list);
            result.Count.Should().Be(1);
            result[0].Artist.Should().Be("Valid");
            result[0].Confidence.Should().BeLessOrEqualTo(1.0);
        }

        [Fact]
        public void IsValidRecommendation_checks_required_and_lengths()
        {
            var logger = LogManager.GetLogger("test");
            var sanitizer = new RecommendationSanitizer(logger);

            sanitizer.IsValidRecommendation(null).Should().BeFalse();
            sanitizer.IsValidRecommendation(new Recommendation { Artist = "" }).Should().BeFalse();
            sanitizer.IsValidRecommendation(new Recommendation { Artist = "A", Confidence = 0.5 }).Should().BeTrue();

            var longArtist = new string('a', 501);
            sanitizer.IsValidRecommendation(new Recommendation { Artist = longArtist }).Should().BeFalse();
        }
    }
}

