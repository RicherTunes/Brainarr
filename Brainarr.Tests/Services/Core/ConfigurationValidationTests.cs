using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class ConfigurationValidationTests
    {
        private readonly Mock<Logger> _loggerMock;
        private readonly BrainarrSettingsValidator _validator;

        public ConfigurationValidationTests()
        {
            _loggerMock = new Mock<Logger>();
            _validator = new BrainarrSettingsValidator();
        }

        [Fact]
        public void Validate_WithValidOllamaConfiguration_ReturnsSuccess()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = "llama3",
                MaxRecommendations = 20,
                DiscoveryMode = DiscoveryMode.Similar
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public void Validate_OllamaWithEmptyUrl_IsValid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "", // Empty URLs use defaults
                MaxRecommendations = 20
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue(); // Empty URLs are allowed and use defaults
        }

        [Fact]
        public void Validate_WithInvalidOllamaUrl_ReturnsError()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "not-a-valid-url",
                MaxRecommendations = 20
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "OllamaUrl" && e.ErrorMessage.Contains("valid URL"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(51)]
        [InlineData(100)]
        public void Validate_WithRecommendationsOutOfRange_ReturnsError(int count)
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

        [Fact]
        public void Validate_LMStudioWithEmptyUrl_IsValid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "", // Empty URLs use defaults
                MaxRecommendations = 20
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeTrue(); // Empty URLs are allowed and use defaults
        }

        // Tests for new cloud provider validations
        [Fact]
        public void Validate_PerplexityWithMissingApiKey_ReturnsError()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Perplexity,
                PerplexityApiKey = "",
                MaxRecommendations = 20
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "PerplexityApiKey");
        }

        [Fact]
        public void Validate_OpenAIWithMissingApiKey_ReturnsError()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "",
                MaxRecommendations = 20
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "OpenAIApiKey");
        }

        [Fact]
        public void Validate_AnthropicWithMissingApiKey_ReturnsError()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Anthropic,
                AnthropicApiKey = "",
                MaxRecommendations = 20
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "AnthropicApiKey");
        }

        [Theory]
        [InlineData(AIProvider.OpenRouter, "OpenRouterApiKey")]
        [InlineData(AIProvider.DeepSeek, "DeepSeekApiKey")]
        [InlineData(AIProvider.Gemini, "GeminiApiKey")]
        [InlineData(AIProvider.Groq, "GroqApiKey")]
        public void Validate_CloudProvidersWithMissingApiKeys_ReturnsError(AIProvider provider, string expectedProperty)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = provider,
                MaxRecommendations = 20
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == expectedProperty);
        }

        [Fact]
        public void ValidateBrainarrSettings_WithValidSettings_ReturnsSuccess()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                MaxRecommendations = 20,
                DiscoveryMode = DiscoveryMode.Similar
            };

            var validator = new ConfigurationValidator();

            // Act
            var result = validator.ValidateSettings(settings);

            // Assert
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public void ValidateBrainarrSettings_WithInvalidUrl_ReturnsError()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "not-a-valid-url",
                MaxRecommendations = 20
            };

            var validator = new ConfigurationValidator();

            // Act
            var result = validator.ValidateSettings(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("URL"));
        }

        [Fact]
        public void ValidateBrainarrSettings_WithNegativeRecommendations_ReturnsError()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                MaxRecommendations = -5
            };

            var validator = new ConfigurationValidator();

            // Act
            var result = validator.ValidateSettings(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("Recommendations"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(51)]
        [InlineData(100)]
        public void ValidateBrainarrSettings_WithOutOfRangeRecommendations_ReturnsError(int count)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                MaxRecommendations = count
            };

            var validator = new ConfigurationValidator();

            // Act
            var result = validator.ValidateSettings(settings);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("between 1 and 50"));
        }

        [Fact]
        public void ValidateProviderConfiguration_WithCompleteConfig_ReturnsSuccess()
        {
            // Arrange
            var config = new TestProviderConfiguration
            {
                Enabled = true,
                Priority = 1,
                Provider = AIProvider.Ollama,
                Settings = new Dictionary<string, object>
                {
                    ["url"] = "http://localhost:11434",
                    ["model"] = "llama2",
                    ["temperature"] = 0.7
                }
            };

            var validator = new ConfigurationValidator();

            // Act
            var result = validator.ValidateProviderConfiguration(config);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void ValidateProviderConfiguration_WithMissingUrl_ReturnsError()
        {
            // Arrange
            var config = new TestProviderConfiguration
            {
                Enabled = true,
                Provider = AIProvider.Ollama,
                Settings = new Dictionary<string, object>
                {
                    ["model"] = "llama2"
                }
            };

            var validator = new ConfigurationValidator();

            // Act
            var result = validator.ValidateProviderConfiguration(config);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("URL"));
        }

        [Fact]
        public void ValidateApiKey_WithValidKey_ReturnsTrue()
        {
            // Arrange
            var validator = new ConfigurationValidator();

            // Act & Assert
            validator.ValidateApiKey("sk-valid-api-key-123456789").Should().BeTrue();
            validator.ValidateApiKey("").Should().BeFalse();
            validator.ValidateApiKey(null).Should().BeFalse();
            validator.ValidateApiKey("short").Should().BeFalse();
        }

        [Theory]
        [InlineData("http://localhost:11434", true)]
        [InlineData("https://api.example.com", true)]
        [InlineData("http://192.168.1.100:8080", true)]
        [InlineData("not-a-url", false)]
        [InlineData("ftp://invalid.com", false)]
        [InlineData("", false)]
        public void ValidateUrl_WithVariousInputs_ReturnsExpected(string url, bool expected)
        {
            // Arrange
            var validator = new ConfigurationValidator();

            // Act
            var result = validator.ValidateUrl(url);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void ValidateModelName_WithValidNames_ReturnsTrue()
        {
            // Arrange
            var validator = new ConfigurationValidator();

            // Act & Assert
            validator.ValidateModelName("llama2").Should().BeTrue();
            validator.ValidateModelName("gpt-4-turbo").Should().BeTrue();
            validator.ValidateModelName("mixtral-8x7b").Should().BeTrue();
            validator.ValidateModelName("").Should().BeFalse();
            validator.ValidateModelName(null).Should().BeFalse();
        }

        [Fact]
        public async Task ValidateWithProviderConnection_WithWorkingProvider_ReturnsSuccess()
        {
            // Arrange
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                MaxRecommendations = 20
            };

            var validator = new ConfigurationValidator();

            // Act
            var result = await validator.ValidateWithConnectionTestAsync(settings, providerMock.Object);

            // Assert
            result.IsValid.Should().BeTrue();
            result.Metadata.Should().ContainKey("connectionTest");
            result.Metadata["connectionTest"].Should().Be("success");
        }

        [Fact]
        public async Task ValidateWithProviderConnection_WithFailingProvider_ReturnsWarning()
        {
            // Arrange
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.TestConnectionAsync()).ReturnsAsync(false);

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                MaxRecommendations = 20
            };

            var validator = new ConfigurationValidator();

            // Act
            var result = await validator.ValidateWithConnectionTestAsync(settings, providerMock.Object);

            // Assert
            result.IsValid.Should().BeTrue(); // Still valid config
            result.Warnings.Should().Contain(w => w.Contains("connection test failed"));
        }
    }

    // Helper classes for testing
    public class TestProviderConfiguration : ProviderConfiguration
    {
        public AIProvider Provider { get; set; }
        public Dictionary<string, object> Settings { get; set; } = new Dictionary<string, object>();
        
        public override string ProviderType => Provider.ToString();
        
        public override FluentValidation.Results.ValidationResult Validate()
        {
            return new FluentValidation.Results.ValidationResult();
        }
    }

    public class ConfigurationValidator
    {
        public TestValidationResult ValidateSettings(BrainarrSettings settings)
        {
            var result = new TestValidationResult { IsValid = true };

            if (settings.MaxRecommendations < 1 || settings.MaxRecommendations > 50)
            {
                result.IsValid = false;
                result.Errors.Add("Recommendations must be between 1 and 50");
            }

            if (!string.IsNullOrEmpty(settings.OllamaUrl) && !ValidateUrl(settings.OllamaUrl))
            {
                result.IsValid = false;
                result.Errors.Add("Invalid Ollama URL format");
            }

            return result;
        }

        public TestValidationResult ValidateProviderConfiguration(TestProviderConfiguration config)
        {
            var result = new TestValidationResult { IsValid = true };

            if (config.Provider == AIProvider.Ollama)
            {
                if (!config.Settings.ContainsKey("url"))
                {
                    result.IsValid = false;
                    result.Errors.Add("Ollama configuration requires URL");
                }
            }

            return result;
        }

        public bool ValidateApiKey(string apiKey)
        {
            return !string.IsNullOrEmpty(apiKey) && apiKey.Length > 10;
        }

        public bool ValidateUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        public bool ValidateModelName(string modelName)
        {
            return !string.IsNullOrEmpty(modelName);
        }

        public async Task<TestValidationResult> ValidateWithConnectionTestAsync(BrainarrSettings settings, IAIProvider provider)
        {
            var result = ValidateSettings(settings);
            
            var connected = await provider.TestConnectionAsync();
            result.Metadata["connectionTest"] = connected ? "success" : "failed";
            
            if (!connected)
            {
                result.Warnings.Add("Provider connection test failed");
            }

            return result;
        }
    }

    public class TestValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}