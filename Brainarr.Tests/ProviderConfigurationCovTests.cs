using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation.Results;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests
{
    public class ProviderConfigurationCovTests
    {
        // ProviderConfiguration base class defaults
        [Fact]
        public void ProviderConfiguration_DefaultEnabled_IsFalse()
        {
            // Arrange & Act - ProviderConfiguration line 17
            var config = new TestProviderConfiguration();

            // Assert
            config.Enabled.Should().BeFalse("because provider should be disabled by default");
        }

        [Fact]
        public void ProviderConfiguration_DefaultPriority_Is100()
        {
            // Arrange & Act - ProviderConfiguration line 24
            var config = new TestProviderConfiguration();

            // Assert
            config.Priority.Should().Be(100, "because default priority should be 100");
        }

        [Fact]
        public void ProviderConfiguration_DefaultMaxRetries_Is3()
        {
            // Arrange & Act - ProviderConfiguration line 29
            var config = new TestProviderConfiguration();

            // Assert
            config.MaxRetries.Should().Be(3, "because default max retries should be 3");
        }

        [Fact]
        public void ProviderConfiguration_DefaultTimeoutSeconds_Is30()
        {
            // Arrange & Act - ProviderConfiguration line 34
            var config = new TestProviderConfiguration();

            // Assert
            config.TimeoutSeconds.Should().Be(30, "because default timeout should be 30 seconds");
        }

        [Fact]
        public void ProviderConfiguration_DefaultRateLimit_IsInitialized()
        {
            // Arrange & Act - ProviderConfiguration line 39
            var config = new TestProviderConfiguration();

            // Assert
            config.RateLimit.Should().NotBeNull();
            config.RateLimit.RequestsPerMinute.Should().Be(60);
            config.RateLimit.BurstSize.Should().Be(10);
            config.RateLimit.Enabled.Should().BeTrue();
        }

        [Fact]
        public void ProviderConfiguration_DefaultCustomHeaders_IsEmptyDictionary()
        {
            // Arrange & Act - ProviderConfiguration line 44
            var config = new TestProviderConfiguration();

            // Assert
            config.CustomHeaders.Should().NotBeNull();
            config.CustomHeaders.Should().BeEmpty();
        }

        [Fact]
        public void ProviderConfiguration_CanSetCustomHeaders()
        {
            // Arrange - ProviderConfiguration line 44
            var config = new TestProviderConfiguration
            {
                CustomHeaders = new Dictionary<string, string>
                {
                    { "Authorization", "Bearer token" },
                    { "X-Custom", "value" }
                }
            };

            // Assert
            config.CustomHeaders.Should().HaveCount(2);
            config.CustomHeaders.Should().ContainKey("Authorization").WhoseValue.Should().Be("Bearer token");
        }

        // RateLimitConfiguration tests
        [Fact]
        public void RateLimitConfiguration_DefaultRequestsPerMinute_Is60()
        {
            // Arrange & Act - RateLimitConfiguration line 67
            var rateLimit = new RateLimitConfiguration();

            // Assert
            rateLimit.RequestsPerMinute.Should().Be(60);
        }

        [Fact]
        public void RateLimitConfiguration_DefaultBurstSize_Is10()
        {
            // Arrange & Act - RateLimitConfiguration line 72
            var rateLimit = new RateLimitConfiguration();

            // Assert
            rateLimit.BurstSize.Should().Be(10);
        }

        [Fact]
        public void RateLimitConfiguration_DefaultEnabled_IsTrue()
        {
            // Arrange & Act - RateLimitConfiguration line 77
            var rateLimit = new RateLimitConfiguration();

            // Assert
            rateLimit.Enabled.Should().BeTrue();
        }

        [Fact]
        public void RateLimitConfiguration_CanModifyValues()
        {
            // Arrange & Act - RateLimitConfiguration lines 67-77
            var rateLimit = new RateLimitConfiguration
            {
                RequestsPerMinute = 120,
                BurstSize = 20,
                Enabled = false
            };

            // Assert
            rateLimit.RequestsPerMinute.Should().Be(120);
            rateLimit.BurstSize.Should().Be(20);
            rateLimit.Enabled.Should().BeFalse();
        }

        // OllamaProviderConfiguration defaults
        [Fact]
        public void OllamaProviderConfiguration_ProviderType_IsOllama()
        {
            // Arrange & Act - OllamaProviderConfiguration line 83
            var config = new OllamaProviderConfiguration();

            // Assert
            config.ProviderType.Should().Be("Ollama");
        }

        [Fact]
        public void OllamaProviderConfiguration_DefaultUrl_IsLocalhost11434()
        {
            // Arrange & Act - OllamaProviderConfiguration line 86
            var config = new OllamaProviderConfiguration();

            // Assert
            config.Url.Should().Be("http://localhost:11434");
        }

        [Fact]
        public void OllamaProviderConfiguration_DefaultModel_IsQwen25()
        {
            // Arrange & Act - OllamaProviderConfiguration line 89
            var config = new OllamaProviderConfiguration();

            // Assert
            config.Model.Should().Be("qwen2.5:latest");
        }

        [Fact]
        public void OllamaProviderConfiguration_DefaultTemperature_Is07()
        {
            // Arrange & Act - OllamaProviderConfiguration line 92
            var config = new OllamaProviderConfiguration();

            // Assert
            config.Temperature.Should().Be(0.7);
        }

        [Fact]
        public void OllamaProviderConfiguration_DefaultTopP_Is09()
        {
            // Arrange & Act - OllamaProviderConfiguration line 95
            var config = new OllamaProviderConfiguration();

            // Assert
            config.TopP.Should().Be(0.9);
        }

        [Fact]
        public void OllamaProviderConfiguration_DefaultMaxTokens_Is2000()
        {
            // Arrange & Act - OllamaProviderConfiguration line 98
            var config = new OllamaProviderConfiguration();

            // Assert
            config.MaxTokens.Should().Be(2000);
        }

        [Fact]
        public void OllamaProviderConfiguration_DefaultStreamResponses_IsFalse()
        {
            // Arrange & Act - OllamaProviderConfiguration line 101
            var config = new OllamaProviderConfiguration();

            // Assert
            config.StreamResponses.Should().BeFalse();
        }

        // Ollama URL validation - OllamaProviderConfigurationValidator lines 144-145
        [Fact]
        public void OllamaConfiguration_NullUrl_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 144: NotEmpty
            var config = new OllamaProviderConfiguration
            {
                Url = null,
                Model = "llama2"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because null URL should fail validation");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("URL"));
        }

        [Fact]
        public void OllamaConfiguration_WhitespaceUrl_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 144: NotEmpty
            var config = new OllamaProviderConfiguration
            {
                Url = "   ",
                Model = "llama2"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because whitespace URL should fail validation");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("URL"));
        }

        [Fact]
        public void OllamaConfiguration_InvalidUrl_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 145: BeValidUrl
            var config = new OllamaProviderConfiguration
            {
                Url = "not a valid url at all!!!",
                Model = "llama2"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because URL should be valid");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("valid URL"));
        }

        [Fact]
        public void OllamaConfiguration_UrlWithoutProtocol_ValidatesSuccessfully()
        {
            // Arrange - OllamaProviderConfigurationValidator line 161-168: BeValidUrl
            var config = new OllamaProviderConfiguration
            {
                Url = "localhost:11434",
                Model = "llama2"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because URL without protocol should be accepted");
        }

        [Fact]
        public void OllamaConfiguration_HttpsUrl_ValidatesSuccessfully()
        {
            // Arrange - OllamaProviderConfigurationValidator line 161-168: BeValidUrl
            var config = new OllamaProviderConfiguration
            {
                Url = "https://ollama.example.com",
                Model = "llama2"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because HTTPS URL should be valid");
        }

        // Ollama Model validation - OllamaProviderConfigurationValidator line 148
        [Fact]
        public void OllamaConfiguration_NullModel_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 148: NotEmpty
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = null
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because null model should fail validation");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Model"));
        }

        [Fact]
        public void OllamaConfiguration_WhitespaceModel_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 148: NotEmpty
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "  "
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because whitespace model should fail validation");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Model"));
        }

        // Ollama Temperature validation - OllamaProviderConfigurationValidator line 151
        [Fact]
        public void OllamaConfiguration_TemperatureBelowZero_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 151: InclusiveBetween(0.0, 1.0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                Temperature = -0.1
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because temperature below 0.0 should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Temperature"));
        }

        [Fact]
        public void OllamaConfiguration_TemperatureAboveOne_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 151: InclusiveBetween(0.0, 1.0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                Temperature = 1.1
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because temperature above 1.0 should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Temperature"));
        }

        [Fact]
        public void OllamaConfiguration_TemperatureAtLowerBoundary_Passes()
        {
            // Arrange - OllamaProviderConfigurationValidator line 151: InclusiveBetween(0.0, 1.0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                Temperature = 0.0
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because temperature 0.0 is at lower boundary");
        }

        [Fact]
        public void OllamaConfiguration_TemperatureAtUpperBoundary_Passes()
        {
            // Arrange - OllamaProviderConfigurationValidator line 151: InclusiveBetween(0.0, 1.0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                Temperature = 1.0
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because temperature 1.0 is at upper boundary");
        }

        // Ollama TopP validation - OllamaProviderConfigurationValidator line 154
        [Fact]
        public void OllamaConfiguration_TopPBelowZero_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 154: InclusiveBetween(0.0, 1.0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                TopP = -0.5
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because TopP below 0.0 should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Top P"));
        }

        [Fact]
        public void OllamaConfiguration_TopPAboveOne_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 154: InclusiveBetween(0.0, 1.0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                TopP = 1.5
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because TopP above 1.0 should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Top P"));
        }

        [Fact]
        public void OllamaConfiguration_TopPAtLowerBoundary_Passes()
        {
            // Arrange - OllamaProviderConfigurationValidator line 154: InclusiveBetween(0.0, 1.0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                TopP = 0.0
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because TopP 0.0 is at lower boundary");
        }

        [Fact]
        public void OllamaConfiguration_TopPAtUpperBoundary_Passes()
        {
            // Arrange - OllamaProviderConfigurationValidator line 154: InclusiveBetween(0.0, 1.0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                TopP = 1.0
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because TopP 1.0 is at upper boundary");
        }

        // Ollama MaxTokens validation - OllamaProviderConfigurationValidator lines 157-158
        [Fact]
        public void OllamaConfiguration_MaxTokensZero_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 157: GreaterThan(0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                MaxTokens = 0
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because MaxTokens 0 should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Max tokens"));
        }

        [Fact]
        public void OllamaConfiguration_MaxTokensNegative_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 157: GreaterThan(0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                MaxTokens = -100
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because negative MaxTokens should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Max tokens"));
        }

        [Fact]
        public void OllamaConfiguration_MaxTokensExceedsLimit_FailsValidation()
        {
            // Arrange - OllamaProviderConfigurationValidator line 158: LessThanOrEqualTo(10000)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                MaxTokens = 10001
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because MaxTokens 10001 exceeds limit");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Max tokens"));
        }

        [Fact]
        public void OllamaConfiguration_MaxTokensAtLowerBoundary_Passes()
        {
            // Arrange - OllamaProviderConfigurationValidator line 157: GreaterThan(0)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                MaxTokens = 1
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because MaxTokens 1 is at lower boundary");
        }

        [Fact]
        public void OllamaConfiguration_MaxTokensAtUpperBoundary_Passes()
        {
            // Arrange - OllamaProviderConfigurationValidator line 158: LessThanOrEqualTo(10000)
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                MaxTokens = 10000
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because MaxTokens 10000 is at upper boundary");
        }

        [Fact]
        public void OllamaConfiguration_MultipleValidationErrors_ReturnsAllErrors()
        {
            // Arrange - multiple validation failures
            var config = new OllamaProviderConfiguration
            {
                Url = null,
                Model = null,
                Temperature = 1.5,
                TopP = -0.5,
                MaxTokens = -1
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because multiple fields are invalid");
            result.Errors.Should().HaveCountGreaterOrEqualTo(5, "because 5 fields are invalid");
        }

        [Fact]
        public void OllamaConfiguration_StreamResponsesTrue_DoesNotAffectValidation()
        {
            // Arrange - StreamResponses has no validation rule
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                StreamResponses = true
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because StreamResponses does not affect validation");
        }

        // LMStudioProviderConfiguration defaults
        [Fact]
        public void LMStudioProviderConfiguration_ProviderType_IsLMStudio()
        {
            // Arrange & Act - LMStudioProviderConfiguration line 115
            var config = new LMStudioProviderConfiguration();

            // Assert
            config.ProviderType.Should().Be("LMStudio");
        }

        [Fact]
        public void LMStudioProviderConfiguration_DefaultUrl_IsLocalhost1234()
        {
            // Arrange & Act - LMStudioProviderConfiguration line 118
            var config = new LMStudioProviderConfiguration();

            // Assert
            config.Url.Should().Be("http://localhost:1234");
        }

        [Fact]
        public void LMStudioProviderConfiguration_DefaultModel_IsLocalModel()
        {
            // Arrange & Act - LMStudioProviderConfiguration line 121
            var config = new LMStudioProviderConfiguration();

            // Assert
            config.Model.Should().Be("local-model");
        }

        [Fact]
        public void LMStudioProviderConfiguration_DefaultTemperature_Is07()
        {
            // Arrange & Act - LMStudioProviderConfiguration line 124
            var config = new LMStudioProviderConfiguration();

            // Assert
            config.Temperature.Should().Be(0.7);
        }

        [Fact]
        public void LMStudioProviderConfiguration_DefaultMaxTokens_Is2000()
        {
            // Arrange & Act - LMStudioProviderConfiguration line 127
            var config = new LMStudioProviderConfiguration();

            // Assert
            config.MaxTokens.Should().Be(2000);
        }

        // LMStudio URL validation - LMStudioProviderConfigurationValidator lines 179-180
        [Fact]
        public void LMStudioConfiguration_NullUrl_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 179: NotEmpty
            var config = new LMStudioProviderConfiguration
            {
                Url = null,
                Model = "mistral"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because null URL should fail validation");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("URL"));
        }

        [Fact]
        public void LMStudioConfiguration_WhitespaceUrl_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 179: NotEmpty
            var config = new LMStudioProviderConfiguration
            {
                Url = "\t\n",
                Model = "mistral"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because whitespace URL should fail validation");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("URL"));
        }

        [Fact]
        public void LMStudioConfiguration_InvalidUrl_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 180: BeValidUrl
            var config = new LMStudioProviderConfiguration
            {
                Url = "!!!invalid!!!",
                Model = "mistral"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because URL should be valid");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("valid URL"));
        }

        [Fact]
        public void LMStudioConfiguration_UrlWithoutProtocol_ValidatesSuccessfully()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 193-199: BeValidUrl
            var config = new LMStudioProviderConfiguration
            {
                Url = "localhost:1234",
                Model = "mistral"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because URL without protocol should be accepted");
        }

        // LMStudio Model validation - LMStudioProviderConfigurationValidator line 183
        [Fact]
        public void LMStudioConfiguration_NullModel_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 183: NotEmpty
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = null
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because null model should fail validation");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Model"));
        }

        [Fact]
        public void LMStudioConfiguration_WhitespaceModel_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 183: NotEmpty
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = ""
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because empty model should fail validation");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Model"));
        }

        // LMStudio Temperature validation - LMStudioProviderConfigurationValidator line 186
        [Fact]
        public void LMStudioConfiguration_TemperatureBelowZero_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 186: InclusiveBetween(0.0, 1.0)
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                Temperature = -0.01
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because temperature below 0.0 should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Temperature"));
        }

        [Fact]
        public void LMStudioConfiguration_TemperatureAboveOne_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 186: InclusiveBetween(0.0, 1.0)
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                Temperature = 1.01
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because temperature above 1.0 should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Temperature"));
        }

        [Fact]
        public void LMStudioConfiguration_TemperatureAtLowerBoundary_Passes()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 186: InclusiveBetween(0.0, 1.0)
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                Temperature = 0.0
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because temperature 0.0 is at lower boundary");
        }

        [Fact]
        public void LMStudioConfiguration_TemperatureAtUpperBoundary_Passes()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 186: InclusiveBetween(0.0, 1.0)
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                Temperature = 1.0
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because temperature 1.0 is at upper boundary");
        }

        // LMStudio MaxTokens validation - LMStudioProviderConfigurationValidator lines 189-190
        [Fact]
        public void LMStudioConfiguration_MaxTokensZero_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 189: GreaterThan(0)
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                MaxTokens = 0
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because MaxTokens 0 should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Max tokens"));
        }

        [Fact]
        public void LMStudioConfiguration_MaxTokensNegative_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 189: GreaterThan(0)
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                MaxTokens = -50
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because negative MaxTokens should fail");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Max tokens"));
        }

        [Fact]
        public void LMStudioConfiguration_MaxTokensExceedsLimit_FailsValidation()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 190: LessThanOrEqualTo(10000)
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                MaxTokens = 10001
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because MaxTokens 10001 exceeds limit");
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Max tokens"));
        }

        [Fact]
        public void LMStudioConfiguration_MaxTokensAtLowerBoundary_Passes()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 189: GreaterThan(0)
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                MaxTokens = 1
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because MaxTokens 1 is at lower boundary");
        }

        [Fact]
        public void LMStudioConfiguration_MaxTokensAtUpperBoundary_Passes()
        {
            // Arrange - LMStudioProviderConfigurationValidator line 190: LessThanOrEqualTo(10000)
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                MaxTokens = 10000
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeTrue("because MaxTokens 10000 is at upper boundary");
        }

        [Fact]
        public void LMStudioConfiguration_MultipleValidationErrors_ReturnsAllErrors()
        {
            // Arrange - multiple validation failures
            var config = new LMStudioProviderConfiguration
            {
                Url = "",
                Model = "",
                Temperature = 2.0,
                MaxTokens = 0
            };

            // Act
            var result = config.Validate();

            // Assert
            result.IsValid.Should().BeFalse("because multiple fields are invalid");
            result.Errors.Should().HaveCountGreaterOrEqualTo(4, "because 4 fields are invalid");
        }

        // ProviderConfiguration inheritance tests
        [Fact]
        public void OllamaConfiguration_InheritsBaseClassDefaults()
        {
            // Arrange & Act - Verify OllamaProviderConfiguration inherits ProviderConfiguration defaults
            var config = new OllamaProviderConfiguration();

            // Assert
            config.Enabled.Should().BeFalse();
            config.Priority.Should().Be(100);
            config.MaxRetries.Should().Be(3);
            config.TimeoutSeconds.Should().Be(30);
        }

        [Fact]
        public void LMStudioConfiguration_InheritsBaseClassDefaults()
        {
            // Arrange & Act - Verify LMStudioProviderConfiguration inherits ProviderConfiguration defaults
            var config = new LMStudioProviderConfiguration();

            // Assert
            config.Enabled.Should().BeFalse();
            config.Priority.Should().Be(100);
            config.MaxRetries.Should().Be(3);
            config.TimeoutSeconds.Should().Be(30);
        }

        [Fact]
        public void OllamaConfiguration_CanModifyBaseProperties()
        {
            // Arrange & Act - ProviderConfiguration properties are settable
            var config = new OllamaProviderConfiguration
            {
                Enabled = true,
                Priority = 50,
                MaxRetries = 5,
                TimeoutSeconds = 60
            };

            // Assert
            config.Enabled.Should().BeTrue();
            config.Priority.Should().Be(50);
            config.MaxRetries.Should().Be(5);
            config.TimeoutSeconds.Should().Be(60);
        }

        [Fact]
        public void LMStudioConfiguration_CanModifyBaseProperties()
        {
            // Arrange & Act - ProviderConfiguration properties are settable
            var config = new LMStudioProviderConfiguration
            {
                Enabled = true,
                Priority = 1,
                MaxRetries = 10,
                TimeoutSeconds = 120
            };

            // Assert
            config.Enabled.Should().BeTrue();
            config.Priority.Should().Be(1);
            config.MaxRetries.Should().Be(10);
            config.TimeoutSeconds.Should().Be(120);
        }

        // Abstract method tests
        [Fact]
        public void OllamaConfiguration_Validate_ReturnsValidationResult()
        {
            // Arrange - OllamaProviderConfiguration line 104: Validate method
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeOfType<ValidationResult>();
        }

        [Fact]
        public void LMStudioConfiguration_Validate_ReturnsValidationResult()
        {
            // Arrange - LMStudioProviderConfiguration line 129: Validate method
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral"
            };

            // Act
            var result = config.Validate();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeOfType<ValidationResult>();
        }

        // Test helper class for abstract ProviderConfiguration
        private class TestProviderConfiguration : ProviderConfiguration
        {
            public override string ProviderType => "Test";
            public override ValidationResult Validate()
            {
                return new ValidationResult();
            }
        }
    }
}
