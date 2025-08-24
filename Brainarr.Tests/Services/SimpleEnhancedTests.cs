using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Enhanced")]
    public class SimpleEnhancedTests
    {
        private readonly Mock<IArtistService> _artistServiceMock;
        private readonly Mock<IAlbumService> _albumServiceMock;
        private readonly Mock<Logger> _loggerMock;

        public SimpleEnhancedTests()
        {
            _artistServiceMock = new Mock<IArtistService>();
            _albumServiceMock = new Mock<IAlbumService>();
            _loggerMock = new Mock<Logger>();
        }

        [Fact]
        public void LibraryAnalyzer_ShouldIncludeGenreDistribution()
        {
            // Arrange
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Rock Artist", Metadata = new ArtistMetadata { Genres = new List<string> {"Rock"} }},
                new Artist { Id = 2, Name = "Jazz Artist", Metadata = new ArtistMetadata { Genres = new List<string> {"Jazz"} }}
            };
            var albums = new List<Album>
            {
                new Album { Id = 1, ArtistId = 1, Title = "Rock Album", AlbumType = "Studio" },
                new Album { Id = 2, ArtistId = 2, Title = "Jazz Album", AlbumType = "Studio" }
            };

            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistServiceMock.Object, _albumServiceMock.Object, _loggerMock.Object);

            // Act
            var profile = analyzer.AnalyzeLibrary();

            // Assert
            profile.Should().NotBeNull();
            profile.Metadata.Should().ContainKey("GenreDistribution");
            profile.Metadata.Should().ContainKey("CollectionStyle");
        }

        [Fact]
        public void PromptBuilder_ShouldIncludeDiscoveryModeTemplates()
        {
            // Arrange
            var promptBuilder = new LibraryAwarePromptBuilder(_loggerMock.Object);
            var profile = new LibraryProfile
            {
                TotalArtists = 10,
                TotalAlbums = 50,
                TopGenres = new Dictionary<string, int> { {"Rock", 5}, {"Jazz", 3} },
                TopArtists = new List<string> {"Artist 1", "Artist 2"},
                RecentlyAdded = new List<string> {"Recent Artist"},
                Metadata = new Dictionary<string, object>()
            };

            var artists = new List<Artist>();
            var albums = new List<Album>();
            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 10,
                Provider = AIProvider.OpenAI
            };

            // Act
            var prompt = promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            // Assert
            prompt.Should().NotBeNullOrEmpty();
            prompt.Should().Contain("music connoisseur");
            prompt.Should().Contain("EXACT SAME subgenres");
        }

        [Fact]
        public void PromptBuilder_ShouldIncludeSamplingStrategyPreamble()
        {
            // Arrange
            var promptBuilder = new LibraryAwarePromptBuilder(_loggerMock.Object);
            var profile = new LibraryProfile
            {
                TotalArtists = 5,
                TotalAlbums = 20,
                TopGenres = new Dictionary<string, int> { {"Rock", 3} },
                TopArtists = new List<string> {"Artist 1"},
                RecentlyAdded = new List<string>(),
                Metadata = new Dictionary<string, object>()
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Adjacent,
                SamplingStrategy = SamplingStrategy.Comprehensive,
                MaxRecommendations = 5,
                Provider = AIProvider.Anthropic
            };

            // Act
            var prompt = promptBuilder.BuildLibraryAwarePrompt(profile, new List<Artist>(), new List<Album>(), settings);

            // Assert
            prompt.Should().Contain("CONTEXT SCOPE: You have been provided with a highly detailed");
            prompt.Should().Contain("comprehensive analysis");
        }

        [Fact]
        public void WeightedGenreDistribution_ShouldCalculateCorrectly()
        {
            // Arrange
            var artists = new List<Artist>();
            for (int i = 0; i < 100; i++)
            {
                var genreName = i < 50 ? "Rock" : i < 70 ? "Jazz" : i < 85 ? "Electronic" : "Folk";
                artists.Add(new Artist 
                { 
                    Id = i + 1, 
                    Name = $"Artist {i+1}", 
                    Metadata = new ArtistMetadata { Genres = new List<string> { genreName } }
                });
            }

            var albums = artists.Select(a => new Album { Id = a.Id, ArtistId = a.Id, Title = $"Album by {a.Name}", AlbumType = "Studio" }).ToList();

            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistServiceMock.Object, _albumServiceMock.Object, _loggerMock.Object);

            // Act
            var profile = analyzer.AnalyzeLibrary();

            // Assert
            var genreDistribution = profile.Metadata["GenreDistribution"] as Dictionary<string, double>;
            genreDistribution["Rock"].Should().Be(50.0); // 50%
            genreDistribution["Jazz"].Should().Be(20.0); // 20%
            genreDistribution["Electronic"].Should().Be(15.0); // 15%
            genreDistribution["Folk"].Should().Be(15.0); // 15%
            
            // Verify significance levels exist
            genreDistribution.Should().ContainKey("Rock_significance");
            genreDistribution.Should().ContainKey("genre_diversity_score");
        }
    }
}