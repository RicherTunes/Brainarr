using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace NzbDrone.Core.ImportLists.Brainarr.Tests.Configuration
{
    [Trait("Category", "Unit")]
    public class UrlValidatorTests
    {
        [Theory]
        [InlineData("javascript:alert(1)")] 
        [InlineData("file:///etc/passwd")]
        [InlineData("data:text/html;base64,AAAA")]
        [InlineData("ftp://example.com/file.txt")]
        public void Dangerous_Schemes_Are_Rejected(string url)
        {
            UrlValidator.IsValidUrl(url, allowEmpty: false).Should().BeFalse();
        }

        [Theory]
        [InlineData("http://localhost:11434")]
        [InlineData("https://127.0.0.1:5000")]
        [InlineData("http://192.168.1.20:8080")]
        [InlineData("http://[::1]:11434")]
        public void Local_Provider_Urls_Are_Valid(string url)
        {
            UrlValidator.IsValidLocalProviderUrl(url).Should().BeTrue();
        }

        [Theory]
        [InlineData("localhost:11434")]
        [InlineData("example.local:8080")]
        public void Missing_Scheme_Inferred_For_Local(string url)
        {
            UrlValidator.IsValidUrl(url, allowEmpty: false).Should().BeTrue();
        }

        [Fact]
        public void Port_Out_Of_Range_Is_Rejected()
        {
            UrlValidator.IsValidUrl("http://localhost:70000", allowEmpty: false).Should().BeFalse();
            UrlValidator.IsValidLocalProviderUrl("http://localhost:70000").Should().BeFalse();
        }

        [Fact]
        public void NormalizeHttpUrlOrOriginal_Works_As_Expected()
        {
            UrlValidator.NormalizeHttpUrlOrOriginal("localhost:11434").Should().Be("http://localhost:11434");
            UrlValidator.NormalizeHttpUrlOrOriginal("https://api.openai.com/v1/chat").Should().Be("https://api.openai.com/v1/chat");
            // Non-http scheme is preserved (not rewritten)
            UrlValidator.NormalizeHttpUrlOrOriginal("ftp://example.com/file.txt").Should().Be("ftp://example.com/file.txt");
            // Clearly non-URL input is preserved
            UrlValidator.NormalizeHttpUrlOrOriginal("not a url").Should().Be("not a url");
        }
    }
}
