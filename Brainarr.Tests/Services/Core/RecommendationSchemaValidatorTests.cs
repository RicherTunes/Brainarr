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
        public void Validate_Computes_Report_With_Drops_Clamps_And_Trims()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var v = new RecommendationSchemaValidator(logger);

            var recs = new List<Recommendation>
            {
                null,
                new Recommendation { Artist = "  " }, // drop missing artist
                new Recommendation { Artist = "Tame Impala", Album = " Currents ", Confidence = 2.0 }, // clamp + trim
                new Recommendation { Artist = "Blur", Album = "13", Confidence = -0.5 }, // clamp
            };

            var report = v.Validate(recs);
            report.TotalItems.Should().Be(4);
            report.DroppedItems.Should().Be(2);
            report.ClampedConfidences.Should().Be(2);
            report.TrimmedFields.Should().BeGreaterThan(0);
            report.Warnings.Should().NotBeEmpty();
        }
    }
}
