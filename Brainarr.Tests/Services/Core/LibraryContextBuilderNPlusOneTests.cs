using System;
using System.Collections.Generic;
using Brainarr.Tests.Helpers;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Regression coverage tying together BOTH live-observed symptoms from the same session:
    /// <list type="bullet">
    /// <item>
    /// <c>System.OutOfMemoryException</c> from the <see cref="Album.ArtistId"/> N+1 lazy load
    /// (18/hour against an ~11,700-artist library).
    /// </item>
    /// <item>
    /// <c>Warn|Brainarr|Failed to get real library data, using fallback: Error parsing column 21
    /// (Links=[...])</c> — <see cref="LibraryContextBuilder.BuildProfile"/> wraps the whole
    /// profile build (including the same <c>Album.ArtistId</c> access, plus the
    /// <c>StyleContextBuilder</c> call that also touched it) in one try/catch, so ANY exception
    /// on that per-row lazy-load path — including a Dapper column-mapping fault on the
    /// <c>Links</c> column, which is populated by the very same per-row
    /// <c>ArtistRepository.Query()</c> the lazy load fires — discards ALL real library data and
    /// substitutes the tiny hardcoded fallback profile (100 fake artists, "Radiohead" etc).
    /// </item>
    /// </list>
    /// Both symptoms share one root cause: something on the profile-build path dereferences
    /// <c>Album.ArtistId</c> (== <c>Artist.Value.Id</c>) per album instead of using the
    /// already-materialized <c>ArtistMetadataId</c> plain column. Fixing the dereference removes
    /// both the memory blow-up and the exposure to that fragile per-row query.
    /// </summary>
    [Trait("Category", "Performance")]
    public class LibraryContextBuilderNPlusOneTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        private static (List<Artist> Artists, List<Album> Albums, LazyLoadCounter Counter) BuildLibrary(
            int artistCount, Func<Exception> failureFactory = null)
        {
            var counter = new LazyLoadCounter();
            var artists = new List<Artist>(artistCount);
            var albums = new List<Album>(artistCount);

            for (var i = 0; i < artistCount; i++)
            {
                var metadataId = i + 1;
                var artist = new Artist
                {
                    Id = metadataId,
                    ArtistMetadataId = metadataId,
                    Name = $"Artist {metadataId}",
                    Added = DateTime.UtcNow.AddDays(-i)
                };
                artists.Add(artist);

                albums.Add(new Album
                {
                    Id = metadataId,
                    Title = $"Album {metadataId}",
                    ArtistMetadataId = metadataId,
                    Genres = new List<string>(),
                    Artist = new RecordingArtistLazyLoaded(artist, counter, failureFactory)
                });
            }

            return (artists, albums, counter);
        }

        private static Mock<IStyleCatalogService> CreateEmptyStyleCatalogMock()
        {
            var mock = new Mock<IStyleCatalogService>();
            mock.Setup(s => s.Normalize(It.IsAny<IEnumerable<string>>()))
                .Returns(new HashSet<string>());
            return mock;
        }

        private static readonly Func<Exception> LinksColumnParseFailure = () =>
            new InvalidOperationException("Error parsing column 21 (Links=[{\"Url\":\"http://bad\"}])");

        [Theory]
        [InlineData(20)]  // below StyleContextBuilder's default parallelization threshold (sequential path)
        [InlineData(120)] // above it (parallel path)
        public void BuildProfile_WhenPerAlbumArtistLoadWouldFail_ReturnsRealProfile_NotFallback(int artistCount)
        {
            var (artists, albums, counter) = BuildLibrary(artistCount, LinksColumnParseFailure);

            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();
            artistServiceMock.Setup(a => a.GetAllArtists()).Returns(artists);
            albumServiceMock.Setup(a => a.GetAllAlbums()).Returns(albums);

            var styleCatalogMock = CreateEmptyStyleCatalogMock();
            var builder = new LibraryContextBuilder(_logger, styleCatalogMock.Object);

            var profile = builder.BuildProfile(artistServiceMock.Object, albumServiceMock.Object);

            // 0 => the per-row lazy load (and its simulated Links-parsing failure) was never
            // triggered, so BuildProfile never entered its catch-all fallback path.
            Assert.Equal(0, counter.Count);
            Assert.Equal(artistCount, profile.TotalArtists);
            Assert.Equal(artistCount, profile.TotalAlbums);
            Assert.NotEqual(100, profile.TotalArtists); // GetFallbackProfile() sentinel
            Assert.NotEqual(500, profile.TotalAlbums);  // GetFallbackProfile() sentinel
        }

        [Fact]
        public void BuildProfile_WithoutStyleCatalog_WhenPerAlbumArtistLoadWouldFail_ReturnsRealProfile()
        {
            var (artists, albums, counter) = BuildLibrary(25, LinksColumnParseFailure);

            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();
            artistServiceMock.Setup(a => a.GetAllArtists()).Returns(artists);
            albumServiceMock.Setup(a => a.GetAllAlbums()).Returns(albums);

            var builder = new LibraryContextBuilder(_logger, styleCatalog: null);

            var profile = builder.BuildProfile(artistServiceMock.Object, albumServiceMock.Object);

            Assert.Equal(0, counter.Count);
            Assert.Equal(25, profile.TotalArtists);
            Assert.Equal(25, profile.TotalAlbums);
        }

        [Fact]
        public void BuildProfile_TopArtistsByAlbumCount_ResolvesRealNames_WithoutLazyLoad()
        {
            var (artists, albums, counter) = BuildLibrary(10);

            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();
            artistServiceMock.Setup(a => a.GetAllArtists()).Returns(artists);
            albumServiceMock.Setup(a => a.GetAllAlbums()).Returns(albums);

            var builder = new LibraryContextBuilder(_logger, styleCatalog: null);

            var profile = builder.BuildProfile(artistServiceMock.Object, albumServiceMock.Object);

            Assert.Equal(0, counter.Count);
            Assert.Contains(profile.TopArtists, name => name.StartsWith("Artist "));
        }
    }
}
