using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class OrchestratorStylesOptionsTests
    {
        private BrainarrOrchestrator MakeOrchestrator(List<Album> albums)
        {
            var logger = LogManager.GetCurrentClassLogger();
            var providerFactoryMock = new Mock<IProviderFactory>();
            var cacheMock = new Mock<IRecommendationCache>();
            var healthMonitorMock = new Mock<IProviderHealthMonitor>();
            var validatorMock = new Mock<IRecommendationValidator>();
            var modelDetectionMock = new Mock<IModelDetectionService>();
            var httpMock = new Mock<IHttpClient>();

            // LibraryAnalyzer using mocked album service
            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            albumService.Setup(x => x.GetAllAlbums()).Returns(albums);
            var libraryAnalyzer = new LibraryAnalyzer(artistService.Object, albumService.Object, logger);

            return new BrainarrOrchestrator(
                logger,
                providerFactoryMock.Object,
                libraryAnalyzer,
                cacheMock.Object,
                healthMonitorMock.Object,
                validatorMock.Object,
                modelDetectionMock.Object,
                httpMock.Object);
        }

        [Fact]
        public void Styles_GetOptions_Typeahead_Should_Return_Matches()
        {
            var orch = MakeOrchestrator(new List<Album>());
            var query = new Dictionary<string, string> { { "query", "prog" } };
            dynamic res = orch.HandleAction("styles/getoptions", query, new BrainarrSettings());
            var options = ((IEnumerable<dynamic>)res.options).ToList();
            options.Should().NotBeEmpty();
            options.Any(o => (string)o.value == "progressive-rock").Should().BeTrue();
        }

        [Fact]
        public void Styles_GetOptions_Default_Should_Reflect_Library_Coverage()
        {
            var albums = new List<Album>
            {
                new Album { Id = 1, ArtistId = 1, Title = "A", Genres = new List<string> { "Progressive Rock" } },
                new Album { Id = 2, ArtistId = 2, Title = "B", Genres = new List<string> { "Jazz" } },
                new Album { Id = 3, ArtistId = 3, Title = "C", Genres = new List<string> { "Jazz" } }
            };
            var orch = MakeOrchestrator(albums);
            var query = new Dictionary<string, string>();
            dynamic res = orch.HandleAction("styles/getoptions", query, new BrainarrSettings());
            var options = ((IEnumerable<dynamic>)res.options).ToList();
            options.Should().NotBeEmpty();
            // Jazz appears twice; Progressive Rock once
            var labels = options.Select(o => (string)o.name).ToList();
            labels.Any(l => l.StartsWith("Jazz", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
            labels.Any(l => l.StartsWith("Progressive Rock", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        }

        [Fact]
        public void Styles_Preview_Should_Return_Coverage_Counts()
        {
            var albums = new List<Album>
            {
                new Album { Id = 1, ArtistId = 1, Title = "A", Genres = new List<string> { "Progressive Rock" } },
                new Album { Id = 2, ArtistId = 2, Title = "B", Genres = new List<string> { "Jazz" } }
            };
            var orch = MakeOrchestrator(albums);
            var query = new Dictionary<string, string> { { "selected", "progressive-rock" } };
            dynamic res = orch.HandleAction("styles/preview", query, new BrainarrSettings());
            int albumsCount = (int)res.counts.albums;
            int totalAlbums = (int)res.total.albums;
            albumsCount.Should().BeGreaterThan(0);
            totalAlbums.Should().Be(albums.Count);
        }
    }
}
