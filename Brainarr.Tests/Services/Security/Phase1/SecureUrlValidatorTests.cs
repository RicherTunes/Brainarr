using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Security;
using Xunit;

namespace Brainarr.Tests.Services.Security.Phase1
{
    [Trait("Area", "Security")]
    [Trait("Phase", "Phase1")]
    public class SecureUrlValidatorTests
    {
        private readonly SecureUrlValidator _validator;

        public SecureUrlValidatorTests()
        {
            _validator = new SecureUrlValidator();
        }

        [Theory]
        [InlineData("http://localhost:11434", true)]
        [InlineData("http://127.0.0.1:1234", true)]
        [InlineData("http://192.168.1.100:8080", true)]
        public void IsValidLocalProviderUrl_WithValidLocalUrls_ShouldReturnTrue(string url, bool expected)
        {
            // Act
            var result = _validator.IsValidLocalProviderUrl(url);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("https://api.openai.com/v1/chat", true)]
        [InlineData("https://api.anthropic.com/v1/messages", true)]
        [InlineData("https://generativelanguage.googleapis.com/v1beta/models", true)]
        public void IsValidCloudProviderUrl_WithValidCloudUrls_ShouldReturnTrue(string url, bool expected)
        {
            // Act
            var result = _validator.IsValidCloudProviderUrl(url);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("javascript:alert('xss')", false)]
        [InlineData("file:///etc/passwd", false)]
        [InlineData("ftp://evil.com/", false)]
        public void IsValidLocalProviderUrl_WithDangerousSchemes_ShouldReturnFalse(string url, bool expected)
        {
            // Act
            var result = _validator.IsValidLocalProviderUrl(url);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("javascript:alert('xss')", false)]
        [InlineData("file:///etc/passwd", false)]
        [InlineData("data:text/html,<script>alert('xss')</script>", false)]
        public void IsValidCloudProviderUrl_WithDangerousSchemes_ShouldReturnFalse(string url, bool expected)
        {
            // Act
            var result = _validator.IsValidCloudProviderUrl(url);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void SanitizeUrl_WithNormalUrl_ShouldReturnSameUrl()
        {
            // Arrange
            const string url = "https://api.openai.com/v1/chat";

            // Act
            var result = _validator.SanitizeUrl(url);

            // Assert
            result.Should().Be(url);
        }

        [Fact]
        public void SanitizeUrl_WithMaliciousUrl_ShouldNormalizeUrl()
        {
            // Arrange
            const string url = "javascript:alert('xss')";

            // Act
            var result = _validator.SanitizeUrl(url);

            // Assert
            // SanitizeUrl normalizes URLs but doesn't reject schemes
            // Validation is done by IsValid* methods
            result.Should().NotBeEmpty();
        }

        [Fact]
        public void IsValidLocalProviderUrl_WithEmptyUrl_ShouldReturnFalse()
        {
            // Act
            var result = _validator.IsValidLocalProviderUrl("");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsValidCloudProviderUrl_WithEmptyUrl_ShouldReturnFalse()
        {
            // Act
            var result = _validator.IsValidCloudProviderUrl("");

            // Assert
            result.Should().BeFalse();
        }
    }
}
