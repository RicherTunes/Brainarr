using System.Collections.Generic;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class RecommendationSchemaValidatorTests
    {
        [Fact]
        public void Validate_counts_dropped_trimmed_and_clamped_correctly()
        {
            var logger = LogManager.GetLogger("test");
            var validator = new RecommendationSchemaValidator(logger);

            var list = new List<Recommendation>
            {
                null, // should be dropped
                new Recommendation { Artist = "   ", Album = "X" }, // missing artist -> dropped
                new Recommendation { Artist = " Artist ", Album = " Album ", Genre = " Genre ", Reason = " Reason ", Confidence = 2.0 },
                new Recommendation { Artist = "Valid", Confidence = -0.1 },
            };

            var report = validator.Validate(list);

            report.TotalItems.Should().Be(4);
            report.DroppedItems.Should().Be(2); // null + missing artist
            report.ClampedConfidences.Should().Be(2); // 2.0 and -0.1
            report.TrimmedFields.Should().BeGreaterThanOrEqualTo(1); // counted non-mutating trims
            report.Warnings.Should().Contain(w => w.Contains("Missing artist") || w.Contains("Null recommendation"));
        }
    }
}
