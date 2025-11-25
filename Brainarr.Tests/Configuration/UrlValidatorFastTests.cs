using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class UrlValidatorFastTests
    {
        [Theory]
        [InlineData("http://example.com", true)]
        [InlineData("https://example.com", true)]
        [InlineData("example.com", true)]
        [InlineData("example", false)]
        [InlineData("ftp://example.com", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("http://exa mple.com", false)]
        public void IsValidUrl_BasicCases(string url, bool expected)
        {
            UrlValidator.IsValidUrl(url).Should().Be(expected);
        }

        [Theory]
        [InlineData("localhost:11434", true)]
        [InlineData("http://localhost:11434", true)]
        [InlineData("10.0.0.1:3000", true)]
        [InlineData("172.16.0.1", true)]
        [InlineData("http://exa mple.com", false)]
        [InlineData("file:///etc/passwd", false)]
        public void IsValidLocalProviderUrl_CommonCases(string url, bool expected)
        {
            UrlValidator.IsValidLocalProviderUrl(url).Should().Be(expected);
        }

        [Theory]
        [InlineData("example.com/", "http://example.com")]  // trims trailing slash
        [InlineData("https://example.com/", "https://example.com")] // keeps scheme
        [InlineData("https://example.com/path", "https://example.com/path")] // preserves path
        [InlineData("mailto:me@example.com", "mailto:me@example.com")] // leaves non-http intact
        [InlineData("not a url", "not a url")] // leaves invalid untouched
        public void NormalizeHttpUrlOrOriginal_Behavior(string input, string expected)
        {
            UrlValidator.NormalizeHttpUrlOrOriginal(input).Should().Be(expected);
        }
    }
}
