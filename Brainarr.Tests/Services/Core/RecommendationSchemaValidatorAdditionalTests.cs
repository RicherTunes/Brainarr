using System.Collections.Generic;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class RecommendationSchemaValidatorAdditionalTests
    {
        [Fact]
        public void Validate_Drops_Nulls_And_MissingArtist_Clamps_And_Counts_Trims()
        {
            var v = new RecommendationSchemaValidator(LogManager.CreateNullLogger());
            var list = new List<Recommendation?>
            {
                null,
                new Recommendation { Artist = "  ", Album = "x" },
                new Recommendation { Artist = "  Artist  ", Album = "  Album  ", Genre = " G ", Reason = " R ", Confidence = 2.0 }
            };
            var report = v.Validate(list!);
            report.TotalItems.Should().Be(3);
            report.DroppedItems.Should().Be(2);
            report.ClampedConfidences.Should().Be(1);
            report.TrimmedFields.Should().BeGreaterThan(0);
            report.Warnings.Should().Contain(w => w.Contains("Missing artist") || w.Contains("Null recommendation"));
        }
    }
}
