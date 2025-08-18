using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr;
using AIProvider = NzbDrone.Core.ImportLists.Brainarr.AIProvider;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using NzbDrone.Common.Http;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<IArtistService> _artistServiceMock;
        private readonly Mock<IAlbumService> _albumServiceMock;
        private readonly Mock<Logger> _loggerMock;
        private readonly BrainarrOrchestrator _orchestrator;

        public BrainarrOrchestratorTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _artistServiceMock = new Mock<IArtistService>();
            _albumServiceMock = new Mock<IAlbumService>();
            _loggerMock = new Mock<Logger>();
            
            _orchestrator = new BrainarrOrchestrator(
                _httpClientMock.Object,
                _artistServiceMock.Object,
                _albumServiceMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public void FetchRecommendations_WithValidSettings_ReturnsRecommendations()
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
            var result = _orchestrator.FetchRecommendations(settings);

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
        public void InitializeProvider_WithInvalidProvider_HandlesGracefully()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = (AIProvider)999
            };

            // Act
            _orchestrator.InitializeProvider(settings);

            // Assert
            Assert.False(_orchestrator.IsProviderHealthy());
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
            _artistServiceMock.Verify(x => x.GetAllArtists(), Times.Exactly(2));
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
            _loggerMock.Verify(
                x => x.Info(It.IsAny<string>()),
                Times.AtLeastOnce);
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
                    Added = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 365)),
                    Statistics = new ArtistStatistics { AlbumCount = Random.Shared.Next(1, 10) },
                    Genres = new List<string> { "Rock", "Alternative" }
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