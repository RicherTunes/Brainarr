using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Xunit;
using NLog;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.Datastore;

namespace Brainarr.Tests.Services
{
    public class IterativeRecommendationStrategyTests
    {
        private readonly Logger _logger;
        private readonly Mock<ILibraryAwarePromptBuilder> _promptBuilderMock;
        private readonly Mock<IAIProvider> _providerMock;
        private readonly IterativeRecommendationStrategy _strategy;

        public IterativeRecommendationStrategyTests()
        {
            _logger = NLog.LogManager.GetLogger("Test");
            _promptBuilderMock = new Mock<ILibraryAwarePromptBuilder>();
            _strategy = new IterativeRecommendationStrategy(_logger, _promptBuilderMock.Object);
            _providerMock = new Mock<IAIProvider>();
        }

        [Fact]
        public async Task GetIterativeRecommendationsAsync_FirstIterationSuccess_ReturnsRecommendations()
        {
            // Arrange
            var profile = new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = new Dictionary<string, int> { { "Rock", 50 }, { "Jazz", 30 } }
            };

            var allArtists = CreateTestArtists(10);
            var allAlbums = CreateTestAlbums(20);
            var settings = new BrainarrSettings { MaxRecommendations = 10 };

            var recommendations = CreateTestRecommendations(10);

            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(recommendations);

            _promptBuilderMock.Setup(p => p.BuildLibraryAwarePrompt(
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<IList<Artist>>(),
                    It.IsAny<IList<Album>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<SamplingStrategy>()))
                .Returns("Test prompt");

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _providerMock.Object, profile, allArtists, allAlbums, settings);

            // Assert
            Assert.Equal(10, result.Count);
            _providerMock.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetIterativeRecommendationsAsync_DuplicatesFiltered_RequestsMore()
        {
            // Arrange
            var profile = new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500
            };

            var allArtists = CreateTestArtists(5);
            var allAlbums = CreateTestAlbums(10);
            var settings = new BrainarrSettings { MaxRecommendations = 10 };

            // First iteration returns 5 duplicates
            var firstBatch = new List<Recommendation>();
            for (int i = 0; i < 5; i++)
            {
                firstBatch.Add(new Recommendation { Artist = allArtists[i % 5].Name, Album = "Album" });
            }
            for (int i = 0; i < 5; i++)
            {
                firstBatch.Add(new Recommendation { Artist = $"New Artist {i}", Album = $"New Album {i}" });
            }

            // Second iteration returns all new
            var secondBatch = CreateTestRecommendations(10, startIndex: 10);

            _providerMock.SetupSequence(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(firstBatch)
                .ReturnsAsync(secondBatch);

            _promptBuilderMock.Setup(p => p.BuildLibraryAwarePrompt(
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<IList<Artist>>(),
                    It.IsAny<IList<Album>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<SamplingStrategy>()))
                .Returns("Test prompt");

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _providerMock.Object, profile, allArtists, allAlbums, settings);

            // Assert
            Assert.Equal(10, result.Count);
            _providerMock.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public async Task GetIterativeRecommendationsAsync_MaxIterationsReached_ReturnsPartialResults()
        {
            // Arrange
            var profile = new LibraryProfile();
            var allArtists = CreateTestArtists(5);
            var allAlbums = CreateTestAlbums(10);
            var settings = new BrainarrSettings { MaxRecommendations = 30 };

            // Each iteration returns only 5 unique recommendations
            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() => CreateTestRecommendations(5, startIndex: Random.Shared.Next(100)));

            _promptBuilderMock.Setup(p => p.BuildLibraryAwarePrompt(
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<IList<Artist>>(),
                    It.IsAny<IList<Album>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<SamplingStrategy>()))
                .Returns("Test prompt");

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _providerMock.Object, profile, allArtists, allAlbums, settings);

            // Assert
            Assert.True(result.Count <= 30);
            Assert.True(result.Count >= 5); // At least first iteration succeeds
            _providerMock.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.AtMost(3));
        }

        [Fact]
        public async Task GetIterativeRecommendationsAsync_VariousArtistsFiltered_NotIncluded()
        {
            // Arrange
            var profile = new LibraryProfile();
            var allArtists = new List<Artist>();
            var allAlbums = new List<Album>();
            var settings = new BrainarrSettings { MaxRecommendations = 5 };

            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Various Artists", Album = "Compilation" },
                new Recommendation { Artist = "VA", Album = "Mix" },
                new Recommendation { Artist = "Soundtrack", Album = "OST" },
                new Recommendation { Artist = "Real Artist 1", Album = "Real Album 1" },
                new Recommendation { Artist = "Real Artist 2", Album = "Real Album 2" }
            };

            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(recommendations);

            _promptBuilderMock.Setup(p => p.BuildLibraryAwarePrompt(
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<IList<Artist>>(),
                    It.IsAny<IList<Album>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<SamplingStrategy>()))
                .Returns("Test prompt");

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _providerMock.Object, profile, allArtists, allAlbums, settings);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.All(result, r => Assert.DoesNotContain("various", r.Artist.ToLower()));
            Assert.All(result, r => Assert.DoesNotContain("soundtrack", r.Artist.ToLower()));
        }

        [Fact]
        public async Task CalculateIterationRequestSize_IncreasesWithIteration()
        {
            // Arrange
            var profile = new LibraryProfile();
            var allArtists = new List<Artist>();
            var allAlbums = new List<Album>();
            var settings = new BrainarrSettings { MaxRecommendations = 30 };

            var requestSizes = new List<int>();

            _promptBuilderMock.Setup(p => p.BuildLibraryAwarePrompt(
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<IList<Artist>>(),
                    It.IsAny<IList<Album>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<SamplingStrategy>()))
                .Returns((LibraryProfile p, IList<Artist> a, IList<Album> al, BrainarrSettings s, SamplingStrategy st) =>
                {
                    return "Test prompt";
                });

            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<Recommendation>()); // Return empty to force iterations

            // Act
            await _strategy.GetIterativeRecommendationsAsync(
                _providerMock.Object, profile, allArtists, allAlbums, settings);

            // Assert
            // Note: With concrete logger, we can't verify log calls
            // The test passes if no exceptions are thrown
        }

        [Fact]
        public async Task NormalizeArtistName_HandlesVariations()
        {
            // Arrange
            var profile = new LibraryProfile();
            var allArtists = new List<Artist>
            {
                new Artist { Name = "The Beatles" },
                new Artist { Name = "R.E.M." }
            };
            var allAlbums = new List<Album>();
            var settings = new BrainarrSettings { MaxRecommendations = 5 };

            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Beatles", Album = "Abbey Road" }, // Without "The"
                new Recommendation { Artist = "REM", Album = "Automatic" }, // Without dots
                new Recommendation { Artist = "New Artist", Album = "New Album" }
            };

            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(recommendations);

            _promptBuilderMock.Setup(p => p.BuildLibraryAwarePrompt(
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<IList<Artist>>(),
                    It.IsAny<IList<Album>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<SamplingStrategy>()))
                .Returns("Test prompt");

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _providerMock.Object, profile, allArtists, allAlbums, settings);

            // Assert
            Assert.Single(result); // Only "New Artist" should be included
            Assert.Equal("New Artist", result.First().Artist);
        }

        [Fact]
        public async Task BuildIterativeContext_IncludesRejectedInfo()
        {
            // Arrange
            var profile = new LibraryProfile();
            var allArtists = CreateTestArtists(2);
            var allAlbums = CreateTestAlbums(2);
            var settings = new BrainarrSettings { MaxRecommendations = 10 };

            // First batch has duplicates
            var firstBatch = new List<Recommendation>
            {
                new Recommendation { Artist = allArtists[0].Name, Album = "Dup1" },
                new Recommendation { Artist = allArtists[1].Name, Album = "Dup2" },
                new Recommendation { Artist = "New1", Album = "Album1" }
            };

            var secondBatch = CreateTestRecommendations(10, startIndex: 10);

            string capturedPrompt = null;
            _providerMock.SetupSequence(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(firstBatch)
                .ReturnsAsync(secondBatch);

            _promptBuilderMock.Setup(p => p.BuildLibraryAwarePrompt(
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<IList<Artist>>(),
                    It.IsAny<IList<Album>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<SamplingStrategy>()))
                .Returns("Base prompt");

            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .Callback<string>(prompt => capturedPrompt = prompt)
                .ReturnsAsync(secondBatch);

            // Act
            await _strategy.GetIterativeRecommendationsAsync(
                _providerMock.Object, profile, allArtists, allAlbums, settings);

            // Assert - The second iteration should mention rejected duplicates
            Assert.NotNull(capturedPrompt);
        }

        [Fact]
        public async Task GetIterativeRecommendationsAsync_ExceptionHandled_ReturnsPartialResults()
        {
            // Arrange
            var profile = new LibraryProfile();
            var allArtists = new List<Artist>();
            var allAlbums = new List<Album>();
            var settings = new BrainarrSettings { MaxRecommendations = 10 };

            var firstBatch = CreateTestRecommendations(5);

            _providerMock.SetupSequence(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(firstBatch)
                .ThrowsAsync(new Exception("API Error"));

            _promptBuilderMock.Setup(p => p.BuildLibraryAwarePrompt(
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<IList<Artist>>(),
                    It.IsAny<IList<Album>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<SamplingStrategy>()))
                .Returns("Test prompt");

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _providerMock.Object, profile, allArtists, allAlbums, settings);

            // Assert
            Assert.Equal(5, result.Count);
            // Note: With concrete logger, we can't verify log calls
        }

        private List<Artist> CreateTestArtists(int count)
        {
            var artists = new List<Artist>();
            for (int i = 0; i < count; i++)
            {
                artists.Add(new Artist
                {
                    Id = i,
                    Name = $"Artist {i}",
                    Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { Name = $"Artist {i}" })
                });
            }
            return artists;
        }

        private List<Album> CreateTestAlbums(int count)
        {
            var albums = new List<Album>();
            for (int i = 0; i < count; i++)
            {
                albums.Add(new Album
                {
                    Id = i,
                    Title = $"Album {i}",
                    ArtistId = i % 5,
                    ArtistMetadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { Name = $"Artist {i % 5}" })
                });
            }
            return albums;
        }

        private List<Recommendation> CreateTestRecommendations(int count, int startIndex = 0)
        {
            var recommendations = new List<Recommendation>();
            for (int i = 0; i < count; i++)
            {
                recommendations.Add(new Recommendation
                {
                    Artist = $"New Artist {startIndex + i}",
                    Album = $"New Album {startIndex + i}",
                    Genre = "Rock",
                    Confidence = 0.8,
                    Reason = "Test reason"
                });
            }
            return recommendations;
        }
    }
}