using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation.Results;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class BrainarrSettingsTests
    {
        [Fact]
        public void Constructor_SetsDefaultValues_Correctly()
        {
            // Act
            var settings = new BrainarrSettings();

            // Assert
            settings.Provider.Should().Be(AIProvider.Ollama);
            settings.OllamaUrl.Should().Be(BrainarrConstants.DefaultOllamaUrl);
            settings.OllamaModel.Should().Be(BrainarrConstants.DefaultOllamaModel);
            settings.LMStudioUrl.Should().Be(BrainarrConstants.DefaultLMStudioUrl);
            settings.LMStudioModel.Should().Be(BrainarrConstants.DefaultLMStudioModel);
            settings.MaxRecommendations.Should().Be(BrainarrConstants.DefaultRecommendations);
            settings.DiscoveryMode.Should().Be(DiscoveryMode.Adjacent);
            settings.AutoDetectModel.Should().BeTrue();
        }

        [Theory]
        [InlineData(0, false)] // Below minimum
        [InlineData(1, true)]  // Minimum
        [InlineData(25, true)] // Valid
        [InlineData(50, true)] // Maximum
        [InlineData(51, false)] // Above maximum
        [InlineData(100, false)] // Way above maximum
        [InlineData(-1, false)] // Negative
        public void Validation_MaxRecommendations_ValidatesCorrectly(int value, bool shouldBeValid)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                MaxRecommendations = value,
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434"
            };

            // Act
            var result = settings.Validate();

            // Assert
            if (shouldBeValid)
            {
                result.IsValid.Should().BeTrue();
            }
            else
            {
                result.IsValid.Should().BeFalse();
                result.Errors.Should().Contain(e => e.PropertyName == nameof(BrainarrSettings.MaxRecommendations));
            }
        }

        [Fact]
        public void Validation_OllamaProvider_AllowsNullUrl()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = null // Should use default URL
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue(); // Null URLs are allowed and use defaults
        }

        [Fact]
        public void Validation_LMStudioProvider_AllowsEmptyUrl()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "" // Should use default URL
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue(); // Empty URLs are allowed and use defaults
        }

        [Theory]
        [InlineData(AIProvider.Ollama, "http://localhost:11434", null, "http://localhost:11434")]
        [InlineData(AIProvider.LMStudio, null, "http://localhost:1234", "http://localhost:1234")]
        [InlineData(AIProvider.Ollama, "http://custom:8080", null, "http://custom:8080")]
        [InlineData(AIProvider.LMStudio, null, "http://custom:9090", "http://custom:9090")]
        public void BaseUrl_ReturnsCorrectUrl_BasedOnProvider(
            AIProvider provider, string? ollamaUrl, string? lmStudioUrl, string expectedUrl)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = provider,
                OllamaUrl = ollamaUrl,
                LMStudioUrl = lmStudioUrl
            };

            // Act
            var baseUrl = settings.BaseUrl;

            // Assert
            baseUrl.Should().Be(expectedUrl);
        }

        [Fact]
        public void DetectedModels_InitializesAsEmptyList()
        {
            // Arrange & Act
            var settings = new BrainarrSettings();

            // Assert
            settings.DetectedModels.Should().NotBeNull();
            settings.DetectedModels.Should().BeEmpty();
        }

        [Theory]
        [InlineData(DiscoveryMode.Similar)]
        [InlineData(DiscoveryMode.Adjacent)]
        [InlineData(DiscoveryMode.Exploratory)]
        public void DiscoveryMode_AcceptsAllValidValues(DiscoveryMode mode)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                DiscoveryMode = mode,
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434"
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validation_WithWhitespaceOllamaUrl_IsValid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "   " // Whitespace only - treated as empty, uses default
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue(); // Whitespace URLs use defaults
        }

        [Theory]
        [InlineData("http://localhost:11434", true)]
        [InlineData("https://localhost:11434", true)]
        [InlineData("http://192.168.1.100:11434", true)]
        [InlineData("http://ollama.local:11434", true)]
        [InlineData("localhost:11434", true)] // No protocol is acceptable
        [InlineData("", true)] // Empty uses default URL
        [InlineData(null, true)] // Null uses default URL
        public void Validation_OllamaUrl_ValidatesCorrectly(string? url, bool shouldBeValid)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = url
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().Be(shouldBeValid);
        }

        [Fact]
        public void Settings_WithLongModelNames_AcceptsCorrectly()
        {
            // Arrange
            var longModelName = new string('a', 500); // Very long model name
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = longModelName
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue();
            settings.OllamaModel.Should().Be(longModelName);
        }

        [Theory]
        [InlineData(AIProvider.Ollama, AIProvider.LMStudio, false)] // Different
        [InlineData(AIProvider.Ollama, AIProvider.Ollama, true)] // Same
        [InlineData(AIProvider.LMStudio, AIProvider.LMStudio, true)] // Same
        public void Provider_Equality_WorksCorrectly(AIProvider provider1, AIProvider provider2, bool shouldBeEqual)
        {
            // Act & Assert
            (provider1 == provider2).Should().Be(shouldBeEqual);
        }

        [Fact]
        public void Settings_WithSpecialCharactersInModel_AcceptsCorrectly()
        {
            // Arrange
            var specialModelName = "llama-2.0:7b-instruct-q4_K_M";
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = specialModelName
            };

            // Act
            var result = settings.Validate();

            // Assert
            result.IsValid.Should().BeTrue();
            settings.OllamaModel.Should().Be(specialModelName);
        }

        [Fact]
        public void Settings_Clone_CreatesIndependentCopy()
        {
            // Arrange
            var original = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://original:11434",
                MaxRecommendations = 30,
                DetectedModels = new List<string> { "model1", "model2" }
            };

            // Act - Manual clone since no Clone method exists
            var clone = new BrainarrSettings
            {
                Provider = original.Provider,
                OllamaUrl = original.OllamaUrl,
                MaxRecommendations = original.MaxRecommendations,
                DetectedModels = new List<string>(original.DetectedModels)
            };

            // Modify clone
            clone.OllamaUrl = "http://modified:11434";
            clone.MaxRecommendations = 40;
            clone.DetectedModels.Add("model3");

            // Assert
            original.OllamaUrl.Should().Be("http://original:11434");
            original.MaxRecommendations.Should().Be(30);
            original.DetectedModels.Should().HaveCount(2);

            clone.OllamaUrl.Should().Be("http://modified:11434");
            clone.MaxRecommendations.Should().Be(40);
            clone.DetectedModels.Should().HaveCount(3);
        }

        [Theory]
        [InlineData(null, "http://localhost:11434")] // Null uses default
        [InlineData("", "http://localhost:11434")] // Empty uses default
        [InlineData("http://custom:11434", "http://custom:11434")] // Custom preserved
        public void OllamaUrl_DefaultHandling_WorksCorrectly(string? input, string expected)
        {
            // Arrange
            var settings = new BrainarrSettings();
            if (input != null)
            {
                settings.OllamaUrl = input;
            }

            // Act
            var url = settings.OllamaUrl;

            // Assert
            if (string.IsNullOrEmpty(input))
            {
                url.Should().Be(BrainarrConstants.DefaultOllamaUrl);
            }
            else
            {
                url.Should().Be(expected);
            }
        }

        [Fact]
        public void AutoDetectModel_DefaultsToTrue()
        {
            // Arrange & Act
            var settings = new BrainarrSettings();

            // Assert
            settings.AutoDetectModel.Should().BeTrue();
        }
        [Fact]
        public void EffectiveSamplingShape_ReturnsDefault_WhenSamplingShapeIsNull()
        {
            var settings = new BrainarrSettings();
            typeof(BrainarrSettings).GetProperty("SamplingShape")?.SetValue(settings, null);

            settings.SamplingShape.Should().NotBeNull();
            settings.EffectiveSamplingShape.Should().BeEquivalentTo(SamplingShape.Default);
        }

    }
}
