using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;

namespace Brainarr.Tests.Services.Core
{
    public class ProviderLifecycleManagerTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Mock<IProviderFactory> _providerFactory;
        private readonly Mock<ModelDetectionService> _modelDetection;
        private readonly Mock<IProviderHealthMonitor> _healthMonitor;
        private readonly Mock<Logger> _logger;
        private readonly ProviderLifecycleManager _manager;

        public ProviderLifecycleManagerTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _providerFactory = new Mock<IProviderFactory>();
            _modelDetection = new Mock<ModelDetectionService>();
            _healthMonitor = new Mock<IProviderHealthMonitor>();
            _logger = new Mock<Logger>();
            
            _manager = new ProviderLifecycleManager(
                _httpClient.Object,
                _providerFactory.Object,
                _modelDetection.Object,
                _healthMonitor.Object,
                _logger.Object);
        }

        [Fact]
        public async Task InitializeProviderAsync_CreatesProvider_WhenSettingsValid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                AutoDetectModel = false
            };
            
            var mockProvider = new Mock<IAIProvider>();
            _providerFactory.Setup(f => f.CreateProvider(settings, _httpClient.Object, _logger.Object))
                .Returns(mockProvider.Object);

            // Act
            var result = await _manager.InitializeProviderAsync(settings);

            // Assert
            Assert.True(result);
            Assert.NotNull(_manager.GetProvider());
            _providerFactory.Verify(f => f.CreateProvider(settings, _httpClient.Object, _logger.Object), Times.Once);
        }

        [Fact]
        public async Task InitializeProviderAsync_AutoDetectsModels_WhenEnabled()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                AutoDetectModel = true,
                OllamaUrl = "http://localhost:11434"
            };
            
            var models = new List<string> { "qwen2.5:latest", "llama3.2:latest" };
            _modelDetection.Setup(m => m.GetOllamaModelsAsync(settings.OllamaUrl))
                .ReturnsAsync(models);
            
            var mockProvider = new Mock<IAIProvider>();
            _providerFactory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), _httpClient.Object, _logger.Object))
                .Returns(mockProvider.Object);

            // Act
            var result = await _manager.InitializeProviderAsync(settings);

            // Assert
            Assert.True(result);
            Assert.Equal("qwen2.5:latest", settings.OllamaModel);
            _modelDetection.Verify(m => m.GetOllamaModelsAsync(settings.OllamaUrl), Times.Once);
        }

        [Fact]
        public async Task InitializeProviderAsync_DisposesOldProvider_WhenTypeChanges()
        {
            // Arrange
            var settings1 = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var settings2 = new BrainarrSettings { Provider = AIProvider.Anthropic };
            
            var mockProvider1 = new Mock<IAIProvider>();
            mockProvider1.As<IDisposable>();
            var mockProvider2 = new Mock<IAIProvider>();
            
            _providerFactory.SetupSequence(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), _httpClient.Object, _logger.Object))
                .Returns(mockProvider1.Object)
                .Returns(mockProvider2.Object);

            // Act
            await _manager.InitializeProviderAsync(settings1);
            await _manager.InitializeProviderAsync(settings2);

            // Assert
            mockProvider1.As<IDisposable>().Verify(p => p.Dispose(), Times.Once);
            Assert.Equal(mockProvider2.Object, _manager.GetProvider());
        }

        [Fact]
        public async Task IsProviderHealthyAsync_ReturnsTrue_WhenProviderHealthy()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var mockProvider = new Mock<IAIProvider>();
            _providerFactory.Setup(f => f.CreateProvider(settings, _httpClient.Object, _logger.Object))
                .Returns(mockProvider.Object);
            
            await _manager.InitializeProviderAsync(settings);
            
            _healthMonitor.Setup(h => h.CheckHealthAsync("OpenAI", null))
                .ReturnsAsync(HealthStatus.Healthy);

            // Act
            var result = await _manager.IsProviderHealthyAsync();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task TestConnectionAsync_DelegatesToProvider()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);
            
            _providerFactory.Setup(f => f.CreateProvider(settings, _httpClient.Object, _logger.Object))
                .Returns(mockProvider.Object);
            
            await _manager.InitializeProviderAsync(settings);

            // Act
            var result = await _manager.TestConnectionAsync();

            // Assert
            Assert.True(result);
            mockProvider.Verify(p => p.TestConnectionAsync(), Times.Once);
        }

        [Fact]
        public void RecordSuccess_CallsHealthMonitor()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var mockProvider = new Mock<IAIProvider>();
            _providerFactory.Setup(f => f.CreateProvider(settings, _httpClient.Object, _logger.Object))
                .Returns(mockProvider.Object);
            
            _manager.InitializeProviderAsync(settings).Wait();

            // Act
            _manager.RecordSuccess(123.45);

            // Assert
            _healthMonitor.Verify(h => h.RecordSuccess("OpenAI", 123.45), Times.Once);
        }

        [Fact]
        public void RecordFailure_CallsHealthMonitor()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var mockProvider = new Mock<IAIProvider>();
            _providerFactory.Setup(f => f.CreateProvider(settings, _httpClient.Object, _logger.Object))
                .Returns(mockProvider.Object);
            
            _manager.InitializeProviderAsync(settings).Wait();

            // Act
            _manager.RecordFailure("Connection timeout");

            // Assert
            _healthMonitor.Verify(h => h.RecordFailure("OpenAI", "Connection timeout"), Times.Once);
        }

        [Fact]
        public void Dispose_DisposesCurrentProvider()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.As<IDisposable>();
            
            _providerFactory.Setup(f => f.CreateProvider(settings, _httpClient.Object, _logger.Object))
                .Returns(mockProvider.Object);
            
            _manager.InitializeProviderAsync(settings).Wait();

            // Act
            _manager.Dispose();

            // Assert
            mockProvider.As<IDisposable>().Verify(p => p.Dispose(), Times.Once);
            Assert.Null(_manager.GetProvider());
        }
    }
}