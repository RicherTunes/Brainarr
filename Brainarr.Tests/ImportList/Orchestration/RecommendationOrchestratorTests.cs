using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;
using NLog;
using Brainarr.Plugin.ImportList.Orchestration;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace Brainarr.Tests.ImportList.Orchestration
{
    public class RecommendationOrchestratorTests
    {
        private readonly Mock<IRecommendationCache> _mockCache;
        private readonly Mock<IProviderHealthMonitor> _mockHealthMonitor;
        private readonly Mock<IRetryPolicy> _mockRetryPolicy;
        private readonly Mock<IRateLimiter> _mockRateLimiter;
        private readonly Mock<IAIProvider> _mockProvider;
        private readonly RecommendationOrchestrator _orchestrator;

        public RecommendationOrchestratorTests()
        {
            _mockCache = new Mock<IRecommendationCache>();
            _mockHealthMonitor = new Mock<IProviderHealthMonitor>();
            _mockRetryPolicy = new Mock<IRetryPolicy>();
            _mockRateLimiter = new Mock<IRateLimiter>();
            _mockProvider = new Mock<IAIProvider>();
            
            _orchestrator = new RecommendationOrchestrator(
                _mockCache.Object,
                _mockHealthMonitor.Object,
                _mockRetryPolicy.Object,
                _mockRateLimiter.Object,
                LogManager.GetCurrentClassLogger());
        }

        [Fact]
        public async Task GetRecommendationsAsync_ReturnsCachedResults_WhenCacheHit()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var libraryProfile = new LibraryProfile();
            var cachedRecommendations = new List<Recommendation> 
            { 
                new Recommendation { Artist = "Cached Artist", Album = "Cached Album" } 
            };
            
            _mockCache.Setup(x => x.GenerateCacheKey(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns("test-key");
            _mockCache.Setup(x => x.TryGet("test-key", out cachedRecommendations))
                .Returns(true);

            // Act
            var result = await _orchestrator.GetRecommendationsAsync(_mockProvider.Object, settings, libraryProfile);

            // Assert
            Assert.Equal(cachedRecommendations, result);
            _mockHealthMonitor.Verify(x => x.CheckHealthAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _mockProvider.Verify(x => x.GetRecommendationsAsync(It.IsAny<LibraryProfile>(), It.IsAny<BrainarrSettings>()), Times.Never);
        }

        [Fact]
        public async Task GetRecommendationsAsync_ReturnsEmpty_WhenProviderUnhealthy()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var libraryProfile = new LibraryProfile();
            List<Recommendation> cached;
            
            _mockCache.Setup(x => x.TryGet(It.IsAny<string>(), out cached))
                .Returns(false);
            _mockHealthMonitor.Setup(x => x.CheckHealthAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(HealthStatus.Unhealthy);

            // Act
            var result = await _orchestrator.GetRecommendationsAsync(_mockProvider.Object, settings, libraryProfile);

            // Assert
            Assert.Empty(result);
            _mockProvider.Verify(x => x.GetRecommendationsAsync(It.IsAny<LibraryProfile>(), It.IsAny<BrainarrSettings>()), Times.Never);
        }

        [Fact]
        public async Task GetRecommendationsAsync_FetchesAndCaches_WhenCacheMiss()
        {
            // Arrange
            var settings = new BrainarrSettings 
            { 
                Provider = AIProvider.OpenAI,
                CacheDurationMinutes = 60
            };
            var libraryProfile = new LibraryProfile();
            var recommendations = new List<Recommendation> 
            { 
                new Recommendation { Artist = "New Artist", Album = "New Album" } 
            };
            List<Recommendation> cached;
            
            _mockCache.Setup(x => x.TryGet(It.IsAny<string>(), out cached))
                .Returns(false);
            _mockHealthMonitor.Setup(x => x.CheckHealthAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(HealthStatus.Healthy);
            _mockProvider.Setup(x => x.ProviderName).Returns("TestProvider");
            
            _mockRateLimiter.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(), 
                    It.IsAny<Func<Task<List<Recommendation>>>>()))
                .ReturnsAsync(recommendations);

            // Act
            var result = await _orchestrator.GetRecommendationsAsync(_mockProvider.Object, settings, libraryProfile);

            // Assert
            Assert.Equal(recommendations, result);
            _mockCache.Verify(x => x.Set(
                It.IsAny<string>(), 
                recommendations, 
                It.Is<TimeSpan>(t => t.TotalMinutes == 60)), 
                Times.Once);
            _mockHealthMonitor.Verify(x => x.RecordSuccess(It.IsAny<string>(), It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendationsAsync_AppliesRateLimitingAndRetry()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var libraryProfile = new LibraryProfile();
            var recommendations = new List<Recommendation>();
            List<Recommendation> cached;
            
            _mockCache.Setup(x => x.TryGet(It.IsAny<string>(), out cached))
                .Returns(false);
            _mockHealthMonitor.Setup(x => x.CheckHealthAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(HealthStatus.Healthy);
            
            var rateLimitExecuted = false;
            var retryExecuted = false;
            
            _mockRateLimiter.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(), 
                    It.IsAny<Func<Task<List<Recommendation>>>>()))
                .Callback(() => rateLimitExecuted = true)
                .ReturnsAsync((string key, Func<Task<List<Recommendation>>> func) => func());
            
            _mockRetryPolicy.Setup(x => x.ExecuteAsync(
                    It.IsAny<Func<Task<List<Recommendation>>>>(),
                    It.IsAny<string>()))
                .Callback(() => retryExecuted = true)
                .ReturnsAsync(recommendations);

            // Act
            await _orchestrator.GetRecommendationsAsync(_mockProvider.Object, settings, libraryProfile);

            // Assert
            Assert.True(rateLimitExecuted);
            Assert.True(retryExecuted);
        }
    }
}