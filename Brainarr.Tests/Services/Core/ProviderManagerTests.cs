using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Common.Http;

namespace Brainarr.Tests.Services.Core
{
    public class ProviderManagerTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<IProviderFactory> _providerFactoryMock;
        private readonly Mock<ModelDetectionService> _modelDetectionMock;
        private readonly Mock<IRetryPolicy> _retryPolicyMock;
        private readonly Mock<IRateLimiter> _rateLimiterMock;
        private readonly Mock<Logger> _loggerMock;
        private readonly ProviderManager _providerManager;

        public ProviderManagerTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _providerFactoryMock = new Mock<IProviderFactory>();
            _modelDetectionMock = new Mock<ModelDetectionService>(
                _httpClientMock.Object, 
                Mock.Of<Logger>());
            _retryPolicyMock = new Mock<IRetryPolicy>();
            _rateLimiterMock = new Mock<IRateLimiter>();
            _loggerMock = new Mock<Logger>();

            _providerManager = new ProviderManager(
                _httpClientMock.Object,
                _providerFactoryMock.Object,
                _modelDetectionMock.Object,
                _retryPolicyMock.Object,
                _rateLimiterMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public void InitializeProvider_WithValidSettings_CreatesProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "test-key",
                OpenAIModel = "gpt-4"
            };

            var mockProvider = new Mock<IAIProvider>();
            _providerFactoryMock.Setup(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()))
                .Returns(mockProvider.Object);

            // Act
            _providerManager.InitializeProvider(settings);

            // Assert
            Assert.True(_providerManager.IsProviderReady());
            Assert.NotNull(_providerManager.GetCurrentProvider());
        }

        [Fact]
        public void InitializeProvider_CalledTwiceWithSameSettings_DoesNotRecreate()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Anthropic,
                BaseUrl = "https://api.anthropic.com",
                AnthropicApiKey = "test-key"
            };

            var mockProvider = new Mock<IAIProvider>();
            _providerFactoryMock.Setup(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()))
                .Returns(mockProvider.Object);

            // Act
            _providerManager.InitializeProvider(settings);
            _providerManager.InitializeProvider(settings);

            // Assert
            _providerFactoryMock.Verify(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()), 
                Times.Once);
        }

        [Fact]
        public async Task DetectAvailableModels_ForOllama_CallsCorrectMethod()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                BaseUrl = "http://localhost:11434"
            };

            var expectedModels = new List<string> { "llama3", "mistral", "phi3" };
            _modelDetectionMock.Setup(x => x.DetectOllamaModelsAsync(It.IsAny<string>()))
                .ReturnsAsync(expectedModels);

            // Act
            var models = await _providerManager.DetectAvailableModels(settings);

            // Assert
            Assert.Equal(expectedModels, models);
            _modelDetectionMock.Verify(x => x.DetectOllamaModelsAsync(settings.BaseUrl), Times.Once);
        }

        [Fact]
        public async Task DetectAvailableModels_ForLMStudio_CallsCorrectMethod()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                BaseUrl = "http://localhost:1234"
            };

            var expectedModels = new List<string> { "local-model" };
            _modelDetectionMock.Setup(x => x.DetectLMStudioModelsAsync(It.IsAny<string>()))
                .ReturnsAsync(expectedModels);

            // Act
            var models = await _providerManager.DetectAvailableModels(settings);

            // Assert
            Assert.Equal(expectedModels, models);
            _modelDetectionMock.Verify(x => x.DetectLMStudioModelsAsync(settings.BaseUrl), Times.Once);
        }

        [Fact]
        public async Task DetectAvailableModels_ForCloudProvider_ReturnsEmpty()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI
            };

            // Act
            var models = await _providerManager.DetectAvailableModels(settings);

            // Assert
            Assert.Empty(models);
        }

        [Theory]
        [InlineData("llama3.1", "llama3", "mistral", "phi3")]
        [InlineData("llama2", "llama3.1", "llama2", "gemma")]
        [InlineData("mistral", "phi3", "mistral", "codellama")]
        public void SelectBestModel_ReturnsHighestRankedModel(
            string expected, 
            params string[] availableModels)
        {
            // Act
            var selected = _providerManager.SelectBestModel(availableModels.ToList());

            // Assert
            Assert.Equal(expected, selected);
        }

        [Fact]
        public void SelectBestModel_WithEmptyList_ReturnsNull()
        {
            // Act
            var selected = _providerManager.SelectBestModel(new List<string>());

            // Assert
            Assert.Null(selected);
        }

        [Fact]
        public void SelectBestModel_WithNullList_ReturnsNull()
        {
            // Act
            var selected = _providerManager.SelectBestModel(null);

            // Assert
            Assert.Null(selected);
        }

        [Fact]
        public void UpdateProvider_WithDifferentProvider_Reinitializes()
        {
            // Arrange
            var settings1 = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                BaseUrl = "https://api.openai.com"
            };

            var settings2 = new BrainarrSettings
            {
                Provider = AIProvider.Anthropic,
                BaseUrl = "https://api.anthropic.com"
            };

            var mockProvider1 = new Mock<IAIProvider>();
            var mockProvider2 = new Mock<IAIProvider>();

            _providerFactoryMock.SetupSequence(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()))
                .Returns(mockProvider1.Object)
                .Returns(mockProvider2.Object);

            // Act
            _providerManager.InitializeProvider(settings1);
            _providerManager.UpdateProvider(settings2);

            // Assert
            _providerFactoryMock.Verify(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()), 
                Times.Exactly(2));
        }

        [Fact]
        public void UpdateProvider_WithModelChange_UpdatesModel()
        {
            // Arrange
            var settings1 = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                BaseUrl = "https://api.openai.com",
                OpenAIModel = "gpt-3.5"
            };

            var settings2 = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                BaseUrl = "https://api.openai.com",
                OpenAIModel = "gpt-4"
            };

            var mockProvider = new Mock<IAIProvider>();
            _providerFactoryMock.Setup(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()))
                .Returns(mockProvider.Object);

            // Act
            _providerManager.InitializeProvider(settings1);
            _providerManager.UpdateProvider(settings2);

            // Assert
            mockProvider.Verify(x => x.UpdateModel(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void Dispose_WithDisposableProvider_DisposesProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama
            };

            var mockProvider = new Mock<IAIProvider>();
            var disposableProvider = mockProvider.As<IDisposable>();
            
            _providerFactoryMock.Setup(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()))
                .Returns(mockProvider.Object);

            _providerManager.InitializeProvider(settings);

            // Act
            _providerManager.Dispose();

            // Assert
            disposableProvider.Verify(x => x.Dispose(), Times.Once);
            Assert.False(_providerManager.IsProviderReady());
        }

        [Fact]
        public async Task InitializeProvider_WithAutoDetection_SelectsBestModel()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                BaseUrl = "http://localhost:11434",
                EnableAutoDetection = true,
                OllamaModel = null
            };

            var availableModels = new List<string> { "codellama", "llama3", "phi3" };
            _modelDetectionMock.Setup(x => x.DetectOllamaModelsAsync(It.IsAny<string>()))
                .ReturnsAsync(availableModels);

            var mockProvider = new Mock<IAIProvider>();
            _providerFactoryMock.Setup(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()))
                .Returns(mockProvider.Object);

            // Act
            _providerManager.InitializeProvider(settings);

            // Assert
            Assert.Equal("llama3", settings.OllamaModel);
            mockProvider.Verify(x => x.UpdateModel("llama3"), Times.Once);
        }

        [Fact]
        public void InitializeProvider_WithException_ThrowsAndLogsError()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI
            };

            _providerFactoryMock.Setup(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()))
                .Throws(new InvalidOperationException("Test exception"));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => 
                _providerManager.InitializeProvider(settings));
            
            _loggerMock.Verify(x => x.Error(
                It.IsAny<Exception>(), 
                It.IsAny<string>()), 
                Times.Once);
        }
    }
}