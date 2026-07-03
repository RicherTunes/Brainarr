using System.Collections.Generic;
using System.Linq;
using Brainarr.Tests.Helpers;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Direct coverage for <see cref="StyleContextBuilder"/> (both the sequential and parallel
    /// aggregation paths), which used to resolve an album's artist for style-fallback purposes
    /// via <see cref="Album.ArtistId"/> -- the same per-album lazy-load hazard fixed in
    /// <c>DuplicateFilterService</c> and <c>LibraryContextBuilder</c>.
    /// </summary>
    [Trait("Category", "Performance")]
    public class StyleContextBuilderNPlusOneTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        private static Mock<IStyleCatalogService> CreateIdentityStyleCatalogMock()
        {
            var mock = new Mock<IStyleCatalogService>();
            mock.Setup(s => s.Normalize(It.IsAny<IEnumerable<string>>()))
                .Returns((IEnumerable<string> values) => new HashSet<string>(
                    values?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>(),
                    System.StringComparer.OrdinalIgnoreCase));
            return mock;
        }

        [Theory]
        [InlineData(10)]  // sequential path (below default parallelization threshold of 64)
        [InlineData(100)] // parallel path
        public void Build_AlbumWithNoGenres_FallsBackToArtistStyles_WithoutPerAlbumLazyLoad(int artistCount)
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
                    Metadata = new ArtistMetadata { Genres = new List<string> { "rock" } }
                };
                artists.Add(artist);

                // No Genres on the album -> ExtractAlbumStyles returns empty -> the builder must
                // fall back to the artist's styles, which used to require Album.ArtistId.
                albums.Add(new Album
                {
                    Id = metadataId,
                    Title = $"Album {metadataId}",
                    ArtistMetadataId = metadataId,
                    Genres = new List<string>(),
                    Artist = new RecordingArtistLazyLoaded(artist, counter)
                });
            }

            var options = new LibraryAnalyzerOptions(); // default ParallelizationThreshold = 64
            var sut = new StyleContextBuilder(CreateIdentityStyleCatalogMock().Object, options, _logger);

            var context = sut.Build(artists, albums);

            Assert.Equal(0, counter.Count);

            // Behavior preserved: every album should have inherited its artist's "rock" style.
            Assert.Equal(artistCount, context.AlbumStyles.Count);
            Assert.All(context.AlbumStyles.Values, styles => Assert.Contains("rock", styles));
        }

        [Fact]
        public void Build_AlbumWithOwnGenres_DoesNotNeedArtistFallback_AndStillAvoidsLazyLoad()
        {
            var counter = new LazyLoadCounter();
            var artist = new Artist { Id = 1, ArtistMetadataId = 1, Name = "Artist 1" };
            var album = new Album
            {
                Id = 1,
                Title = "Album 1",
                ArtistMetadataId = 1,
                Genres = new List<string> { "jazz" },
                Artist = new RecordingArtistLazyLoaded(artist, counter)
            };

            var options = new LibraryAnalyzerOptions();
            var sut = new StyleContextBuilder(CreateIdentityStyleCatalogMock().Object, options, _logger);

            var context = sut.Build(new List<Artist> { artist }, new List<Album> { album });

            Assert.Equal(0, counter.Count);
            Assert.True(context.AlbumStyles.TryGetValue(1, out var styles));
            Assert.Contains("jazz", styles);
        }
    }
}
