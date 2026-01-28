using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class UrlValidatorTests
    {
        [Theory]
        [InlineData("http://localhost:11434", true)]
        [InlineData("https://127.0.0.1:11434", true)]
        [InlineData("http://[::1]:11434", true)]
        [InlineData("localhost:11434", true)] // scheme inferred
        [InlineData("myhost", false)] // no dot or port
        [InlineData("file:///etc/passwd", false)]
        [InlineData("javascript:alert(1)", false)]
        public void IsValidLocalProviderUrl_cases(string url, bool expected)
        {
            UrlValidator.IsValidLocalProviderUrl(url).Should().Be(expected);
        }

        [Theory]
        [InlineData("example.com", true)]
        [InlineData("http://example.com", true)]
        [InlineData("https://example.com:443", true)]
        [InlineData("bad scheme://example.com", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("http://example.com:70000", false)]
        public void IsValidUrl_generic(string url, bool expected)
        {
            UrlValidator.IsValidUrl(url, allowEmpty: false).Should().Be(expected);
        }

        [Theory]
        [InlineData("example.com/", "http://example.com")] // strip '/'
        [InlineData("http://example.com/", "http://example.com")] // keep authority only
        [InlineData("http://example.com/path", "http://example.com/path")] // preserve path
        [InlineData("ftp://example.com", "ftp://example.com")] // non-http left unchanged
        public void NormalizeHttpUrlOrOriginal_behaviour(string input, string expected)
        {
            UrlValidator.NormalizeHttpUrlOrOriginal(input).Should().Be(expected);
        }
    }
}
