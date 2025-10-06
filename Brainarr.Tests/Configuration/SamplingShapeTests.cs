using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Xunit;
using NzbDrone.Core.ImportLists.Brainarr;

namespace Brainarr.Tests.Configuration
{
    public class SamplingShapeTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Configuration")]
        public void DefaultArtistDistributionMatchesExpected()
        {
            var shape = SamplingShape.Default;

            var similar = shape.GetArtistDistribution(DiscoveryMode.Similar);
            Assert.Equal(60, similar.TopPercent);
            Assert.Equal(30, similar.RecentPercent);
            Assert.Equal(10, similar.RandomPercent);

            var adjacent = shape.GetArtistDistribution(DiscoveryMode.Adjacent);
            Assert.Equal(45, adjacent.TopPercent);
            Assert.Equal(35, adjacent.RecentPercent);
            Assert.Equal(20, adjacent.RandomPercent);

            var exploratory = shape.GetArtistDistribution(DiscoveryMode.Exploratory);
            Assert.Equal(35, exploratory.TopPercent);
            Assert.Equal(40, exploratory.RecentPercent);
            Assert.Equal(25, exploratory.RandomPercent);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Configuration")]
        public void DefaultAlbumDistributionMatchesExpected()
        {
            var shape = SamplingShape.Default;

            var similar = shape.GetAlbumDistribution(DiscoveryMode.Similar);
            Assert.Equal(55, similar.TopPercent);
            Assert.Equal(30, similar.RecentPercent);
            Assert.Equal(15, similar.RandomPercent);

            var adjacent = shape.GetAlbumDistribution(DiscoveryMode.Adjacent);
            Assert.Equal(45, adjacent.TopPercent);
            Assert.Equal(35, adjacent.RecentPercent);
            Assert.Equal(20, adjacent.RandomPercent);

            var exploratory = shape.GetAlbumDistribution(DiscoveryMode.Exploratory);
            Assert.Equal(35, exploratory.TopPercent);
            Assert.Equal(40, exploratory.RecentPercent);
            Assert.Equal(25, exploratory.RandomPercent);
        }
    }
}
