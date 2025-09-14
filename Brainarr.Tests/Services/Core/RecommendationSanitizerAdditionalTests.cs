using System.Collections.Generic;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class RecommendationSanitizerAdditionalTests
    {
        private static RecommendationSanitizer Create() => new RecommendationSanitizer(LogManager.CreateNullLogger());

        [Fact]
        public void SanitizeString_Removes_Injection_Xss_Path_And_Normalizes()
        {
            var s = Create();
            var input = "\x00DROP TABLE Users; <script>alert(1)</script> ..\\etc/passwd Windows\\System32 name\" & value\n";
            var sanitized = s.SanitizeString(input);
            sanitized.Should().NotContain("DROP");
            sanitized.Should().NotContain("script");
            sanitized.Should().NotContain("..");
            sanitized.Should().NotContain("System32");
            sanitized.Should().NotContain("\"",
                "double quotes are removed");
            sanitized.Should().Contain("&amp;",
                "ampersand should be HTML-encoded");
            sanitized.Should().NotContain("\x00");
            sanitized.Should().NotContain("\n");
            sanitized.Should().NotContain("  ");
        }

        [Fact]
        public void SanitizeRecommendations_Filters_Malicious_Entries()
        {
            var s = Create();
            var list = new List<Recommendation>
            {
                new Recommendation { Artist = "<script>bad</script>", Album = "OK" },
                new Recommendation { Artist = "Good Artist", Album = "Good Album", Confidence = 1.2 }
            };
            var output = s.SanitizeRecommendations(list);
            output.Should().HaveCount(1);
            output[0].Artist.Should().Be("Good Artist");
            output[0].Album.Should().Be("Good Album");
            output[0].Confidence.Should().BeLessOrEqualTo(1.0);
        }

        [Fact]
        public void IsValidRecommendation_Rejects_Malicious_And_Oversized()
        {
            var s = Create();
            var bad = new Recommendation { Artist = "../hack" };
            s.IsValidRecommendation(bad).Should().BeFalse();

            var big = new Recommendation { Artist = new string('a', 501) };
            s.IsValidRecommendation(big).Should().BeFalse();
        }
    }
}
