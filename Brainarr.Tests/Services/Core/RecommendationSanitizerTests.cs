using System;
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
        private static RecommendationSanitizer Create()
        {
            var logger = LogManager.GetCurrentClassLogger();
            return new RecommendationSanitizer(logger);
        }

        [Theory]
        [InlineData("<script>alert(1)</script>", "")] // stripped
        [InlineData("../etc/passwd", "")] // dangerous path removed
        [InlineData("AC/DC & Friends", "AC/DC &amp; Friends")] // amp encoded
        [InlineData("O\u0000asis", "Oasis")] // null byte removed
        public void SanitizeString_Removes_Malicious_Constructs(string input, string expected)
        {
            var s = Create();
            s.SanitizeString(input).Should().Be(expected);
        }

        [Fact]
        public void IsValidRecommendation_Rejects_Malicious_And_OutOfRange()
        {
            var s = Create();
            var bad = new Recommendation { Artist = "<img src=x onerror=alert(1)>", Album = "A" };
            s.IsValidRecommendation(bad).Should().BeFalse();

            var bad2 = new Recommendation { Artist = "Artist", Album = "A", Confidence = 1.5 };
            s.IsValidRecommendation(bad2).Should().BeFalse();
        }

        [Fact]
        public void IsValidRecommendation_Accepts_Reasonable_Item()
        {
            var s = Create();
            var ok = new Recommendation { Artist = "Radiohead", Album = "OK Computer", Confidence = 0.9 };
            s.IsValidRecommendation(ok).Should().BeTrue();
        }

        [Fact]
        public void SanitizeRecommendations_Filters_Malicious_And_Clamps_Confidence()
        {
            var s = Create();
            var list = new List<Recommendation>
            {
                new Recommendation { Artist = "<script>x</script>", Album = "A", Confidence = 0.2 },
                new Recommendation { Artist = "Muse", Album = "Absolution", Confidence = 2.0, Reason = "Great & classic" },
                new Recommendation { Artist = "../System32", Album = "B", Confidence = -1.0 },
            };

            var sanitized = s.SanitizeRecommendations(list);
            sanitized.Should().HaveCount(1);
            sanitized[0].Artist.Should().Be("Muse");
            sanitized[0].Album.Should().Be("Absolution");
            sanitized[0].Reason.Should().Be("Great &amp; classic");
            sanitized[0].Confidence.Should().BeInRange(0.0, 1.0);
        }
    }
}
