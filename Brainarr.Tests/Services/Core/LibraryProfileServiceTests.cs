using System.Collections.Generic;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class LibraryProfileServiceTests
    {
        private LibraryProfileService CreateSut()
        {
            var artistSvc = new Mock<IArtistService>();
            var albumSvc = new Mock<IAlbumService>();
            Logger logger = TestLogger.CreateNullLogger();
            var builder = new LibraryContextBuilder(logger);
            return new LibraryProfileService(builder, logger, artistSvc.Object, albumSvc.Object);
        }

        [Fact]
        public void GenerateLibraryFingerprint_ProducesStableString()
        {
            var sut = CreateSut();
            var profile = new LibraryProfile
            {
                TotalArtists = 10,
                TotalAlbums = 200,
                TopGenres = new Dictionary<string, int>
                {
                    ["rock"] = 50,
                    ["pop"] = 30,
                    ["jazz"] = 20
                },
                TopArtists = new List<string> { "A", "B", "C" },
                RecentlyAdded = new List<string> { "X", "Y" }
            };

            var fp = sut.GenerateLibraryFingerprint(profile);
            fp.Should().NotBeNullOrEmpty();
            fp.Length.Should().BeGreaterThan(10);
        }

        [Fact]
        public void DetermineListeningTrends_ReturnsExpectedTags()
        {
            var sut = CreateSut();
            var profile = new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 600, // Avid Collector
                TopGenres = new Dictionary<string, int>
                {
                    ["rock"] = 100,
                    ["pop"] = 90,
                    ["jazz"] = 80,
                    ["metal"] = 70,
                    ["electronic"] = 60,
                    ["classical"] = 50
                },
                TopArtists = new List<string>(),
                RecentlyAdded = new List<string>()
            };

            var trends = sut.DetermineListeningTrends(profile);
            trends.Should().Contain("Avid Collector");
            trends.Should().Contain("Genre Explorer");
        }
    }
}
