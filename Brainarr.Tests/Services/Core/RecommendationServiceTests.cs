using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.Services.Core
{
    [TestFixture]
    public class RecommendationServiceTests
    {
        private Mock<IRecommendationCache> _mockCache;
        private Mock<IProviderHealthMonitor> _mockHealthMonitor;
        private Mock<IRetryPolicy> _mockRetryPolicy;
        private Mock<IRateLimiter> _mockRateLimiter;
        private Mock<IRecommendationSanitizer> _mockSanitizer;
        private Mock<IterativeRecommendationStrategy> _mockIterativeStrategy;
        private Mock<Logger> _mockLogger;
        private RecommendationService _service;

        [SetUp]
        public void Setup()
        {
            _mockCache = new Mock<IRecommendationCache>();
            _mockHealthMonitor = new Mock<IProviderHealthMonitor>();
            _mockRetryPolicy = new Mock<IRetryPolicy>();
            _mockRateLimiter = new Mock<IRateLimiter>();
            _mockSanitizer = new Mock<IRecommendationSanitizer>();
            _mockIterativeStrategy = new Mock<IterativeRecommendationStrategy>();
            _mockLogger = new Mock<Logger>();

            _service = new RecommendationService(
                _mockCache.Object,
                _mockHealthMonitor.Object,
                _mockRetryPolicy.Object,
                _mockRateLimiter.Object,
                _mockSanitizer.Object,
                _mockIterativeStrategy.Object,
                _mockLogger.Object);
        }

        [Test]
        public async Task GetRecommendationsAsync_ReturnsCachedResults_WhenCacheHit()
        {
            var cachedItems = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Test Artist", Album = "Test Album" }
            };

            _mockCache.Setup(c => c.GenerateCacheKey(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns("test-key");
            _mockCache.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems))
                .Returns(true);

            var result = await _service.GetRecommendationsAsync("provider", 10, "fingerprint");

            Assert.That(result, Is.EqualTo(cachedItems));
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task GetRecommendationsAsync_ReturnsEmpty_WhenCacheMiss()
        {
            var emptyList = new List<ImportListItemInfo>();
            _mockCache.Setup(c => c.TryGet(It.IsAny<string>(), out emptyList))
                .Returns(false);

            var result = await _service.GetRecommendationsAsync("provider", 10, "fingerprint");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GenerateRecommendationsAsync_AppliesRateLimiting()
        {
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.Name).Returns("TestProvider");

            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "New Artist", Album = "New Album" }
            };

            _mockRateLimiter.Setup(r => r.WaitAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            _mockRetryPolicy.Setup(r => r.ExecuteAsync(It.IsAny<Func<Task<List<ImportListItemInfo>>>>()))
                .ReturnsAsync(recommendations);

            _mockSanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<ImportListItemInfo>>(), It.IsAny<LibraryProfile>()))
                .Returns(recommendations);

            var result = await _service.GenerateRecommendationsAsync(
                mockProvider.Object,
                10,
                new LibraryProfile());

            _mockRateLimiter.Verify(r => r.WaitAsync("TestProvider"), Times.Once);
            Assert.That(result, Is.EqualTo(recommendations));
        }

        [Test]
        public async Task GenerateRecommendationsAsync_RecordsFailure_OnException()
        {
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.Name).Returns("TestProvider");

            _mockRateLimiter.Setup(r => r.WaitAsync(It.IsAny<string>()))
                .Throws(new Exception("Test exception"));

            _mockHealthMonitor.Setup(h => h.RecordFailureAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            Assert.ThrowsAsync<Exception>(async () =>
                await _service.GenerateRecommendationsAsync(
                    mockProvider.Object,
                    10,
                    new LibraryProfile()));

            _mockHealthMonitor.Verify(h => h.RecordFailureAsync("TestProvider"), Times.Once);
        }

        [Test]
        public async Task GenerateRecommendationsAsync_SanitizesResults()
        {
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.Name).Returns("TestProvider");

            var rawRecommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Raw Artist", Album = "Raw Album" }
            };

            var sanitizedRecommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Sanitized Artist", Album = "Sanitized Album" }
            };

            _mockRetryPolicy.Setup(r => r.ExecuteAsync(It.IsAny<Func<Task<List<ImportListItemInfo>>>>()))
                .ReturnsAsync(rawRecommendations);

            _mockSanitizer.Setup(s => s.SanitizeRecommendations(rawRecommendations, It.IsAny<LibraryProfile>()))
                .Returns(sanitizedRecommendations)
                .Verifiable();

            var result = await _service.GenerateRecommendationsAsync(
                mockProvider.Object,
                10,
                new LibraryProfile());

            _mockSanitizer.Verify();
            Assert.That(result, Is.EqualTo(sanitizedRecommendations));
        }
    }
}