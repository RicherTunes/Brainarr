using Xunit;
using NzbDrone.Core.ImportLists.Brainarr;

namespace Brainarr.Tests.Configuration
{
    public class BrainarrSettingsValidatorAdvancedTests
    {
        private readonly BrainarrSettingsValidator _validator;

        public BrainarrSettingsValidatorAdvancedTests()
        {
            _validator = new BrainarrSettingsValidator();
        }

        [Theory]
        [InlineData("http://localhost%3a11434", true)] // Encoded characters
        [InlineData("http://user:pass@evil.com", true)] // User info in URL (valid per URI spec, but potentially risky)
        [InlineData("file:///C:/Users/user/secrets.txt", false)] // File path
        [InlineData("javascript:alert('xss')", false)] // Javascript injection
        public void BeValidUrl_WithMaliciousUrls_ReturnsExpected(string url, bool expected)
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = url
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            Assert.Equal(expected, result.IsValid);
        }

        [Fact]
        public void Validate_ApiKey_WithVeryLongKey_IsValid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = new string('a', 500) // Very long API key
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            Assert.True(result.IsValid); // No length validation on API key
        }

        [Fact]
        public void Validate_ApiKey_WithSpecialCharacters_IsValid()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "sk-!@#$%^&*()_+-=[]{}|;:'\",./<>?"
            };

            // Act
            var result = _validator.Validate(settings);

            // Assert
            Assert.True(result.IsValid);
        }
    }
}
