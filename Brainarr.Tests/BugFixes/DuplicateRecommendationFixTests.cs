using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.BugFixes
{
    /// <summary>
    /// Test for the bug fix where artists were getting duplicated up to 8 times in Lidarr.
    /// The bug was caused by ConvertToImportListItems not deduplicating recommendations.
    /// </summary>
    [Trait("Category", "BugFix")]
    public class DuplicateRecommendationFixTests
    {
        private readonly Mock<IProviderFactory> _providerFactoryMock;
        private readonly Mock<ILibraryAnalyzer> _libraryAnalyzerMock;
        private readonly Mock<IRecommendationCache> _cacheMock;
        private readonly Mock<IProviderHealthMonitor> _healthMonitorMock;
        private readonly Mock<IRecommendationValidator> _validatorMock;
        private readonly Mock<IModelDetectionService> _modelDetectionMock;
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<Logger> _loggerMock;
        private readonly BrainarrOrchestrator _orchestrator;

        public DuplicateRecommendationFixTests()
        {
            _providerFactoryMock = new Mock<IProviderFactory>();
            _libraryAnalyzerMock = new Mock<ILibraryAnalyzer>();
            _cacheMock = new Mock<IRecommendationCache>();
            _healthMonitorMock = new Mock<IProviderHealthMonitor>();
            _validatorMock = new Mock<IRecommendationValidator>();
            _modelDetectionMock = new Mock<IModelDetectionService>();
            _httpClientMock = new Mock<IHttpClient>();
            _loggerMock = new Mock<Logger>();

            _orchestrator = new BrainarrOrchestrator(
                _loggerMock.Object,
                _providerFactoryMock.Object,
                _libraryAnalyzerMock.Object,
                _cacheMock.Object,
                _healthMonitorMock.Object,
                _validatorMock.Object,
                _modelDetectionMock.Object,
                _httpClientMock.Object);
        }

        [Fact]
        public async Task FetchRecommendationsAsync_WithDuplicateArtistsAndAlbums_DeduplicatesResults()
        {
            // Arrange - Simulate AI returning duplicate artist/album combinations
            var settings = new BrainarrSettings 
            { 
                Provider = AIProvider.OpenAI, 
                OpenAIApiKey = "test-key", 
                MaxRecommendations = 10 
            };
            
            var libraryProfile = new LibraryProfile 
            { 
                TotalArtists = 100, 
                TotalAlbums = 500 
            };
            
            // Create recommendations with duplicates (simulating the bug scenario)
            var duplicatedRecommendations = new List<Recommendation>
            {
                // Same artist/album appears 5 times with different confidence scores
                new Recommendation { Artist = "Pink Floyd", Album = "The Wall", Year = 1979, Confidence = 0.9 },
                new Recommendation { Artist = "Pink Floyd", Album = "The Wall", Year = 1979, Confidence = 0.8 },
                new Recommendation { Artist = "Pink Floyd", Album = "The Wall", Year = 1979, Confidence = 0.7 },
                new Recommendation { Artist = "PINK FLOYD", Album = "THE WALL", Year = 1979, Confidence = 0.6 }, // Case variation
                new Recommendation { Artist = " Pink Floyd ", Album = " The Wall ", Year = 1979, Confidence = 0.5 }, // Whitespace
                
                // Another duplicate pair
                new Recommendation { Artist = "Led Zeppelin", Album = "IV", Year = 1971, Confidence = 0.9 },
                new Recommendation { Artist = "Led Zeppelin", Album = "IV", Year = 1971, Confidence = 0.85 },
                new Recommendation { Artist = "led zeppelin", Album = "iv", Year = 1971, Confidence = 0.8 }, // Lowercase
                
                // Unique recommendation
                new Recommendation { Artist = "The Beatles", Album = "Abbey Road", Year = 1969, Confidence = 0.95 }
            };

            // Setup mocks
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                       .ReturnsAsync(duplicatedRecommendations);
            
            _providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                              .Returns(mockProvider.Object);
            
            _libraryAnalyzerMock.Setup(l => l.AnalyzeLibrary())
                              .Returns(libraryProfile);
            
            _libraryAnalyzerMock.Setup(l => l.BuildPrompt(It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<DiscoveryMode>()))
                              .Returns("test prompt");
            
            _healthMonitorMock.Setup(h => h.IsHealthy(It.IsAny<string>()))
                            .Returns(true);
            
            // Setup validator to pass all recommendations through
            var validationResult = new ValidationResult
            {
                ValidRecommendations = duplicatedRecommendations,
                FilteredRecommendations = new List<Recommendation>(),
                TotalCount = duplicatedRecommendations.Count,
                ValidCount = duplicatedRecommendations.Count,
                FilteredCount = 0
            };
            
            _validatorMock.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                        .Returns(validationResult);

            // Act
            var result = await _orchestrator.FetchRecommendationsAsync(settings);

            // Assert - Should have deduplicated to only unique artist/album combinations
            Assert.NotNull(result);
            Assert.Equal(3, result.Count); // Only 3 unique artist/album combinations
            
            // Verify each unique artist appears only once
            var artistAlbums = result.Select(r => $"{r.Artist}|{r.Album}").ToList();
            Assert.Equal(3, artistAlbums.Distinct().Count());
            
            // Verify the correct artists are present
            var artists = result.Select(r => r.Artist).ToList();
            Assert.Contains("Pink Floyd", artists);
            Assert.Contains("Led Zeppelin", artists);
            Assert.Contains("The Beatles", artists);
            
            // Note: Logger verification removed since NLog's Logger.Info isn't virtual
            // The deduplication logic is verified by the result count assertions above
        }

        [Fact]
        public async Task FetchRecommendationsAsync_WithNoDuplicates_ReturnsAllRecommendations()
        {
            // Arrange - All unique recommendations
            var settings = new BrainarrSettings 
            { 
                Provider = AIProvider.Anthropic, 
                AnthropicApiKey = "test-key" 
            };
            
            var uniqueRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Pink Floyd", Album = "The Wall", Year = 1979 },
                new Recommendation { Artist = "Pink Floyd", Album = "Dark Side of the Moon", Year = 1973 },
                new Recommendation { Artist = "Led Zeppelin", Album = "IV", Year = 1971 },
                new Recommendation { Artist = "The Beatles", Album = "Abbey Road", Year = 1969 }
            };

            // Setup mocks
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                       .ReturnsAsync(uniqueRecommendations);
            
            _providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                              .Returns(mockProvider.Object);
            
            _libraryAnalyzerMock.Setup(l => l.AnalyzeLibrary())
                              .Returns(new LibraryProfile());
            
            _libraryAnalyzerMock.Setup(l => l.BuildPrompt(It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<DiscoveryMode>()))
                              .Returns("test prompt");
            
            _healthMonitorMock.Setup(h => h.IsHealthy(It.IsAny<string>()))
                            .Returns(true);
            
            var validationResult = new ValidationResult
            {
                ValidRecommendations = uniqueRecommendations,
                FilteredRecommendations = new List<Recommendation>(),
                TotalCount = uniqueRecommendations.Count,
                ValidCount = uniqueRecommendations.Count,
                FilteredCount = 0
            };
            
            _validatorMock.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                        .Returns(validationResult);

            // Act
            var result = await _orchestrator.FetchRecommendationsAsync(settings);

            // Assert - All recommendations should be returned
            Assert.NotNull(result);
            Assert.Equal(4, result.Count);
            
            // Note: With no duplicates, no duplicate removal logging would occur
        }
    }
}