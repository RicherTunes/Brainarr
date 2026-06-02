
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
using System.Threading;

namespace Brainarr.Tests.Services
{
    public class IterativeRecommendationStrategyAdvancedTests
    {
        private readonly Mock<IAIProvider> _mockProvider;
        private readonly Mock<ILibraryAwarePromptBuilder> _mockPromptBuilder;
        private readonly Logger _logger;
        private readonly IterativeRecommendationStrategy _strategy;
        private readonly BrainarrSettings _settings;
        private readonly LibraryProfile _profile;
        private readonly List<Artist> _existingArtists;
        private readonly List<Album> _existingAlbums;

        public IterativeRecommendationStrategyAdvancedTests()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _mockProvider = new Mock<IAIProvider>();
            var mockAnalyzer = new Mock<ILibraryAnalyzer>();
            _mockPromptBuilder = new Mock<ILibraryAwarePromptBuilder>();
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
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
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
            // Strategy stops after 2 iterations when success rate is 0% (persistent duplicates)
            _mockProvider.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.Exactly(2));
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

        [Fact]
        public async Task GetIterativeRecommendationsAsync_TopUpTokenEstimate_UsesResolvedModelKeyNotDefault()
        {
            // The initial (main) prompt path resolves the tokenizer via the budget's ModelKey
            // (e.g. "ollama:test-model"), but the top-up loop historically called EstimateTokens(prompt)
            // with no model key, so it silently fell back to the "<default>" tokenizer — emitting a
            // misleading "no tokenizer registered for <default>" WARN and diverging from the main path's
            // tokenizer. The top-up must thread the same BudgetModelKey the metrics path computed.
            const string expectedModelKey = "ollama:test-model";

            _mockPromptBuilder
                .Setup(p => p.BuildLibraryAwarePromptWithMetrics(
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<List<Artist>>(),
                    It.IsAny<List<Album>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new LibraryPromptResult
                {
                    Prompt = "base prompt",
                    SampledArtists = 1,
                    SampledAlbums = 1,
                    BudgetModelKey = expectedModelKey
                });

            // Drive at least one full iteration (provider returns a usable rec) so all top-up
            // EstimateTokens diagnostics fire.
            _mockProvider
                .Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<Recommendation>
                {
                    new Recommendation { Artist = "New Artist", Album = "New Album" }
                });

            _settings.EnableDebugLogging = true; // gate for the Iteration Tokens / Summary diagnostics

            // Act
            await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object, _profile, _existingArtists, _existingAlbums, _settings);

            // Assert: the top-up estimated tokens against the resolved model key, never the
            // null/empty key that resolves to the "<default>" tokenizer.
            _mockPromptBuilder.Verify(
                p => p.EstimateTokens(It.IsAny<string>(), expectedModelKey),
                Times.AtLeastOnce(),
                "top-up token estimation must use the resolved BudgetModelKey, not the <default> tokenizer");
            _mockPromptBuilder.Verify(
                p => p.EstimateTokens(It.IsAny<string>(), null),
                Times.Never(),
                "top-up must not estimate tokens with a null model key (falls back to <default>)");
        }
    }
}
