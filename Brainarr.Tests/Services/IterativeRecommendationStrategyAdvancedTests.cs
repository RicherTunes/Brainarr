
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class IterativeRecommendationStrategyAdvancedTests
    {
        private readonly Mock<IAIProvider> _mockProvider;
        private readonly Mock<LibraryAwarePromptBuilder> _mockPromptBuilder;
        private readonly Logger _logger;
        private readonly IterativeRecommendationStrategy _strategy;
        private readonly BrainarrSettings _settings;
        private readonly LibraryProfile _profile;
        private readonly List<Artist> _existingArtists;
        private readonly List<Album> _existingAlbums;

        public IterativeRecommendationStrategyAdvancedTests()
        {
            _mockProvider = new Mock<IAIProvider>();
            var mockAnalyzer = new Mock<ILibraryAnalyzer>();
            _mockPromptBuilder = new Mock<LibraryAwarePromptBuilder>(mockAnalyzer.Object);
            _logger = LogManager.GetCurrentClassLogger();
            _strategy = new IterativeRecommendationStrategy(_logger, _mockPromptBuilder.Object);

            _settings = new BrainarrSettings
            {
                MaxRecommendations = 10,
                Provider = AIProvider.Ollama
            };

            _profile = new LibraryProfile
            {
                TopGenres = new Dictionary<string, int> { { "Rock", 50 } },
                TopArtists = new List<string> { "Existing Artist" },
                TotalAlbums = 1,
                TotalArtists = 1
            };

            _existingArtists = new List<Artist> { new Artist { Name = "Existing Artist" } };
            _existingAlbums = new List<Album> { new Album { Title = "Existing Album", ArtistMetadata = new ArtistMetadata { Name = "Existing Artist" } } };

            _mockPromptBuilder.Setup(p => p.BuildLibraryAwarePrompt(
                It.IsAny<LibraryProfile>(),
                It.IsAny<List<Artist>>(),
                It.IsAny<List<Album>>(),
                It.IsAny<BrainarrSettings>(),
                It.IsAny<bool>()))
                .Returns("prompt");
        }

        [Fact]
        public async Task GetIterativeRecommendationsAsync_WithPersistentDuplicates_StopsAfterMaxIterations()
        {
            // Arrange
            var duplicateRecs = new List<Recommendation>
            {
                new Recommendation { Artist = "Existing Artist", Album = "Existing Album" }
            };
            _mockProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>())).ReturnsAsync(duplicateRecs);

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object, _profile, _existingArtists, _existingAlbums, _settings);

            // Assert
            Assert.Empty(result);
            _mockProvider.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.Exactly(3));
        }

        [Fact]
        public async Task GetIterativeRecommendationsAsync_WithDiminishingReturns_ExitsEarly()
        {
            // Arrange
            var callCount = 0;
            _mockProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        return new List<Recommendation>
                        {
                            new Recommendation { Artist = "Artist 1", Album = "Album 1" },
                            new Recommendation { Artist = "Artist 2", Album = "Album 2" },
                            new Recommendation { Artist = "Existing Artist", Album = "Existing Album" } // Duplicate
                        };
                    }
                    // Subsequent calls return only duplicates, triggering low success rate
                    return new List<Recommendation>
                    {
                        new Recommendation { Artist = "Existing Artist", Album = "Existing Album" }
                    };
                });

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object, _profile, _existingArtists, _existingAlbums, _settings);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal(2, callCount); // Should exit after the second call due to low success rate
        }

        [Fact]
        public async Task GetIterativeRecommendationsAsync_WhenProviderReturnsFewerThanRequested_HandlesGracefully()
        {
            // Arrange
            var callCount = 0;
            _mockProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // Return fewer than needed
                        return new List<Recommendation>
                        {
                            new Recommendation { Artist = "Artist 1", Album = "Album 1" }
                        };
                    }
                    // Subsequent call provides more
                    return new List<Recommendation>
                    {
                        new Recommendation { Artist = "Artist 2", Album = "Album 2" },
                        new Recommendation { Artist = "Artist 3", Album = "Album 3" }
                    };
                });

            _settings.MaxRecommendations = 3;

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object, _profile, _existingArtists, _existingAlbums, _settings);

            // Assert
            Assert.Equal(3, result.Count);
            Assert.Equal(2, callCount);
        }
    }
}
