using System;
using FluentAssertions;
using Brainarr.Plugin.Configuration.Providers;
using Xunit;

namespace Brainarr.Tests.Configuration.Providers
{
    [Trait("Category", "Unit")]
    public class GeminiSettingsTests
    {
        [Fact]
        public void Constructor_SetsCorrectDefaults()
        {
            // Act
            var settings = new GeminiSettings();

            // Assert
            settings.Model.Should().Be(GeminiModel.Gemini_15_Flash);
            settings.Temperature.Should().Be(0.7);
            settings.MaxTokens.Should().Be(2000);
            settings.TopP.Should().Be(0.95);
            settings.TopK.Should().Be(40);
            settings.SafetyLevel.Should().Be("BLOCK_MEDIUM_AND_ABOVE");
            settings.ApiKey.Should().Be(string.Empty);
        }

        [Fact]
        public void Validate_WithValidSettings_ReturnsValid()
        {
            // Arrange
            var settings = new GeminiSettings
            {
                ApiKey = "AIzaSyTest1234567890abcdef",
                Model = GeminiModel.Gemini_15_Pro,
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
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Validate_WithInvalidApiKey_ReturnsInvalid(string invalidApiKey)
        {
            // Arrange
            var settings = new GeminiSettings { ApiKey = invalidApiKey };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(GeminiSettings.ApiKey));
            result.Errors.Should().Contain(e => e.ErrorMessage == "Google Gemini API key is required");
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(2.1)]
        [InlineData(100.0)]
        public void Validate_WithInvalidTemperature_ReturnsInvalid(double invalidTemperature)
        {
            // Arrange
            var settings = new GeminiSettings 
            { 
                ApiKey = "AIzaSyValid",
                Temperature = invalidTemperature 
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(GeminiSettings.Temperature));
        }

        [Theory]
        [InlineData(99)]
        [InlineData(2000001)]
        [InlineData(0)]
        public void Validate_WithInvalidMaxTokens_ReturnsInvalid(int invalidMaxTokens)
        {
            // Arrange
            var settings = new GeminiSettings 
            { 
                ApiKey = "AIzaSyValid",
                MaxTokens = invalidMaxTokens 
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(GeminiSettings.MaxTokens));
        }

        [Theory]
        [InlineData(GeminiModel.Gemini_15_Flash)]
        [InlineData(GeminiModel.Gemini_15_Flash_8B)]
        [InlineData(GeminiModel.Gemini_15_Pro)]
        [InlineData(GeminiModel.Gemini_20_Flash)]
        public void Model_AllEnumValues_AreValid(GeminiModel model)
        {
            // Arrange
            var settings = new GeminiSettings
            {
                ApiKey = "AIzaSyValidKey",
                Model = model
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
            var settings = new GeminiSettings();

            // Act
            settings.ApiKey = "AIzaSyModified123";
            settings.Model = GeminiModel.Gemini_15_Pro;
            settings.Temperature = 1.5;
            settings.MaxTokens = 100000;
            settings.TopP = 0.8;
            settings.TopK = 20;
            settings.SafetyLevel = "BLOCK_HIGH_AND_ABOVE";

            // Assert
            settings.ApiKey.Should().Be("AIzaSyModified123");
            settings.Model.Should().Be(GeminiModel.Gemini_15_Pro);
            settings.Temperature.Should().Be(1.5);
            settings.MaxTokens.Should().Be(100000);
            settings.TopP.Should().Be(0.8);
            settings.TopK.Should().Be(20);
            settings.SafetyLevel.Should().Be("BLOCK_HIGH_AND_ABOVE");
        }

        [Theory]
        [InlineData(0.0, 100)]
        [InlineData(1.0, 1000000)]
        [InlineData(2.0, 2000000)]
        public void Validate_WithValidBoundaryValues_ReturnsValid(double temperature, int maxTokens)
        {
            // Arrange
            var settings = new GeminiSettings 
            { 
                ApiKey = "AIzaSyBoundaryTest",
                Temperature = temperature,
                MaxTokens = maxTokens
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue();
        }
    }
}