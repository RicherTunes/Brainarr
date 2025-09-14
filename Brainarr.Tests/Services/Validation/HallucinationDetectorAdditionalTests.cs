using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using Xunit;

namespace Brainarr.Tests.Services.Validation
{
    [Trait("Category", "Unit")]
    public class HallucinationDetectorAdditionalTests
    {
        private static HallucinationDetector Create() => new HallucinationDetector(LogManager.CreateNullLogger());

        [Fact]
        public async Task Detect_Flags_EmptyArtist_And_Sets_Confidence()
        {
            var det = Create();
            var rec = new Recommendation { Artist = string.Empty, Album = "Test Album" };
            var res = await det.DetectHallucinationAsync(rec);
            res.DetectedPatterns.Should().NotBeEmpty();
            res.HallucinationConfidence.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Detect_Flags_ImpossibleYear_And_SuspiciousName()
        {
            var det = Create();
            var rec = new Recommendation { Artist = "Xqwrtypsdfg", Album = "Alpha Demo Deluxe Edition", Year = System.DateTime.UtcNow.Year + 50 };
            var res = await det.DetectHallucinationAsync(rec);
            res.DetectedPatterns.Should().NotBeEmpty();
        }
    }
}
