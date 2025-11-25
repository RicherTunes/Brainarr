using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Xunit;
using Newtonsoft.Json;

namespace Brainarr.Tests.Services
{
    public class LocalAIProviderTests
    {
        private readonly Logger _logger;

        public LocalAIProviderTests()
        {
            // Create a minimal logger for testing (NLog creates a null logger if not configured)
            _logger = NLog.LogManager.GetLogger("test");
        }

        [Fact]
        public void OllamaProvider_ProviderName_ReturnsCorrectName()
        {
            // Arrange
            var httpClient = Mock.Of<IHttpClient>();
            var provider = new OllamaProvider("http://localhost:11434", "test-model", httpClient, _logger);

            // Act & Assert
            provider.ProviderName.Should().Be("Ollama");
        }

        [Fact]
        public void LMStudioProvider_ProviderName_ReturnsCorrectName()
        {
            // Arrange
            var httpClient = Mock.Of<IHttpClient>();
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", httpClient, _logger);

            // Act & Assert
            provider.ProviderName.Should().Be("LM Studio");
        }

        [Fact]
        public void OllamaProvider_Constructor_WithNullUrl_UsesDefault()
        {
            // Arrange & Act
            var httpClient = Mock.Of<IHttpClient>();
            var provider = new OllamaProvider(null, "model", httpClient, _logger);

            // Assert
            provider.Should().NotBeNull();
        }

        [Fact]
        public void OllamaProvider_Constructor_WithNullHttpClient_ThrowsException()
        {
            // Arrange & Act
            Action act = () => new OllamaProvider("http://localhost", "model", null, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClient");
        }

        [Fact]
        public void LMStudioProvider_Constructor_WithNullHttpClient_ThrowsException()
        {
            // Arrange & Act
            Action act = () => new LMStudioProvider("http://localhost", "model", null, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClient");
        }
    }
}
