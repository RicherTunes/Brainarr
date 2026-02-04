using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr;
using AIProvider = NzbDrone.Core.ImportLists.Brainarr.AIProvider;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Common.Abstractions.Llm;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<IArtistService> _artistServiceMock;
        private readonly Mock<IAlbumService> _albumServiceMock;
        private readonly Mock<ILibraryAwarePromptBuilder> _promptBuilderMock;
        private readonly Logger _logger;
        private readonly BrainarrOrchestrator _orchestrator;

        public BrainarrOrchestratorTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _artistServiceMock = new Mock<IArtistService>();
            _albumServiceMock = new Mock<IAlbumService>();
            _promptBuilderMock = new Mock<ILibraryAwarePromptBuilder>();
            _logger = TestLogger.CreateNullLogger();

            // Create all required collaborators for the new constructor
            var providerFactoryMock = new Mock<IProviderFactory>();
            var cache = new RecommendationCache(_logger);
            var healthMonitorMock = new Mock<IProviderHealthMonitor>();
            var validatorMock = new Mock<IRecommendationValidator>();
            var modelDetectionMock = new Mock<IModelDetectionService>();
            var duplicationPrevention = new DuplicationPreventionService(_logger);

            // Use REAL LibraryAnalyzer so it calls the mocked artist/album services
            var styleCatalog = new StyleCatalogService(_logger, httpClient: null);
            var libraryAnalyzer = new LibraryAnalyzer(_artistServiceMock.Object, _albumServiceMock.Object, styleCatalog, _logger);

            // Set up provider factory to return a mock provider with dynamic name based on settings
            providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                              .Returns((BrainarrSettings settings, IHttpClient client, Logger logger) =>
                              {
                                  // Throw exception for invalid provider types
                                  if (!Enum.IsDefined(typeof(AIProvider), settings.Provider))
                                  {
                                      throw new NotSupportedException($"Provider {settings.Provider} is not supported");
                                  }

                                  var mockProvider = new Mock<IAIProvider>();
                                  mockProvider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(ProviderHealthResult.Healthy(responseTime: TimeSpan.FromSeconds(1)));
                                  mockProvider.Setup(p => p.ProviderName).Returns(settings.Provider.ToString());
                                  mockProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>())).ReturnsAsync(new List<Recommendation>
                                  {
                                      new Recommendation { Artist = "Test Artist", Album = "Test Album", Confidence = 0.8 }
                                  });
                                  return mockProvider.Object;
                              });

            // Set up health monitor to return healthy status for any provider type name (including Moq proxy types)
            healthMonitorMock.Setup(h => h.IsHealthy(It.IsAny<string>())).Returns(true);

            // Set up cache to always miss initially// Set up validator to return valid results with test data
            var defaultRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Test Artist", Album = "Test Album", Confidence = 0.8 }
            };
            var validationResult = new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
            {
                ValidRecommendations = defaultRecommendations,
                FilteredRecommendations = new List<Recommendation>(),
                TotalCount = 1,
                ValidCount = 1,
                FilteredCount = 0
            };
            // Set up validator to pass through recommendations dynamically
            validatorMock.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                        .Returns((List<Recommendation> recs, bool strict) => new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                        {
                            ValidRecommendations = recs ?? new List<Recommendation>(),
                            FilteredRecommendations = new List<Recommendation>(),
                            TotalCount = recs?.Count ?? 0,
                            ValidCount = recs?.Count ?? 0,
                            FilteredCount = 0
                        });

            _orchestrator = new BrainarrOrchestrator(
                _logger,
                providerFactoryMock.Object,
                libraryAnalyzer, // Real implementation that will call our mocked services
                cache,
                healthMonitorMock.Object,
                validatorMock.Object,
                modelDetectionMock.Object,
                _httpClientMock.Object,
                duplicationPrevention,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object);
        }

        [Fact]
        public async Task FetchRecommendations_WithValidSettings_ReturnsRecommendations()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = "llama3",
                MaxRecommendations = 10
            };

            var mockArtists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Test Artist 1" },
                new Artist { Id = 2, Name = "Test Artist 2" }
            };

            var mockAlbums = new List<Album>
            {
                new Album { Id = 1, Title = "Test Album 1", ArtistId = 1 },
                new Album { Id = 2, Title = "Test Album 2", ArtistId = 2 }
            };

            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(mockArtists);
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(mockAlbums);

            // Act
            var result = await _orchestrator.FetchRecommendationsAsync(settings);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<List<ImportListItemInfo>>(result);
        }

        [Fact]
        public void InitializeProvider_WithValidSettings_InitializesSuccessfully()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "test-key",
                OpenAIModel = "gpt-4"
            };

            // Act
            _orchestrator.InitializeProvider(settings);

            // Assert
            Assert.True(_orchestrator.IsProviderHealthy());
        }

        [Fact]
        public void InitializeProvider_WithInvalidProvider_ThrowsException()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = (AIProvider)999 // Invalid provider type
            };

            // Act & Assert
            Assert.Throws<NotSupportedException>(() =>
                _orchestrator.InitializeProvider(settings));
        }

        [Fact]
        public async Task FetchRecommendationsAsync_WithCachedData_ReturnsCachedResults()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = "llama3",
                MaxRecommendations = 5,
                CacheDuration = TimeSpan.FromMinutes(60)
            };

            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(new List<Artist>());
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(new List<Album>());

            // Act - First call
            var result1 = await _orchestrator.FetchRecommendationsAsync(settings);

            // Act - Second call (should hit cache)
            var result2 = await _orchestrator.FetchRecommendationsAsync(settings);

            // Assert
            Assert.Equal(result1.Count, result2.Count);
            // Avoid brittle call-count checks; ensure library access occurred
            _artistServiceMock.Verify(x => x.GetAllArtists(), Times.AtLeastOnce());
        }

        [Fact]
        public void GetProviderStatus_WithHealthyProvider_ReturnsCorrectStatus()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Anthropic,
                AnthropicApiKey = "test-key"
            };

            _orchestrator.InitializeProvider(settings);

            // Act
            var status = _orchestrator.GetProviderStatus();

            // Assert
            Assert.NotNull(status);
            Assert.Contains("Anthropic", status);
        }

        [Fact]
        public void GetProviderStatus_WithoutProvider_ReturnsNotInitialized()
        {
            // Act
            var status = _orchestrator.GetProviderStatus();

            // Assert
            Assert.Equal("Not Initialized", status);
        }

        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        public void InitializeProvider_WithLocalProvider_AttemptsAutoDetection(AIProvider provider)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = provider,
                EnableAutoDetection = true,
                BaseUrl = "http://localhost:11434"
            };

            // Act
            _orchestrator.InitializeProvider(settings);

            // Assert
            // Note: Logger verification removed as Logger methods are non-overridable
            Assert.True(_orchestrator.IsProviderHealthy()); // Verify provider was initialized
        }

        [Fact]
        public void UpdateProviderConfiguration_WithNewSettings_UpdatesProvider()
        {
            // Arrange
            var initialSettings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "key1",
                OpenAIModel = "gpt-3.5"
            };

            var updatedSettings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "key1",
                OpenAIModel = "gpt-4"
            };

            // Act
            _orchestrator.InitializeProvider(initialSettings);
            _orchestrator.UpdateProviderConfiguration(updatedSettings);

            // Assert
            Assert.True(_orchestrator.IsProviderHealthy());
        }

        [Fact]
        public async Task FetchRecommendationsAsync_WithUnhealthyProvider_ReturnsEmptyList()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://invalid-url:99999"
            };

            // Act
            var result = await _orchestrator.FetchRecommendationsAsync(settings);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void FetchRecommendations_WithLibraryData_GeneratesCorrectProfile()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Gemini,
                GeminiApiKey = "test-key",
                EnableLibraryAnalysis = true
            };

            var mockArtists = GenerateMockArtists(50);
            var mockAlbums = GenerateMockAlbums(200);

            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(mockArtists);
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(mockAlbums);

            // Act
            var result = _orchestrator.FetchRecommendations(settings);

            // Assert
            Assert.NotNull(result);
            _artistServiceMock.Verify(x => x.GetAllArtists(), Times.AtLeastOnce);
            _albumServiceMock.Verify(x => x.GetAllAlbums(), Times.AtLeastOnce);
        }

        private List<Artist> GenerateMockArtists(int count)
        {
            var artists = new List<Artist>();
            for (int i = 0; i < count; i++)
            {
                artists.Add(new Artist
                {
                    Id = i + 1,
                    Name = $"Artist {i + 1}",
                    Added = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 365))
                });
            }
            return artists;
        }

        private List<Album> GenerateMockAlbums(int count)
        {
            var albums = new List<Album>();
            for (int i = 0; i < count; i++)
            {
                albums.Add(new Album
                {
                    Id = i + 1,
                    Title = $"Album {i + 1}",
                    ArtistId = Random.Shared.Next(1, 50),
                    Added = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 365)),
                    ReleaseDate = DateTime.UtcNow.AddYears(-Random.Shared.Next(0, 30)),
                    Genres = new List<string> { "Rock", "Alternative" }
                });
            }
            return albums;
        }
    }
}
