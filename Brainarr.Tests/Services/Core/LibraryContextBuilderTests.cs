using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Xunit;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;

namespace Brainarr.Tests.Services.Core
{
    public class LibraryContextBuilderTests
    {
        private readonly Mock<IArtistService> _mockArtistService;
        private readonly Mock<IAlbumService> _mockAlbumService;
        private readonly Mock<Logger> _mockLogger;
        private readonly LibraryContextBuilder _builder;

        public LibraryContextBuilderTests()
        {
            _mockArtistService = new Mock<IArtistService>();
            _mockAlbumService = new Mock<IAlbumService>();
            _mockLogger = new Mock<Logger>();
            _builder = new LibraryContextBuilder(_mockLogger.Object);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildProfile_WithValidData_ReturnsCompleteProfile()
        {
            // Arrange
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Artist 1", Added = DateTime.UtcNow.AddDays(-1) },
                new Artist { Id = 2, Name = "Artist 2", Added = DateTime.UtcNow.AddDays(-2) },
                new Artist { Id = 3, Name = "Artist 3", Added = DateTime.UtcNow.AddDays(-3) }
            };

            var albums = new List<Album>
            {
                new Album { ArtistId = 1 },
                new Album { ArtistId = 1 },
                new Album { ArtistId = 2 },
                new Album { ArtistId = 3 }
            };

            _mockArtistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _mockAlbumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _builder.BuildProfile(_mockArtistService.Object, _mockAlbumService.Object);

            // Assert
            Assert.Equal(3, profile.TotalArtists);
            Assert.Equal(4, profile.TotalAlbums);
            Assert.NotEmpty(profile.TopArtists);
            Assert.NotEmpty(profile.TopGenres);
            Assert.NotEmpty(profile.RecentlyAdded);
            Assert.Contains("Artist 1", profile.TopArtists);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildProfile_WithEmptyLibrary_ReturnsEmptyProfile()
        {
            // Arrange
            _mockArtistService.Setup(s => s.GetAllArtists()).Returns(new List<Artist>());
            _mockAlbumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            // Act
            var profile = _builder.BuildProfile(_mockArtistService.Object, _mockAlbumService.Object);

            // Assert
            Assert.Equal(0, profile.TotalArtists);
            Assert.Equal(0, profile.TotalAlbums);
            Assert.Empty(profile.TopArtists);
            Assert.NotEmpty(profile.TopGenres); // Should have fallback genres
        }

        [Fact]
        [Trait("Category", "EdgeCase")]
        public void BuildProfile_WithServiceException_ReturnsFallbackProfile()
        {
            // Arrange
            _mockArtistService.Setup(s => s.GetAllArtists()).Throws(new Exception("Database error"));

            // Act
            var profile = _builder.BuildProfile(_mockArtistService.Object, _mockAlbumService.Object);

            // Assert
            Assert.Equal(100, profile.TotalArtists); // Fallback values
            Assert.Equal(500, profile.TotalAlbums);
            Assert.Contains("Radiohead", profile.TopArtists);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GenerateFingerprint_WithSameProfile_ReturnsSameFingerprint()
        {
            // Arrange
            var profile = new NzbDrone.Core.ImportLists.Brainarr.Models.LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = new Dictionary<string, int> { { "Rock", 30 }, { "Jazz", 20 } },
                TopArtists = new List<string> { "Artist1", "Artist2" },
                RecentlyAdded = new List<string> { "NewArtist1" }
            };

            // Act
            var fingerprint1 = _builder.GenerateFingerprint(profile);
            var fingerprint2 = _builder.GenerateFingerprint(profile);

            // Assert
            Assert.Equal(fingerprint1, fingerprint2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GenerateFingerprint_WithDifferentProfiles_ReturnsDifferentFingerprints()
        {
            // Arrange
            var profile1 = new NzbDrone.Core.ImportLists.Brainarr.Models.LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = new Dictionary<string, int> { { "Rock", 30 } },
                TopArtists = new List<string> { "Artist1" },
                RecentlyAdded = new List<string> { "NewArtist1" }
            };

            var profile2 = new NzbDrone.Core.ImportLists.Brainarr.Models.LibraryProfile
            {
                TotalArtists = 200,
                TotalAlbums = 1000,
                TopGenres = new Dictionary<string, int> { { "Jazz", 40 } },
                TopArtists = new List<string> { "Artist2" },
                RecentlyAdded = new List<string> { "NewArtist2" }
            };

            // Act
            var fingerprint1 = _builder.GenerateFingerprint(profile1);
            var fingerprint2 = _builder.GenerateFingerprint(profile2);

            // Assert
            Assert.NotEqual(fingerprint1, fingerprint2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildProfile_SortsTopArtistsByAlbumCount()
        {
            // Arrange
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Artist 1" },
                new Artist { Id = 2, Name = "Artist 2" },
                new Artist { Id = 3, Name = "Artist 3" }
            };

            var albums = new List<Album>
            {
                new Album { ArtistId = 1 },
                new Album { ArtistId = 2 },
                new Album { ArtistId = 2 },
                new Album { ArtistId = 3 },
                new Album { ArtistId = 3 },
                new Album { ArtistId = 3 }
            };

            _mockArtistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _mockAlbumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _builder.BuildProfile(_mockArtistService.Object, _mockAlbumService.Object);

            // Assert
            Assert.Equal("Artist 3", profile.TopArtists.First()); // Has most albums (3)
        }
    }
}