using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;

namespace Brainarr.Tests.Services.Core
{
    public class ModelActionHandlerTests
    {
        private readonly Mock<IModelDetectionService> _mockModelDetection;
        private readonly Mock<IProviderFactory> _mockProviderFactory;
        private readonly Mock<IHttpClient> _mockHttpClient;
        private readonly Mock<Logger> _mockLogger;
        private readonly ModelActionHandler _handler;

        public ModelActionHandlerTests()
        {
            _mockHttpClient = new Mock<IHttpClient>();
            _mockLogger = new Mock<Logger>();
            _mockModelDetection = new Mock<IModelDetectionService>();
            _mockProviderFactory = new Mock<IProviderFactory>();
            
            _handler = new ModelActionHandler(
                _mockModelDetection.Object,
                _mockProviderFactory.Object,
                _mockHttpClient.Object,
                _mockLogger.Object);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task HandleTestConnectionAsync_SuccessfulConnection_ReturnsSuccessMessage()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);
            mockProvider.Setup(p => p.ProviderName).Returns("Ollama");
            
            _mockProviderFactory
                .Setup(f => f.CreateProvider(settings, _mockHttpClient.Object, _mockLogger.Object))
                .Returns(mockProvider.Object);

            // Act
            var result = await _handler.HandleTestConnectionAsync(settings);

            // Assert
            Assert.Contains("Success", result);
            Assert.Contains("Ollama", result);
            mockProvider.Verify(p => p.TestConnectionAsync(), Times.Once);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task HandleTestConnectionAsync_FailedConnection_ReturnsFailureMessage()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(false);
            mockProvider.Setup(p => p.ProviderName).Returns("OpenAI");
            
            _mockProviderFactory
                .Setup(f => f.CreateProvider(settings, _mockHttpClient.Object, _mockLogger.Object))
                .Returns(mockProvider.Object);

            // Act
            var result = await _handler.HandleTestConnectionAsync(settings);

            // Assert
            Assert.Contains("Failed", result);
            Assert.Contains("Cannot connect", result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task HandleGetModelsAsync_OllamaProvider_ReturnsModelOptions()
        {
            // Arrange
            var settings = new BrainarrSettings 
            { 
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434"
            };
            
            var models = new List<string> { "qwen2.5:latest", "llama3.2:latest" };
            _mockModelDetection
                .Setup(m => m.GetOllamaModelsAsync(settings.OllamaUrl))
                .ReturnsAsync(models);

            // Act
            var result = await _handler.HandleGetModelsAsync(settings);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Contains(result, o => o.Value == "qwen2.5:latest");
            Assert.Contains(result, o => o.Value == "llama3.2:latest");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task HandleGetModelsAsync_StaticProvider_ReturnsEnumOptions()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };

            // Act
            var result = await _handler.HandleGetModelsAsync(settings);

            // Assert
            Assert.NotEmpty(result);
            Assert.All(result, option =>
            {
                Assert.NotNull(option.Value);
                Assert.NotNull(option.Name);
            });
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HandleProviderAction_ProviderChanged_ClearsModelCache()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                DetectedModels = new List<string> { "model1", "model2" }
            };

            // Act
            var result = _handler.HandleProviderAction("providerChanged", settings);

            // Assert
            Assert.NotNull(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            Assert.Contains("\"success\":true", json);
            Assert.Empty(settings.DetectedModels);
        }

        [Fact]
        [Trait("Category", "EdgeCase")]
        public async Task HandleTestConnectionAsync_ExceptionThrown_ReturnsErrorMessage()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Gemini };
            _mockProviderFactory
                .Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                .Throws(new Exception("Connection error"));

            // Act
            var result = await _handler.HandleTestConnectionAsync(settings);

            // Assert
            Assert.Contains("Failed", result);
            Assert.Contains("Connection error", result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task HandleGetModelsAsync_NoModelsFound_ReturnsFallbackOptions()
        {
            // Arrange
            var settings = new BrainarrSettings 
            { 
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434"
            };
            
            _mockModelDetection
                .Setup(m => m.GetOllamaModelsAsync(settings.OllamaUrl))
                .ReturnsAsync(new List<string>());

            // Act
            var result = await _handler.HandleGetModelsAsync(settings);

            // Assert
            Assert.NotEmpty(result);
            Assert.Contains(result, o => o.Name.Contains("Recommended"));
        }
    }
}