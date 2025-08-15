using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Xunit;
using NLog;
using NzbDrone.Core.Music;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Datastore;

namespace Brainarr.Tests.Services
{
    public class MusicBrainzResolverTests
    {
        private readonly Mock<ISearchForNewArtist> _artistSearchMock;
        private readonly Mock<ISearchForNewAlbum> _albumSearchMock;
        private readonly Mock<IArtistService> _artistServiceMock;
        private readonly Mock<IAlbumService> _albumServiceMock;
        private readonly Mock<Logger> _loggerMock;
        private readonly MusicBrainzResolver _resolver;

        public MusicBrainzResolverTests()
        {
            _artistSearchMock = new Mock<ISearchForNewArtist>();
            _albumSearchMock = new Mock<ISearchForNewAlbum>();
            _artistServiceMock = new Mock<IArtistService>();
            _albumServiceMock = new Mock<IAlbumService>();
            _loggerMock = new Mock<Logger>();
            
            _resolver = new MusicBrainzResolver(
                _artistSearchMock.Object,
                _albumSearchMock.Object,
                _artistServiceMock.Object,
                _albumServiceMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task ResolveRecommendation_ArtistAlreadyInLibrary_ReturnsAlreadyInLibrary()
        {
            // Arrange
            var existingArtist = new Artist { Name = "Pink Floyd" };
            _artistServiceMock.Setup(s => s.GetAllArtists())
                .Returns(new List<Artist> { existingArtist });

            var recommendation = new Recommendation
            {
                Artist = "Pink Floyd",
                Album = "The Wall"
            };

            // Act
            var result = await _resolver.ResolveRecommendation(recommendation);

            // Assert
            Assert.Equal(ResolutionStatus.AlreadyInLibrary, result.Status);
            Assert.Equal("Artist already in library", result.Reason);
        }

        [Fact]
        public async Task ResolveRecommendation_ArtistNotFoundInMusicBrainz_ReturnsNotFound()
        {
            // Arrange
            _artistServiceMock.Setup(s => s.GetAllArtists())
                .Returns(new List<Artist>());
            _artistSearchMock.Setup(s => s.SearchForNewArtist(It.IsAny<string>()))
                .Returns(new List<Artist>());

            var recommendation = new Recommendation
            {
                Artist = "NonExistentArtist",
                Album = "NonExistentAlbum"
            };

            // Act
            var result = await _resolver.ResolveRecommendation(recommendation);

            // Assert
            Assert.Equal(ResolutionStatus.NotFound, result.Status);
            Assert.Equal("Artist not found in MusicBrainz", result.Reason);
        }

        [Fact]
        public async Task ResolveRecommendation_VariousArtists_ReturnsInvalid()
        {
            // Arrange
            _artistServiceMock.Setup(s => s.GetAllArtists())
                .Returns(new List<Artist>());
            
            var variousArtist = new Artist 
            { 
                Name = "Various Artists",
                ForeignArtistId = "89ad4ac3-39f7-470e-963a-56509c546377"
            };
            
            _artistSearchMock.Setup(s => s.SearchForNewArtist(It.IsAny<string>()))
                .Returns(new List<Artist> { variousArtist });

            var recommendation = new Recommendation
            {
                Artist = "Compilation",
                Album = "Now That's What I Call Music"
            };

            // Act
            var result = await _resolver.ResolveRecommendation(recommendation);

            // Assert
            Assert.Equal(ResolutionStatus.Invalid, result.Status);
            Assert.Equal("Would map to Various Artists", result.Reason);
        }

        [Fact]
        public async Task ResolveRecommendation_ValidArtistWithAlbum_ReturnsResolved()
        {
            // Arrange
            _artistServiceMock.Setup(s => s.GetAllArtists())
                .Returns(new List<Artist>());
            
            var artist = new Artist 
            { 
                Name = "Radiohead",
                ForeignArtistId = "a74b1b7f-71a5-4011-9441-d0b5e4122711"
            };
            
            var album = new Album
            {
                Title = "OK Computer",
                ForeignAlbumId = "0a60e7d4-a38f-3b5e-9c5e-ad3cb91e2c5f"
            };
            
            _artistSearchMock.Setup(s => s.SearchForNewArtist("Radiohead"))
                .Returns(new List<Artist> { artist });
            
            _albumSearchMock.Setup(s => s.SearchForNewAlbum("OK Computer", "Radiohead"))
                .Returns(new List<Album> { album });

            var recommendation = new Recommendation
            {
                Artist = "Radiohead",
                Album = "OK Computer",
                Genre = "Alternative Rock",
                Confidence = 0.9
            };

            // Act
            var result = await _resolver.ResolveRecommendation(recommendation);

            // Assert
            Assert.Equal(ResolutionStatus.Resolved, result.Status);
            Assert.Equal(artist.ForeignArtistId, result.ArtistMbId);
            Assert.Equal(album.ForeignAlbumId, result.AlbumMbId);
            Assert.Equal("Radiohead", result.DisplayArtist);
            Assert.Equal("OK Computer", result.DisplayAlbum);
            Assert.True(result.Confidence > 0.5);
        }

        [Fact]
        public async Task ResolveRecommendation_ValidArtistWithoutAlbum_ReturnsResolvedWithLowerConfidence()
        {
            // Arrange
            _artistServiceMock.Setup(s => s.GetAllArtists())
                .Returns(new List<Artist>());
            
            var artist = new Artist 
            { 
                Name = "The Beatles",
                ForeignArtistId = "b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d"
            };
            
            _artistSearchMock.Setup(s => s.SearchForNewArtist("The Beatles"))
                .Returns(new List<Artist> { artist });
            
            _albumSearchMock.Setup(s => s.SearchForNewAlbum(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new List<Album>());

            var recommendation = new Recommendation
            {
                Artist = "The Beatles",
                Album = "Unknown Album"
            };

            // Act
            var result = await _resolver.ResolveRecommendation(recommendation);

            // Assert
            Assert.Equal(ResolutionStatus.Resolved, result.Status);
            Assert.Equal(artist.ForeignArtistId, result.ArtistMbId);
            Assert.Null(result.AlbumMbId);
            Assert.Equal("The Beatles", result.DisplayArtist);
            Assert.Equal("Unknown Album", result.DisplayAlbum);
            Assert.True(result.Confidence < 0.8); // Lower confidence without album match
        }

        [Fact]
        public async Task ResolveRecommendation_ExceptionThrown_ReturnsError()
        {
            // Arrange
            _artistServiceMock.Setup(s => s.GetAllArtists())
                .Throws(new Exception("Database error"));

            var recommendation = new Recommendation
            {
                Artist = "Test Artist",
                Album = "Test Album"
            };

            // Act
            var result = await _resolver.ResolveRecommendation(recommendation);

            // Assert
            Assert.Equal(ResolutionStatus.Error, result.Status);
            Assert.Equal("Database error", result.Reason);
        }

        [Theory]
        [InlineData("The Beatles", "beatles", true)]
        [InlineData("Pink Floyd", "pink floyd", true)]
        [InlineData("Nirvana", "Nirvana", true)]
        [InlineData("R.E.M.", "rem", true)]
        [InlineData("AC/DC", "acdc", true)]
        [InlineData("Guns N' Roses", "guns n roses", true)]
        public void FindBestArtistMatch_NormalizesNames_FindsMatch(string searchName, string artistName, bool shouldMatch)
        {
            // Arrange
            var artist = new Artist { Name = artistName };
            _artistSearchMock.Setup(s => s.SearchForNewArtist(searchName))
                .Returns(new List<Artist> { artist });

            // Act
            var result = _resolver.FindBestArtistMatch(searchName);

            // Assert
            if (shouldMatch)
            {
                Assert.NotNull(result);
                Assert.Equal(artistName, result.Name);
            }
            else
            {
                Assert.Null(result);
            }
        }

        [Fact]
        public void FindBestArtistMatch_WithCache_ReturnsCachedResult()
        {
            // Arrange
            var artist = new Artist { Name = "Cached Artist" };
            _artistSearchMock.Setup(s => s.SearchForNewArtist("Cached Artist"))
                .Returns(new List<Artist> { artist });

            // Act - First call
            var result1 = _resolver.FindBestArtistMatch("Cached Artist");
            // Second call should use cache
            var result2 = _resolver.FindBestArtistMatch("Cached Artist");

            // Assert
            Assert.Same(result1, result2);
            _artistSearchMock.Verify(s => s.SearchForNewArtist("Cached Artist"), Times.Once);
        }

        [Fact]
        public void FindBestAlbumMatch_FindsCorrectAlbum()
        {
            // Arrange
            var album = new Album 
            { 
                Title = "Dark Side of the Moon",
                ArtistMetadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { Name = "Pink Floyd" })
            };
            
            _albumSearchMock.Setup(s => s.SearchForNewAlbum("Dark Side of the Moon", "Pink Floyd"))
                .Returns(new List<Album> { album });

            // Act
            var result = _resolver.FindBestAlbumMatch("Pink Floyd", "Dark Side of the Moon");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Dark Side of the Moon", result.Title);
        }

        [Theory]
        [InlineData("Various Artists", true)]
        [InlineData("Compilation", true)]
        [InlineData("VA", true)]
        [InlineData("Radiohead", false)]
        [InlineData("The Beatles", false)]
        public async Task ResolveRecommendation_DetectsVariousArtistsVariants(string artistName, bool shouldReject)
        {
            // Arrange
            _artistServiceMock.Setup(s => s.GetAllArtists())
                .Returns(new List<Artist>());
            
            var artist = new Artist 
            { 
                Name = artistName,
                ForeignArtistId = shouldReject ? "89ad4ac3-39f7-470e-963a-56509c546377" : "valid-id"
            };
            
            _artistSearchMock.Setup(s => s.SearchForNewArtist(It.IsAny<string>()))
                .Returns(new List<Artist> { artist });

            var recommendation = new Recommendation
            {
                Artist = artistName,
                Album = "Test Album"
            };

            // Act
            var result = await _resolver.ResolveRecommendation(recommendation);

            // Assert
            if (shouldReject)
            {
                Assert.Equal(ResolutionStatus.Invalid, result.Status);
                Assert.Equal("Would map to Various Artists", result.Reason);
            }
            else
            {
                Assert.NotEqual(ResolutionStatus.Invalid, result.Status);
            }
        }

        [Fact]
        public async Task ResolveRecommendation_CalculatesConfidenceCorrectly()
        {
            // Arrange
            _artistServiceMock.Setup(s => s.GetAllArtists())
                .Returns(new List<Artist>());
            
            var artist = new Artist 
            { 
                Name = "Exact Match Artist",
                ForeignArtistId = "artist-id",
                Albums = new LazyLoaded<List<Album>>(new List<Album> { new Album() })
            };
            
            var album = new Album
            {
                Title = "Exact Match Album",
                ForeignAlbumId = "album-id",
                ArtistMetadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { Name = "Exact Match Artist" })
            };
            
            _artistSearchMock.Setup(s => s.SearchForNewArtist("Exact Match Artist"))
                .Returns(new List<Artist> { artist });
            
            _albumSearchMock.Setup(s => s.SearchForNewAlbum("Exact Match Album", "Exact Match Artist"))
                .Returns(new List<Album> { album });

            var recommendation = new Recommendation
            {
                Artist = "Exact Match Artist",
                Album = "Exact Match Album"
            };

            // Act
            var result = await _resolver.ResolveRecommendation(recommendation);

            // Assert
            Assert.Equal(ResolutionStatus.Resolved, result.Status);
            Assert.True(result.Confidence > 0.9, $"Expected confidence > 0.9, got {result.Confidence}");
        }
    }
}