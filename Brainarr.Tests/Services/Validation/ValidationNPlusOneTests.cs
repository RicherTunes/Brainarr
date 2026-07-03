using System;
using System.Collections.Generic;
using Brainarr.Tests.Helpers;
using Brainarr.Tests.Services.Core;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Validation
{
    /// <summary>
    /// Regression coverage for the two duplicate-check N+1 sites swept in the follow-up pass:
    /// <c>AdvancedDuplicateDetector</c> and <c>SimpleRecommendationValidator</c> each looked albums up
    /// per artist via <c>albums.Where(a =&gt; a.ArtistId == artist.Id)</c> -- a
    /// <see cref="Album.ArtistId"/> deref (per-row DB round trip) for every album, repeated per
    /// candidate artist. Both now group albums by the plain <c>ArtistMetadataId</c> column once.
    /// </summary>
    [Trait("Category", "Performance")]
    public class ValidationNPlusOneTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        private static (List<Artist> Artists, List<Album> Albums, LazyLoadCounter Counter) BuildLibrary()
        {
            var counter = new LazyLoadCounter();
            var artists = new List<Artist>();
            var albums = new List<Album>();

            // Target artist/album we will look up, plus filler so the per-album scan is non-trivial.
            var names = new[] { "Radiohead", "Pink Floyd", "Miles Davis", "The Beatles", "Aphex Twin" };
            var albumTitles = new[] { "OK Computer", "The Wall", "Kind of Blue", "Abbey Road", "Windowlicker" };

            for (var i = 0; i < names.Length; i++)
            {
                var metadataId = i + 1;
                var artist = new Artist
                {
                    Id = metadataId,
                    ArtistMetadataId = metadataId,
                    Name = names[i]
                };
                artists.Add(artist);

                albums.Add(new Album
                {
                    Id = metadataId,
                    Title = albumTitles[i],
                    ArtistMetadataId = metadataId,
                    Artist = new RecordingArtistLazyLoaded(artist, counter)
                });
            }

            return (artists, albums, counter);
        }

        [Fact]
        public async System.Threading.Tasks.Task AdvancedDuplicateDetector_FindsExistingAlbum_WithoutPerAlbumLazyLoad()
        {
            var (artists, albums, counter) = BuildLibrary();

            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            artistService.Setup(a => a.GetAllArtists()).Returns(artists);
            albumService.Setup(a => a.GetAllAlbums()).Returns(albums);

            var detector = new AdvancedDuplicateDetector(_logger, artistService.Object, albumService.Object);

            var found = await detector.IsAlreadyInLibraryAsync(
                new Recommendation { Artist = "Radiohead", Album = "OK Computer" });

            Assert.True(found);
            Assert.Equal(0, counter.Count);
        }

        [Fact]
        public async System.Threading.Tasks.Task SimpleRecommendationValidator_FindsExistingAlbum_WithoutPerAlbumLazyLoad()
        {
            var (artists, albums, counter) = BuildLibrary();

            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            artistService.Setup(a => a.GetAllArtists()).Returns(artists);
            albumService.Setup(a => a.GetAllAlbums()).Returns(albums);

            var validator = new SimpleRecommendationValidator(_logger, artistService.Object, albumService.Object);

            var found = await validator.IsAlreadyInLibraryAsync(
                new Recommendation { Artist = "Radiohead", Album = "OK Computer" });

            Assert.True(found);
            Assert.Equal(0, counter.Count);
        }
    }
}
