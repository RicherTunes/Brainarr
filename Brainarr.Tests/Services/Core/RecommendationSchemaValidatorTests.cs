using System.Collections.Generic;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationSchemaValidatorTests
    {
        [Fact]
        public void Validate_CountsDroppedClampedAndTrimmed()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var v = new RecommendationSchemaValidator(logger);
            var list = new List<Recommendation>
            {
                null,
                new Recommendation { Artist = "", Album = "X" }, // dropped
                new Recommendation { Artist = " A ", Album = " B ", Genre = " G ", Reason = " R ", Confidence = 1.5 }, // clamped+trim
                new Recommendation { Artist = "C", Album = "D", Confidence = -0.2 } // clamped
            };

            var report = v.Validate(list);
            Assert.Equal(4, report.TotalItems);
            Assert.Equal(2, report.DroppedItems); // null + missing artist
            Assert.True(report.ClampedConfidences >= 2);
            Assert.True(report.TrimmedFields >= 4);
        }
    }
}
