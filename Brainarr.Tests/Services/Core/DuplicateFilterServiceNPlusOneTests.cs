using System.Collections.Generic;
using System.Linq;
using Brainarr.Tests.Helpers;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Regression coverage for the live-observed N+1 lazy-load OOM:
    /// <c>DuplicateFilterService.FilterExistingRecommendations</c> (and its sibling
    /// <c>FilterDuplicates</c>) used to read <see cref="Album.ArtistId"/> per album while
    /// building its artist-name lookup, which lazy-loads a full <c>Artist</c> from the DB
    /// on every unloaded album (see <see cref="RecordingArtistLazyLoaded"/> doc comment).
    /// Against the user's ~11,700-artist library this thrashed memory
    /// (18 <c>OutOfMemoryException</c>s/hour, live logs).
    /// </summary>
    [Trait("Category", "Performance")]
    public class DuplicateFilterServiceNPlusOneTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        private const int ArtistCount = 50;
        private const int AlbumsPerArtist = 40; // 2,000 albums total — large enough that an
                                                  // O(n) per-album DB round trip would be obviously wrong.

        private static (List<Artist> Artists, List<Album> Albums, LazyLoadCounter Counter) BuildLargeLibrary()
        {
            var counter = new LazyLoadCounter();
            var artists = new List<Artist>(ArtistCount);
            var albums = new List<Album>(ArtistCount * AlbumsPerArtist);

            for (var artistIdx = 0; artistIdx < ArtistCount; artistIdx++)
            {
                var artistMetadataId = artistIdx + 1;
                var artist = new Artist
                {
                    Id = artistMetadataId,
                    ArtistMetadataId = artistMetadataId,
                    Name = $"Artist {artistMetadataId}"
                };
                artists.Add(artist);

                for (var albumIdx = 0; albumIdx < AlbumsPerArtist; albumIdx++)
                {
                    var albumId = (artistIdx * AlbumsPerArtist) + albumIdx + 1;

                    // Deliberately do NOT set Album.ArtistId (its setter itself dereferences
                    // Artist.Value and would pre-load it, defeating the test). Only the plain
                    // ArtistMetadataId column + an UNLOADED Artist lazy-load stand-in are set,
                    // exactly matching what IAlbumService.GetAllAlbums() actually returns.
                    albums.Add(new Album
                    {
                        Id = albumId,
                        Title = $"Album {albumId}",
                        ArtistMetadataId = artistMetadataId,
                        Artist = new RecordingArtistLazyLoaded(artist, counter)
                    });
                }
            }

            return (artists, albums, counter);
        }

        private static DuplicateFilterService BuildSut(List<Artist> artists, List<Album> albums, Logger logger)
        {
            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();
            artistServiceMock.Setup(a => a.GetAllArtists()).Returns(artists);
            albumServiceMock.Setup(a => a.GetAllAlbums()).Returns(albums);

            return new DuplicateFilterService(artistServiceMock.Object, albumServiceMock.Object, logger);
        }

        [Fact]
        public void FilterExistingRecommendations_AlbumMode_WithLargeLibrary_DoesNotTriggerPerAlbumArtistLazyLoad()
        {
            var (artists, albums, counter) = BuildLargeLibrary();
            var sut = BuildSut(artists, albums, _logger);

            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Brand New Artist", Album = "Brand New Album", Confidence = 0.9 },
                new Recommendation { Artist = "Artist 3", Album = "Album 81", Confidence = 0.9 } // exists in library
            };

            var result = sut.FilterExistingRecommendations(recommendations, artistMode: false);

            Assert.Equal(0, counter.Count);
            Assert.Single(result);
            Assert.Equal("Brand New Artist", result[0].Artist);
        }

        [Fact]
        public void FilterExistingRecommendations_ArtistMode_WithLargeLibrary_DoesNotTriggerPerAlbumArtistLazyLoad()
        {
            var (artists, albums, counter) = BuildLargeLibrary();
            var sut = BuildSut(artists, albums, _logger);

            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Brand New Artist", Confidence = 0.9 },
                new Recommendation { Artist = "Artist 10", Confidence = 0.9 } // exists in library
            };

            var result = sut.FilterExistingRecommendations(recommendations, artistMode: true);

            Assert.Equal(0, counter.Count);
            Assert.Single(result);
            Assert.Equal("Brand New Artist", result[0].Artist);
        }

        [Fact]
        public void FilterDuplicates_WithLargeLibrary_DoesNotTriggerPerAlbumArtistLazyLoad()
        {
            var (artists, albums, counter) = BuildLargeLibrary();
            var sut = BuildSut(artists, albums, _logger);

            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Brand New Artist", Album = "Brand New Album" },
                new ImportListItemInfo { Artist = "Artist 25", Album = "Album 981" } // exists in library
            };

            var result = sut.FilterDuplicates(recommendations);

            Assert.Equal(0, counter.Count);
            Assert.Single(result);
            Assert.Equal("Brand New Artist", result[0].Artist);
        }

        [Fact]
        public void FilterExistingRecommendations_QueryCountIsIndependentOfAlbumCount()
        {
            // Prove O(1)-ish (not O(n)) behavior directly: doubling the album count must not
            // change the (zero) count of per-album Artist DB round trips.
            var (smallArtists, smallAlbums, smallCounter) = BuildLargeLibrary();
            var (largeArtists, largeAlbums, largeCounter) = BuildLargeLibrary();
            largeAlbums.AddRange(BuildLargeLibrary().Albums); // triple the album volume

            var smallSut = BuildSut(smallArtists, smallAlbums, _logger);
            var largeSut = BuildSut(largeArtists, largeAlbums, _logger);

            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Brand New Artist", Album = "Brand New Album", Confidence = 0.9 }
            };

            smallSut.FilterExistingRecommendations(recommendations, artistMode: false);
            largeSut.FilterExistingRecommendations(recommendations, artistMode: false);

            Assert.Equal(0, smallCounter.Count);
            Assert.Equal(0, largeCounter.Count);
            Assert.True(largeAlbums.Count >= smallAlbums.Count * 2);
        }
    }
}
