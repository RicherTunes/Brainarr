using System;
using FluentAssertions;
using FluentValidation.TestHelper;
using Brainarr.Plugin.Configuration.Providers;
using Xunit;

namespace Brainarr.Tests.Configuration.Providers
{
    [Trait("Category", "Unit")]
    public class OpenAISettingsTests
    {
        [Fact]
        public void Constructor_SetsCorrectDefaults()
        {
            // Act
            var settings = new OpenAISettings();

            // Assert
            settings.ApiEndpoint.Should().Be("https://api.openai.com/v1");
            settings.Model.Should().Be("gpt-4o-mini");
            settings.Temperature.Should().Be(0.7);
            settings.MaxTokens.Should().Be(2000);
            settings.TopP.Should().Be(1.0);
            settings.FrequencyPenalty.Should().Be(0.0);
            settings.PresencePenalty.Should().Be(0.0);
            settings.Timeout.Should().Be(30);
            settings.ApiKey.Should().Be(string.Empty);
        }

        [Fact]
        public void Validate_WithValidSettings_ReturnsValid()
        {
            // Arrange
            var settings = new OpenAISettings
            {
                ApiKey = "sk-test1234567890abcdef",
                Model = "gpt-4",
                Temperature = 1.0,
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
        public void Validate_WithInvalidApiKey_ReturnsInvalid(string invalidApiKey)
        {
            // Arrange
            var settings = new OpenAISettings { ApiKey = invalidApiKey };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(OpenAISettings.ApiKey));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Validate_WithInvalidModel_ReturnsInvalid(string invalidModel)
        {
            // Arrange
            var settings = new OpenAISettings 
            { 
                ApiKey = "sk-valid-key",
                Model = invalidModel 
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(OpenAISettings.Model));
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(2.1)]
        [InlineData(100.0)]
        public void Validate_WithInvalidTemperature_ReturnsInvalid(double invalidTemperature)
        {
            // Arrange
            var settings = new OpenAISettings 
            { 
                ApiKey = "sk-valid-key",
                Model = "gpt-4",
                Temperature = invalidTemperature 
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(OpenAISettings.Temperature));
        }

        [Theory]
        [InlineData(99)]
        [InlineData(128001)]
        [InlineData(-1)]
        public void Validate_WithInvalidMaxTokens_ReturnsInvalid(int invalidMaxTokens)
        {
            // Arrange
            var settings = new OpenAISettings 
            { 
                ApiKey = "sk-valid-key",
                Model = "gpt-4",
                MaxTokens = invalidMaxTokens 
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(OpenAISettings.MaxTokens));
        }

        [Theory]
        [InlineData(0.0, 100, true)]
        [InlineData(0.5, 500, true)]
        [InlineData(1.0, 1000, true)]
        [InlineData(1.5, 5000, true)]
        [InlineData(2.0, 128000, true)]
        public void Validate_WithValidBoundaryValues_ReturnsExpectedResult(double temperature, int maxTokens, bool expectedValid)
        {
            // Arrange
            var settings = new OpenAISettings 
            { 
                ApiKey = "sk-valid-boundary-test",
                Model = "gpt-4",
                Temperature = temperature,
                MaxTokens = maxTokens
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().Be(expectedValid);
        }

        [Fact]
        public void Properties_CanBeSetAndRetrieved()
        {
            // Arrange
            var settings = new OpenAISettings();
            const string expectedApiKey = "sk-new-test-key";
            const string expectedModel = "gpt-4-turbo";
            const double expectedTemperature = 0.9;
            const int expectedMaxTokens = 8000;

            // Act
            settings.ApiKey = expectedApiKey;
            settings.Model = expectedModel;
            settings.Temperature = expectedTemperature;
            settings.MaxTokens = expectedMaxTokens;

            // Assert
            settings.ApiKey.Should().Be(expectedApiKey);
            settings.Model.Should().Be(expectedModel);
            settings.Temperature.Should().Be(expectedTemperature);
            settings.MaxTokens.Should().Be(expectedMaxTokens);
        }

        [Fact]
        public void OptionalParameters_HaveReasonableDefaults()
        {
            // Arrange & Act
            var settings = new OpenAISettings();

            // Assert
            settings.TopP.Should().Be(1.0);
            settings.FrequencyPenalty.Should().Be(0.0);
            settings.PresencePenalty.Should().Be(0.0);
            settings.Timeout.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Validator_WithComplexValidScenario_Passes()
        {
            // Arrange
            var settings = new OpenAISettings
            {
                ApiKey = "sk-proj-1234567890abcdef1234567890abcdef1234567890abcdef",
                Model = "gpt-4o-mini",
                ApiEndpoint = "https://api.openai.com/v1",
                Temperature = 0.7,
                MaxTokens = 4096,
                TopP = 0.95,
                FrequencyPenalty = 0.1,
                PresencePenalty = 0.1,
                Timeout = 60
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }
    }
}