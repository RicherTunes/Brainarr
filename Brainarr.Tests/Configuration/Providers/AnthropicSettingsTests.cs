using System;
using FluentAssertions;
using Brainarr.Plugin.Configuration.Providers;
using Xunit;

namespace Brainarr.Tests.Configuration.Providers
{
    [Trait("Category", "Unit")]
    public class AnthropicSettingsTests
    {
        [Fact]
        public void Constructor_SetsCorrectDefaults()
        {
            // Act
            var settings = new AnthropicSettings();

            // Assert
            settings.ApiEndpoint.Should().Be("https://api.anthropic.com/v1");
            settings.Model.Should().Be("claude-3-5-sonnet-20241022");
            settings.Temperature.Should().Be(0.7);
            settings.MaxTokens.Should().Be(4000);
            settings.TopP.Should().Be(0.9);
            settings.TopK.Should().Be(0);
            settings.Timeout.Should().Be(30);
            settings.ApiKey.Should().Be(string.Empty);
        }

        [Fact]
        public void Validate_WithValidSettings_ReturnsValid()
        {
            // Arrange
            var settings = new AnthropicSettings
            {
                ApiKey = "sk-ant-test1234567890abcdef",
                Model = "claude-3-5-sonnet-20241022",
                Temperature = 0.5,
                MaxTokens = 8000
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1.1)]
        [InlineData(2.0)]
        public void Validate_WithInvalidTemperature_ReturnsInvalid(double invalidTemperature)
        {
            // Arrange
            var settings = new AnthropicSettings
            {
                ApiKey = "sk-ant-valid",
                Model = "claude-3-haiku",
                Temperature = invalidTemperature
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(AnthropicSettings.Temperature));
        }

        [Theory]
        [InlineData(99)]
        [InlineData(200001)]
        [InlineData(0)]
        public void Validate_WithInvalidMaxTokens_ReturnsInvalid(int invalidMaxTokens)
        {
            // Arrange
            var settings = new AnthropicSettings
            {
                ApiKey = "sk-ant-valid",
                Model = "claude-3-sonnet",
                MaxTokens = invalidMaxTokens
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(AnthropicSettings.MaxTokens));
        }

        [Theory]
        [InlineData(0.0, 100)]
        [InlineData(0.5, 1000)]
        [InlineData(1.0, 200000)]
        public void Validate_WithValidBoundaryValues_ReturnsValid(double temperature, int maxTokens)
        {
            // Arrange
            var settings = new AnthropicSettings
            {
                ApiKey = "sk-ant-boundary-test",
                Model = "claude-3-opus",
                Temperature = temperature,
                MaxTokens = maxTokens
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Properties_CanBeModified()
        {
            // Arrange
            var settings = new AnthropicSettings();

            // Act
            settings.ApiKey = "sk-ant-modified";
            settings.Model = "claude-3-opus";
            settings.Temperature = 0.3;
            settings.MaxTokens = 100000;
            settings.TopP = 0.8;
            settings.TopK = 10;

            // Assert
            settings.ApiKey.Should().Be("sk-ant-modified");
            settings.Model.Should().Be("claude-3-opus");
            settings.Temperature.Should().Be(0.3);
            settings.MaxTokens.Should().Be(100000);
            settings.TopP.Should().Be(0.8);
            settings.TopK.Should().Be(10);
        }
    }
}
