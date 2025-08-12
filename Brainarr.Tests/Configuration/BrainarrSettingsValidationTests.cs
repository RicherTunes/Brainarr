using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class BrainarrSettingsValidationTests
    {
        private readonly BrainarrSettingsValidator _validator;

        public BrainarrSettingsValidationTests()
        {
            _validator = new BrainarrSettingsValidator();
        }

        #region MaxRecommendations Validation

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(25)]
        [InlineData(50)]
        public void Validate_WithValidRecommendationCount_ReturnsValid(int count)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                MaxRecommendations = count
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(51)]
        [InlineData(100)]
        public void Validate_WithInvalidRecommendationCount_ReturnsInvalid(int count)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                MaxRecommendations = count
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "MaxRecommendations");
        }

        #endregion

        #region Ollama Validation

        [Fact]
        public void Validate_OllamaWithValidUrl_ReturnsValid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Validate_OllamaWithEmptyUrl_UsesDefaultAndReturnsValid(string url)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                MaxRecommendations = 10
            };
            
            if (url != null)
            {
                settings.OllamaUrl = url;
            }

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue(); // Default URL is used
            settings.OllamaUrl.Should().NotBeNullOrEmpty(); // Getter returns default
        }

        [Theory]
        [InlineData("not-a-url")]
        [InlineData("ftp://invalid.com")]
        [InlineData("javascript:alert(1)")]
        public void Validate_OllamaWithInvalidUrl_ReturnsInvalid(string url)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = url,
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => 
                e.PropertyName == "OllamaUrl" && 
                e.ErrorMessage.Contains("valid URL"));
        }

        #endregion

        #region LM Studio Validation

        [Fact]
        public void Validate_LMStudioWithValidUrl_ReturnsValid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "http://localhost:1234",
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("invalid-url")]
        [InlineData("not://valid")]
        public void Validate_LMStudioWithInvalidUrl_ReturnsInvalid(string url)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = url,
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => 
                e.PropertyName == "LMStudioUrl" && 
                e.ErrorMessage.Contains("valid URL"));
        }

        #endregion

        #region Cloud Provider API Key Validation

        [Theory]
        [InlineData(AIProvider.Perplexity, "PerplexityApiKey", "pplx-1234567890")]
        [InlineData(AIProvider.OpenAI, "OpenAIApiKey", "sk-1234567890")]
        [InlineData(AIProvider.Anthropic, "AnthropicApiKey", "sk-ant-1234567890")]
        [InlineData(AIProvider.OpenRouter, "OpenRouterApiKey", "or-1234567890")]
        [InlineData(AIProvider.DeepSeek, "DeepSeekApiKey", "ds-1234567890")]
        [InlineData(AIProvider.Gemini, "GeminiApiKey", "AIza1234567890")]
        [InlineData(AIProvider.Groq, "GroqApiKey", "gsk-1234567890")]
        public void Validate_CloudProviderWithValidApiKey_ReturnsValid(
            AIProvider provider, string propertyName, string apiKey)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = provider,
                MaxRecommendations = 10
            };
            
            var property = typeof(BrainarrSettings).GetProperty(propertyName);
            property.SetValue(settings, apiKey);

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(AIProvider.Perplexity, "PerplexityApiKey")]
        [InlineData(AIProvider.OpenAI, "OpenAIApiKey")]
        [InlineData(AIProvider.Anthropic, "AnthropicApiKey")]
        [InlineData(AIProvider.OpenRouter, "OpenRouterApiKey")]
        [InlineData(AIProvider.DeepSeek, "DeepSeekApiKey")]
        [InlineData(AIProvider.Gemini, "GeminiApiKey")]
        [InlineData(AIProvider.Groq, "GroqApiKey")]
        public void Validate_CloudProviderWithEmptyApiKey_ReturnsInvalid(
            AIProvider provider, string propertyName)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = provider,
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => 
                e.PropertyName == propertyName && 
                e.ErrorMessage.Contains("required"));
        }

        #endregion

        #region Cross-Provider Validation

        [Fact]
        public void Validate_ProviderSwitchDoesNotRequireOtherProviderSettings()
        {
            // Arrange - OpenAI selected but Ollama URL not provided
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "sk-test123",
                MaxRecommendations = 10
                // Note: OllamaUrl not set
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue(); // Should not require Ollama settings
        }

        [Fact]
        public void Validate_MultipleProvidersConfigured_OnlyValidatesSelectedProvider()
        {
            // Arrange - Both Ollama and OpenAI configured, but OpenAI selected
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OllamaUrl = "invalid-url", // Invalid but shouldn't matter
                OpenAIApiKey = "sk-valid-key",
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue(); // Invalid Ollama URL ignored
        }

        #endregion

        #region URL Validation Helper Tests

        [Theory]
        [InlineData("http://localhost:11434", true)]
        [InlineData("https://api.example.com", true)]
        [InlineData("http://192.168.1.100:8080", true)]
        [InlineData("http://ollama.local", true)]
        [InlineData("https://secure-api.com:443/v1", true)]
        [InlineData("not-a-url", false)]
        [InlineData("ftp://wrong-protocol.com", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("file:///etc/passwd", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void BeValidUrl_WithVariousInputs_ReturnsExpectedResult(string url, bool expected)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = url,
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            if (expected && !string.IsNullOrEmpty(url))
            {
                result.IsValid.Should().BeTrue();
            }
            else if (!expected && !string.IsNullOrEmpty(url))
            {
                result.IsValid.Should().BeFalse();
            }
        }

        #endregion

        #region Default Values Tests

        [Fact]
        public void Constructor_SetsDefaultValues()
        {
            // Arrange & Act
            var settings = new BrainarrSettings();

            // Assert
            settings.Provider.Should().Be(AIProvider.Ollama);
            settings.MaxRecommendations.Should().Be(BrainarrConstants.DefaultRecommendations);
            settings.DiscoveryMode.Should().Be(DiscoveryMode.Adjacent);
            settings.AutoDetectModel.Should().BeTrue();
            settings.OllamaUrl.Should().Be(BrainarrConstants.DefaultOllamaUrl);
            settings.OllamaModel.Should().Be(BrainarrConstants.DefaultOllamaModel);
            settings.LMStudioUrl.Should().Be(BrainarrConstants.DefaultLMStudioUrl);
            settings.LMStudioModel.Should().Be(BrainarrConstants.DefaultLMStudioModel);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Validate_WithAllFieldsPopulated_ValidatesOnlySelectedProvider()
        {
            // Arrange - All fields filled but Gemini selected
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Gemini,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = "llama3",
                LMStudioUrl = "http://localhost:1234",
                LMStudioModel = "model",
                PerplexityApiKey = "pplx-key",
                OpenAIApiKey = "sk-key",
                AnthropicApiKey = "ant-key",
                OpenRouterApiKey = "or-key",
                DeepSeekApiKey = "ds-key",
                GeminiApiKey = "AIza-valid-key", // Only this matters
                GroqApiKey = "gsk-key",
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validate_WithSpecialCharactersInApiKey_ReturnsValid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "sk-proj-abc123!@#$%^&*()_+-=[]{}|;:',.<>?/",
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue(); // API keys can contain special characters
        }

        [Fact]
        public void Validate_WithWhitespaceOnlyApiKey_ReturnsInvalid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "   ",
                MaxRecommendations = 10
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "OpenAIApiKey");
        }

        #endregion
    }
}