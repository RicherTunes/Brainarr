using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using Xunit;
using ValidationResult = NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult;
using Brainarr.Tests.Helpers;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Highly specific unit tests for BrainarrOrchestrator that focus on individual responsibilities.
    /// This addresses the tech lead's feedback about improving test specificity by testing
    /// single behaviors rather than broad integration scenarios.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Component", "Orchestrator")]
    public class BrainarrOrchestratorSpecificTests
    {
        private readonly Mock<IProviderFactory> _providerFactoryMock;
        private readonly Mock<ILibraryAnalyzer> _libraryAnalyzerMock;
        private readonly Mock<IRecommendationCache> _cacheMock;
        private readonly Mock<IProviderHealthMonitor> _healthMonitorMock;
        private readonly Mock<IRecommendationValidator> _validatorMock;
        private readonly Mock<IModelDetectionService> _modelDetectionMock;
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Logger _logger;
        private readonly Mock<IDuplicationPrevention> _duplicationPreventionMock;
        private readonly Mock<IBreakerRegistry> _breakerRegistryMock;
        private readonly BrainarrOrchestrator _orchestrator;

        public BrainarrOrchestratorSpecificTests()
        {
            _providerFactoryMock = new Mock<IProviderFactory>();
            _libraryAnalyzerMock = new Mock<ILibraryAnalyzer>();
            _cacheMock = new Mock<IRecommendationCache>();
            _healthMonitorMock = new Mock<IProviderHealthMonitor>();
            _validatorMock = new Mock<IRecommendationValidator>();
            _modelDetectionMock = new Mock<IModelDetectionService>();
            _httpClientMock = new Mock<IHttpClient>();
            _logger = TestLogger.CreateNullLogger();
            _duplicationPreventionMock = new Mock<IDuplicationPrevention>();
            _breakerRegistryMock = PassThroughBreakerRegistry.CreateMock();

            // Setup duplication prevention to pass through for unit tests
            _duplicationPreventionMock
                .Setup(d => d.PreventConcurrentFetch<IList<ImportListItemInfo>>(It.IsAny<string>(), It.IsAny<Func<Task<IList<ImportListItemInfo>>>>()))
                .Returns<string, Func<Task<IList<ImportListItemInfo>>>>((key, func) => func());

            _duplicationPreventionMock
                .Setup(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()))
                .Returns((List<ImportListItemInfo> items) => items);

            _duplicationPreventionMock
                .Setup(d => d.FilterPreviouslyRecommended(It.IsAny<List<ImportListItemInfo>>(), It.IsAny<ISet<string>>()))
                .Returns((List<ImportListItemInfo> items, ISet<string> _) => items);


            _orchestrator = new BrainarrOrchestrator(
                _logger,
                _providerFactoryMock.Object,
                _libraryAnalyzerMock.Object,
                _cacheMock.Object,
                _healthMonitorMock.Object,
                _validatorMock.Object,
                _modelDetectionMock.Object,
                _httpClientMock.Object,
                duplicationPrevention: null, // Use default DuplicationPreventionService instead of mock
                breakerRegistry: _breakerRegistryMock.Object,
                duplicateFilter: Mock.Of<IDuplicateFilterService>());
        }

        [Fact]
        public void InitializeProvider_WhenCalledWithValidSettings_CreatesProviderThroughFactory()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, OpenAIApiKey = "test-key" };
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.ProviderName).Returns("OpenAI");
            _providerFactoryMock.Setup(f => f.CreateProvider(settings, _httpClientMock.Object, _logger))
                              .Returns(mockProvider.Object);

            // Act
            _orchestrator.InitializeProvider(settings);

            // Assert - Verify ONLY the factory interaction
            _providerFactoryMock.Verify(f => f.CreateProvider(settings, _httpClientMock.Object, _logger), Times.Once);
        }

        [Fact]
        public void InitializeProvider_WhenProviderAlreadyInitialized_SkipsReinitialization()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Anthropic, AnthropicApiKey = "test-key" };
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.ProviderName).Returns("Anthropic");
            _providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                                .Returns(mockProvider.Object);

            // Act - Initialize twice with same settings
            _orchestrator.InitializeProvider(settings);
            _orchestrator.InitializeProvider(settings);

            // Assert - With provider type tracking, re-initialization is skipped
            _providerFactoryMock.Verify(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()), Times.Once);
        }

        [Fact]
        public void IsProviderHealthy_WithNoProvider_ReturnsFalse()
        {
            // Act
            var result = _orchestrator.IsProviderHealthy();

            // Assert - Simple, focused assertion
            Assert.False(result);
        }

        [Fact]
        public void IsProviderHealthy_WithInitializedProvider_DelegatesHealthCheck()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Gemini, GeminiApiKey = "test-key" };
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.ProviderName).Returns("Gemini");

            _providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                              .Returns(mockProvider.Object);
            _healthMonitorMock.Setup(h => h.IsHealthy("Gemini")).Returns(true);

            _orchestrator.InitializeProvider(settings);

            // Act
            var result = _orchestrator.IsProviderHealthy();

            // Assert - Verify delegation to health monitor
            Assert.True(result);
            _healthMonitorMock.Verify(h => h.IsHealthy(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void GetProviderStatus_WithoutProvider_ReturnsExpectedMessage()
        {
            // Act
            var status = _orchestrator.GetProviderStatus();

            // Assert - Very specific string check
            Assert.Equal("Not Initialized", status);
        }

        [Fact]
        public void GetProviderStatus_WithHealthyProvider_ReturnsFormattedStatus()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Perplexity, PerplexityApiKey = "test-key" };
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.ProviderName).Returns("Perplexity");

            _providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                              .Returns(mockProvider.Object);
            _healthMonitorMock.Setup(h => h.IsHealthy("Perplexity")).Returns(true);

            _orchestrator.InitializeProvider(settings);

            // Act
            var status = _orchestrator.GetProviderStatus();

            // Assert - Verify exact format
            Assert.Equal("Perplexity: Healthy", status);
            _healthMonitorMock.Verify(h => h.IsHealthy("Perplexity"), Times.Once);
        }

        [Fact]
        public void ValidateConfiguration_WithInvalidConnection_AddsCorrectFailure()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenRouter, OpenRouterApiKey = "invalid-key" };
            var failures = new List<ValidationFailure>();

            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(false);
            _providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                              .Returns(mockProvider.Object);

            // Act
            _orchestrator.ValidateConfiguration(settings, failures);

            // Assert - Very specific failure validation
            Assert.Single(failures);
            Assert.Equal("Provider", failures[0].PropertyName);
            Assert.Equal("Unable to connect to AI provider", failures[0].ErrorMessage);
        }

        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        public void ValidateConfiguration_WithLocalProviderNoModels_AddsModelFailure(AIProvider provider)
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = provider };
            if (provider == AIProvider.Ollama) settings.OllamaUrl = "http://localhost:11434";
            if (provider == AIProvider.LMStudio) settings.LMStudioUrl = "http://localhost:1234";

            var failures = new List<ValidationFailure>();

            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);
            _providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                              .Returns(mockProvider.Object);

            // Set up model detection to return empty list
            _modelDetectionMock.Setup(m => m.GetOllamaModelsAsync(It.IsAny<string>())).ReturnsAsync(new List<string>());
            _modelDetectionMock.Setup(m => m.GetLMStudioModelsAsync(It.IsAny<string>())).ReturnsAsync(new List<string>());

            // Act
            _orchestrator.ValidateConfiguration(settings, failures);

            // Assert - Specific validation for model detection
            var modelFailure = failures.Find(f => f.PropertyName == "Model");
            Assert.NotNull(modelFailure);
            Assert.Contains("No models detected", modelFailure.ErrorMessage);
        }

        [Fact]
        public void ConvertToImportListItems_WithDuplicates_DeduplicatesResults()
        {
            // Arrange - This test verifies the deduplication logic directly
            // Instead of testing the full workflow, test the specific method responsible for deduplication
            var duplicatedRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Pink Floyd", Album = "The Wall", Year = 1979, Confidence = 0.9 },
                new Recommendation { Artist = "Pink Floyd", Album = "The Wall", Year = 1979, Confidence = 0.8 }, // Duplicate
                new Recommendation { Artist = "Pink Floyd", Album = "The Wall", Year = 1979, Confidence = 0.7 }, // Duplicate
                new Recommendation { Artist = "Led Zeppelin", Album = "IV", Year = 1971, Confidence = 0.9 },
                new Recommendation { Artist = "Led Zeppelin", Album = "IV", Year = 1971, Confidence = 0.85 }, // Duplicate
                new Recommendation { Artist = "The Beatles", Album = "Abbey Road", Year = 1969, Confidence = 0.95 },
                new Recommendation { Artist = "PINK FLOYD", Album = "THE WALL", Year = 1979, Confidence = 0.6 }, // Case-insensitive duplicate
                new Recommendation { Artist = " Pink Floyd ", Album = " The Wall ", Year = 1979, Confidence = 0.5 } // Whitespace duplicate
            };

            // Act - Test the deduplication logic directly via reflection or by creating a testable method
            // Since ConvertToImportListItems is private, we test via the DuplicationPreventionService
            var duplicationService = new DuplicationPreventionService(_logger);

            // Convert recommendations to ImportListItemInfo format first
            var importItems = duplicatedRecommendations.Select(r => new ImportListItemInfo
            {
                Artist = r.Artist,
                Album = r.Album,
                ReleaseDate = r.Year.HasValue ? new DateTime(r.Year.Value, 1, 1) : DateTime.MinValue
            }).ToList();

            var deduplicatedItems = duplicationService.DeduplicateRecommendations(importItems);

            // Assert - The deduplication should remove duplicates INCLUDING case variations
            // This correctly reduces 8 items to 3 unique artist/album combinations (case-insensitive)
            Assert.NotNull(deduplicatedItems);
            Assert.Equal(3, deduplicatedItems.Count); // 3 unique combinations after case-insensitive deduplication

            // Verify the unique artist/album combinations are present (case may vary)
            var normalizedPairs = deduplicatedItems.Select(r => $"{r.Artist.ToLowerInvariant()}|{r.Album?.ToLowerInvariant()}").ToList();
            Assert.Contains("pink floyd|the wall", normalizedPairs);
            Assert.Contains("led zeppelin|iv", normalizedPairs);
            Assert.Contains("the beatles|abbey road", normalizedPairs);
        }
    }
}
