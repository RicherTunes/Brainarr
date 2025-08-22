using System;
using System.Collections.Generic;
using Xunit;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using System.Threading.Tasks;

namespace Brainarr.Tests.Services.Core
{
    public class SettingsActionHandlerTests
    {
        private readonly Mock<ModelDetectionService> _modelDetectionMock;
        private readonly Mock<Logger> _loggerMock;
        private readonly SettingsActionHandler _handler;

        public SettingsActionHandlerTests()
        {
            _modelDetectionMock = new Mock<ModelDetectionService>();
            _loggerMock = new Mock<Logger>();
            _handler = new SettingsActionHandler(_modelDetectionMock.Object, _loggerMock.Object);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ShouldThrowArgumentNullException_WhenModelDetectionIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new SettingsActionHandler(null, _loggerMock.Object));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new SettingsActionHandler(_modelDetectionMock.Object, null));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HandleAction_ProviderChanged_ShouldClearDetectedModels()
        {
            var settings = new BrainarrSettings
            {
                DetectedModels = new List<string> { "model1", "model2" }
            };

            var result = _handler.HandleAction("providerChanged", settings, null);

            Assert.NotNull(result);
            Assert.Empty(settings.DetectedModels);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HandleAction_GetModelOptions_Ollama_ShouldReturnOllamaOptions()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434"
            };

            _modelDetectionMock.Setup(x => x.GetOllamaModelsAsync(It.IsAny<string>()))
                .Returns(Task.FromResult(new List<string> { "qwen2.5:latest", "llama3.2:latest" }));

            var result = _handler.HandleAction("getModelOptions", settings, null) as dynamic;

            Assert.NotNull(result);
            Assert.NotNull(result.options);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HandleAction_GetModelOptions_LMStudio_ShouldReturnLMStudioOptions()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "http://localhost:1234"
            };

            _modelDetectionMock.Setup(x => x.GetLMStudioModelsAsync(It.IsAny<string>()))
                .Returns(Task.FromResult(new List<string> { "local-model" }));

            var result = _handler.HandleAction("getModelOptions", settings, null) as dynamic;

            Assert.NotNull(result);
            Assert.NotNull(result.options);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HandleAction_GetModelOptions_CloudProvider_ShouldReturnStaticOptions()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI
            };

            var result = _handler.HandleAction("getModelOptions", settings, null) as dynamic;

            Assert.NotNull(result);
            Assert.NotNull(result.options);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HandleAction_UnknownAction_ShouldReturnEmptyObject()
        {
            var settings = new BrainarrSettings();
            
            var result = _handler.HandleAction("unknownAction", settings, null) as dynamic;

            Assert.NotNull(result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HandleAction_GetOllamaOptions_WhenProviderMismatch_ShouldReturnEmptyObject()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI
            };

            var result = _handler.HandleAction("getOllamaOptions", settings, null) as dynamic;

            Assert.NotNull(result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HandleAction_GetLMStudioOptions_WhenProviderMismatch_ShouldReturnEmptyObject()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI
            };

            var result = _handler.HandleAction("getLMStudioOptions", settings, null) as dynamic;

            Assert.NotNull(result);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void HandleAction_GetModelOptions_Ollama_WithEmptyUrl_ShouldReturnFallback()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = ""
            };

            var result = _handler.HandleAction("getModelOptions", settings, null) as dynamic;

            Assert.NotNull(result);
            Assert.NotNull(result.options);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void HandleAction_GetModelOptions_LMStudio_WithEmptyUrl_ShouldReturnFallback()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = ""
            };

            var result = _handler.HandleAction("getModelOptions", settings, null) as dynamic;

            Assert.NotNull(result);
            Assert.NotNull(result.options);
        }

        [Fact]
        [Trait("Category", "EdgeCase")]
        public void HandleAction_GetModelOptions_WithException_ShouldReturnFallback()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434"
            };

            _modelDetectionMock.Setup(x => x.GetOllamaModelsAsync(It.IsAny<string>()))
                .Throws(new Exception("Connection failed"));

            var result = _handler.HandleAction("getModelOptions", settings, null) as dynamic;

            Assert.NotNull(result);
            Assert.NotNull(result.options);
        }
    }
}