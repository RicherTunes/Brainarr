using System;
using System.Collections.Generic;
using System.Linq;
using Brainarr.Tests.Helpers;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Regression coverage for the two remaining <see cref="Album.ArtistId"/> N+1 lazy-load sites in
    /// <see cref="LibraryAnalyzer"/> (<c>AnalyzeCollectionDepth</c> and
    /// <c>GetTopArtistsByAlbumCount</c>), swept in the follow-up pass to the same
    /// <c>ArtistMetadataId</c> pattern the crash-path fix used. Reading <c>Album.ArtistId</c> per
    /// album fires a per-row <c>ArtistRepository.Query()</c> DB round trip (N+1 -> OOM at
    /// ~11,700-artist scale); both sites now group on the plain <c>ArtistMetadataId</c> column.
    /// </summary>
    [Trait("Category", "Performance")]
    public class LibraryAnalyzerNPlusOneTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        private static Mock<IStyleCatalogService> CreateEmptyStyleCatalogMock()
        {
            var mock = new Mock<IStyleCatalogService>();
            mock.Setup(s => s.Normalize(It.IsAny<IEnumerable<string>>()))
                .Returns(new HashSet<string>());
            return mock;
        }

        private static (List<Artist> Artists, List<Album> Albums, LazyLoadCounter Counter) BuildLibrary(
            int artistCount, int albumsPerArtist)
        {
            var counter = new LazyLoadCounter();
            var artists = new List<Artist>(artistCount);
            var albums = new List<Album>(artistCount * albumsPerArtist);

            var albumId = 1;
            for (var i = 0; i < artistCount; i++)
            {
                var metadataId = i + 1;
                var artist = new Artist
                {
                    Id = metadataId,
                    ArtistMetadataId = metadataId,
                    Name = $"Artist {metadataId}",
                    Added = DateTime.UtcNow.AddDays(-i),
                    Metadata = new ArtistMetadata { Name = $"Artist {metadataId}", Genres = new List<string>() }
                };
                artists.Add(artist);

                for (var j = 0; j < albumsPerArtist; j++)
                {
                    albums.Add(new Album
                    {
                        Id = albumId++,
                        Title = $"Album {metadataId}-{j}",
                        ArtistMetadataId = metadataId,
                        AlbumType = "Studio",
                        Genres = new List<string>(),
                        Artist = new RecordingArtistLazyLoaded(artist, counter)
                    });
                }
            }

            return (artists, albums, counter);
        }

        [Theory]
        [InlineData(50, 3)]
        [InlineData(200, 2)]
        public void AnalyzeLibrary_ResolvesTopArtistsAndCollectionDepth_WithoutPerAlbumLazyLoad(
            int artistCount, int albumsPerArtist)
        {
            var (artists, albums, counter) = BuildLibrary(artistCount, albumsPerArtist);

            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();
            artistServiceMock.Setup(a => a.GetAllArtists()).Returns(artists);
            albumServiceMock.Setup(a => a.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(
                artistServiceMock.Object, albumServiceMock.Object, CreateEmptyStyleCatalogMock().Object, _logger);

            var profile = analyzer.AnalyzeLibrary();

            // 0 => neither AnalyzeCollectionDepth nor GetTopArtistsByAlbumCount dereferenced
            // Album.ArtistId (would fire a per-row DB round trip per album).
            Assert.Equal(0, counter.Count);

            // Behavior preserved: real profile, not the hardcoded fallback (100 artists / 500 albums).
            Assert.Equal(artistCount, profile.TotalArtists);
            Assert.Equal(artistCount * albumsPerArtist, profile.TotalAlbums);
            Assert.NotEmpty(profile.TopArtists);
            Assert.All(profile.TopArtists, name => Assert.StartsWith("Artist ", name));
        }

        [Fact]
        public void AnalyzeLibrary_TopCollectedArtists_ReportRealArtistIdsAndCounts_WithoutLazyLoad()
        {
            var counter = new LazyLoadCounter();

            // Artist 7 (metadataId 7) is the clear top collector with 5 albums; others have 1.
            var artists = new List<Artist>();
            var albums = new List<Album>();
            var albumId = 1;
            for (var i = 1; i <= 10; i++)
            {
                var artist = new Artist
                {
                    Id = i,
                    ArtistMetadataId = i,
                    Name = $"Artist {i}",
                    Added = DateTime.UtcNow.AddDays(-i),
                    Metadata = new ArtistMetadata { Name = $"Artist {i}", Genres = new List<string>() }
                };
                artists.Add(artist);

                var count = i == 7 ? 5 : 1;
                for (var j = 0; j < count; j++)
                {
                    albums.Add(new Album
                    {
                        Id = albumId++,
                        Title = $"Album {i}-{j}",
                        ArtistMetadataId = i,
                        AlbumType = "Studio",
                        Genres = new List<string>(),
                        Artist = new RecordingArtistLazyLoaded(artist, counter)
                    });
                }
            }

            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();
            artistServiceMock.Setup(a => a.GetAllArtists()).Returns(artists);
            albumServiceMock.Setup(a => a.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(
                artistServiceMock.Object, albumServiceMock.Object, CreateEmptyStyleCatalogMock().Object, _logger);

            var profile = analyzer.AnalyzeLibrary();

            Assert.Equal(0, counter.Count);

            var topCollected = Assert.IsType<List<ArtistDepth>>(profile.Metadata["TopCollectedArtists"]);
            // ArtistDepth.ArtistId must still be the real Artist.Id (7), not the raw grouping key.
            Assert.Equal(7, topCollected[0].ArtistId);
            Assert.Equal(5, topCollected[0].AlbumCount);
        }
    }
}
