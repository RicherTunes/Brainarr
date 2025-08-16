using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using Brainarr.Plugin;
using Brainarr.Plugin.Services;
using Brainarr.Plugin.Services.Core;
using Brainarr.Plugin.Services.Library;

namespace Brainarr.Tests.Unit.Services.Core
{
    [TestFixture]
    public class FetchOrchestratorTests
    {
        private FetchOrchestrator _orchestrator;
        private Mock<IAIProviderFactory> _providerFactoryMock;
        private Mock<IRecommendationCache> _cacheMock;
        private Mock<IProviderHealth> _healthMonitorMock;
        private Mock<IRetryPolicy> _retryPolicyMock;
        private Mock<IRateLimiter> _rateLimiterMock;
        private Mock<IModelDetectionService> _modelDetectionMock;
        private Mock<IAIProvider> _providerMock;
        private Logger _logger;
        
        [SetUp]
        public void Setup()
        {
            _providerFactoryMock = new Mock<IAIProviderFactory>();
            _cacheMock = new Mock<IRecommendationCache>();
            _healthMonitorMock = new Mock<IProviderHealth>();
            _retryPolicyMock = new Mock<IRetryPolicy>();
            _rateLimiterMock = new Mock<IRateLimiter>();
            _modelDetectionMock = new Mock<IModelDetectionService>();
            _providerMock = new Mock<IAIProvider>();
            _logger = LogManager.GetCurrentClassLogger();
            
            _orchestrator = new FetchOrchestrator(
                _providerFactoryMock.Object,
                _cacheMock.Object,
                _healthMonitorMock.Object,
                _retryPolicyMock.Object,
                _rateLimiterMock.Object,
                _modelDetectionMock.Object,
                _logger);
                
            SetupDefaultMocks();
        }
        
        private void SetupDefaultMocks()
        {
            _providerMock.Setup(p => p.ProviderName).Returns("TestProvider");
            _providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>()))
                .Returns(_providerMock.Object);
                
            _healthMonitorMock.Setup(h => h.CheckHealthAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(HealthStatus.Healthy);
                
            _rateLimiterMock.Setup(r => r.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<List<Recommendation>>>>()))
                .Returns<string, Func<Task<List<Recommendation>>>>((_, func) => func());
                
            _retryPolicyMock.Setup(r => r.ExecuteAsync(
                It.IsAny<Func<Task<List<Recommendation>>>>(),
                It.IsAny<string>()))
                .Returns<Func<Task<List<Recommendation>>>, string>((func, _) => func());
        }
        
        [Test]
        public void ExecuteFetch_WithCachedResults_ReturnsCachedData()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var libraryProfile = new LibraryProfile { TotalArtists = 100 };
            var cachedItems = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Test Artist", Album = "Test Album" }
            };
            
            _cacheMock.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems))
                .Returns(true);
            
            // Act
            var result = _orchestrator.ExecuteFetch(settings, libraryProfile);
            
            // Assert
            Assert.That(result, Is.EqualTo(cachedItems));
            _providerMock.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.Never);
        }
        
        [Test]
        public void ExecuteFetch_WithUnhealthyProvider_ReturnsEmptyList()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var libraryProfile = new LibraryProfile();
            
            _healthMonitorMock.Setup(h => h.CheckHealthAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(HealthStatus.Unhealthy);
            
            var cachedItems = default(List<ImportListItemInfo>);
            _cacheMock.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems))
                .Returns(false);
            
            // Act
            var result = _orchestrator.ExecuteFetch(settings, libraryProfile);
            
            // Assert
            Assert.That(result, Is.Empty);
            _providerMock.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.Never);
        }
        
        [Test]
        public void ExecuteFetch_WithValidRecommendations_ReturnsConvertedItems()
        {
            // Arrange
            var settings = new BrainarrSettings 
            { 
                Provider = AIProvider.OpenAI,
                MaxRecommendations = 10
            };
            var libraryProfile = new LibraryProfile();
            
            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Artist 1", Album = "Album 1", Year = "2023" },
                new Recommendation { Artist = "Artist 2", Album = "Album 2", Year = "2022" }
            };
            
            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(recommendations);
            
            var cachedItems = default(List<ImportListItemInfo>);
            _cacheMock.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems))
                .Returns(false);
            
            // Act
            var result = _orchestrator.ExecuteFetch(settings, libraryProfile);
            
            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Artist, Is.EqualTo("Artist 1"));
            Assert.That(result[0].Album, Is.EqualTo("Album 1"));
            Assert.That(result[1].Artist, Is.EqualTo("Artist 2"));
            Assert.That(result[1].Album, Is.EqualTo("Album 2"));
            
            _cacheMock.Verify(c => c.Set(
                It.IsAny<string>(),
                It.IsAny<List<ImportListItemInfo>>(),
                It.IsAny<TimeSpan>()), Times.Once);
        }
        
        [Test]
        public void ExecuteFetch_WithAutoDetectEnabled_CallsModelDetection()
        {
            // Arrange
            var settings = new BrainarrSettings 
            { 
                Provider = AIProvider.Ollama,
                AutoDetectModel = true,
                OllamaUrl = "http://localhost:11434"
            };
            var libraryProfile = new LibraryProfile();
            
            var models = new List<string> { "llama3.2", "mistral" };
            _modelDetectionMock.Setup(m => m.GetOllamaModelsAsync(It.IsAny<string>()))
                .ReturnsAsync(models);
            
            var cachedItems = default(List<ImportListItemInfo>);
            _cacheMock.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems))
                .Returns(false);
                
            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<Recommendation>());
            
            // Act
            _orchestrator.ExecuteFetch(settings, libraryProfile);
            
            // Assert
            _modelDetectionMock.Verify(m => m.GetOllamaModelsAsync(settings.OllamaUrl), Times.Once);
            Assert.That(settings.OllamaModel, Is.EqualTo("llama3.2"));
        }
        
        [Test]
        public void ExecuteFetch_WithProviderException_ReturnsEmptyListAndRecordsFailure()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var libraryProfile = new LibraryProfile();
            
            var exception = new HttpRequestException("Connection failed");
            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ThrowsAsync(exception);
            
            var cachedItems = default(List<ImportListItemInfo>);
            _cacheMock.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems))
                .Returns(false);
            
            // Act
            var result = _orchestrator.ExecuteFetch(settings, libraryProfile);
            
            // Assert
            Assert.That(result, Is.Empty);
            _healthMonitorMock.Verify(h => h.RecordFailure(
                It.IsAny<string>(),
                It.Is<string>(msg => msg.Contains("Connection failed"))), Times.Once);
        }
        
        [Test]
        public void ExecuteFetch_FiltersInvalidRecommendations()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var libraryProfile = new LibraryProfile();
            
            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Valid Artist", Album = "Valid Album" },
                new Recommendation { Artist = "", Album = "Invalid - No Artist" },
                new Recommendation { Artist = "Invalid - No Album", Album = "" },
                new Recommendation { Artist = "  ", Album = "  " },
                null
            };
            
            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(recommendations);
            
            var cachedItems = default(List<ImportListItemInfo>);
            _cacheMock.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems))
                .Returns(false);
            
            // Act
            var result = _orchestrator.ExecuteFetch(settings, libraryProfile);
            
            // Assert
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Artist, Is.EqualTo("Valid Artist"));
            Assert.That(result[0].Album, Is.EqualTo("Valid Album"));
        }
        
        [Test]
        public void ExecuteFetch_WithIterativeStrategy_UsesIterativeRecommendations()
        {
            // Arrange
            var settings = new BrainarrSettings 
            { 
                Provider = AIProvider.OpenAI,
                UseIterativeStrategy = true,
                MaxRecommendations = 20
            };
            var libraryProfile = new LibraryProfile();
            
            var recommendations = Enumerable.Range(1, 20)
                .Select(i => new Recommendation { Artist = $"Artist {i}", Album = $"Album {i}" })
                .ToList();
            
            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(recommendations.Take(5).ToList());
            
            var cachedItems = default(List<ImportListItemInfo>);
            _cacheMock.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems))
                .Returns(false);
            
            // Act
            var result = _orchestrator.ExecuteFetch(settings, libraryProfile);
            
            // Assert
            Assert.That(result.Count, Is.GreaterThan(0));
            _providerMock.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.AtLeastOnce);
        }
        
        [Test]
        public void ExecuteFetch_RecordsSuccessMetrics()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var libraryProfile = new LibraryProfile();
            
            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Test", Album = "Album" }
            };
            
            _providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(recommendations);
            
            var cachedItems = default(List<ImportListItemInfo>);
            _cacheMock.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems))
                .Returns(false);
            
            // Act
            _orchestrator.ExecuteFetch(settings, libraryProfile);
            
            // Assert
            _healthMonitorMock.Verify(h => h.RecordSuccess(
                It.IsAny<string>(),
                It.IsAny<double>()), Times.Once);
        }
    }
}