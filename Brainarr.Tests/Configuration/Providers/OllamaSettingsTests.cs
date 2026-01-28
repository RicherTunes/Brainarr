using System;
using FluentAssertions;
using Brainarr.Plugin.Configuration.Providers;
using Xunit;

namespace Brainarr.Tests.Configuration.Providers
{
    public class OllamaSettingsTests
    {
        [Fact]
        public void Constructor_SetsCorrectDefaults()
        {
            // Act
            var settings = new OllamaSettings();

            // Assert
            settings.Endpoint.Should().Be("http://localhost:11434");
            settings.ModelName.Should().Be("llama3.2");
            settings.Temperature.Should().Be(0.7);
            settings.MaxTokens.Should().Be(2000);
            settings.TopP.Should().Be(0.9);
            settings.TopK.Should().Be(40);
            settings.Timeout.Should().Be(30);
        }

        [Fact]
        public void Validate_WithValidSettings_ReturnsValid()
        {
            // Arrange
            var settings = new OllamaSettings
            {
                Endpoint = "http://localhost:11434",
                ModelName = "llama3.1:8b",
                Temperature = 0.8,
                MaxTokens = 4000
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("invalid-url")]
        [InlineData("ftp://invalid.com")]
        public void Validate_WithInvalidEndpoint_ReturnsInvalid(string? invalidEndpoint)
        {
            // Arrange
            var settings = new OllamaSettings
            {
                Endpoint = invalidEndpoint,
                ModelName = "llama3.1"
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(OllamaSettings.Endpoint));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Validate_WithInvalidModelName_ReturnsInvalid(string? invalidModel)
        {
            // Arrange
            var settings = new OllamaSettings
            {
                Endpoint = "http://localhost:11434",
                ModelName = invalidModel
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(OllamaSettings.ModelName));
        }

        [Theory]
        [InlineData("http://localhost:11434")]
        [InlineData("https://my-ollama.server.com:8080")]
        [InlineData("http://192.168.1.100:11434")]
        public void Validate_WithValidUrls_ReturnsValid(string validUrl)
        {
            // Arrange
            var settings = new OllamaSettings
            {
                Endpoint = validUrl,
                ModelName = "llama3.1"
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(2.1)]
        [InlineData(100.0)]
        public void Validate_WithInvalidTemperature_ReturnsInvalid(double invalidTemperature)
        {
            // Arrange
            var settings = new OllamaSettings
            {
                Endpoint = "http://localhost:11434",
                ModelName = "llama3.1",
                Temperature = invalidTemperature
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(OllamaSettings.Temperature));
        }

        [Theory]
        [InlineData(99)]
        [InlineData(32001)]
        [InlineData(0)]
        public void Validate_WithInvalidMaxTokens_ReturnsInvalid(int invalidMaxTokens)
        {
            // Arrange
            var settings = new OllamaSettings
            {
                Endpoint = "http://localhost:11434",
                ModelName = "llama3.1",
                MaxTokens = invalidMaxTokens
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(OllamaSettings.MaxTokens));
        }

        [Fact]
        public void Properties_CanBeModified()
        {
            // Arrange
            var settings = new OllamaSettings();

            // Act
            settings.Endpoint = "http://custom-server:8080";
            settings.ModelName = "codellama:34b";
            settings.Temperature = 0.1;
            settings.MaxTokens = 8000;
            settings.TopP = 0.95;
            settings.TopK = 20;

            // Assert
            settings.Endpoint.Should().Be("http://custom-server:8080");
            settings.ModelName.Should().Be("codellama:34b");
            settings.Temperature.Should().Be(0.1);
            settings.MaxTokens.Should().Be(8000);
            settings.TopP.Should().Be(0.95);
            settings.TopK.Should().Be(20);
        }
    }
}
