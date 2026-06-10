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
        // Percent-encoded colon in the AUTHORITY ("%3a" between host and port). This is rejected, and
        // rejection is the correct, more-secure behavior:
        //   * .NET 8 Uri.TryCreate("http://localhost%3a11434", Absolute) returns FALSE outright — per
        //     RFC 3986 the authority (host[:port]) component may not be percent-encoded, so the parser
        //     treats it as malformed (it does NOT decode it to the host "localhost" on port 11434).
        //   * Literally interpreted the authority is the nonsensical host string "localhost%3a11434";
        //     no Ollama server is reachable at it. The real URL a user wants is "http://localhost:11434".
        //   * Accepting an encoded authority is a parser-confusion / SSRF-evasion vector: a "decode-first"
        //     validator would see a different (and possibly allow-listed) host than the raw string the
        //     HTTP client actually dials. SecureUrlValidator.IsSafeProviderUrl parses the raw string (no
        //     decode) and rejects it, matching what Lidarr's HttpClient would do at request time.
        [InlineData("http://localhost%3a11434", false)] // Encoded-authority (percent-encoded colon) — malformed, rejected
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
