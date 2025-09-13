using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class UrlValidatorTests
    {
        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData(" ", true)]
        public void IsValidUrl_AllowsEmpty_WhenConfigured(string input, bool expected)
        {
            UrlValidator.IsValidUrl(input, allowEmpty: true).Should().Be(expected);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData(" ", false)]
        public void IsValidUrl_RejectsEmpty_WhenNotAllowed(string input, bool expected)
        {
            UrlValidator.IsValidUrl(input, allowEmpty: false).Should().Be(expected);
        }

        [Theory]
        [InlineData("http://example.com", true)]
        [InlineData("https://example.com", true)]
        [InlineData("example.com", true)] // scheme added
        [InlineData("EXAMPLE.com", true)]
        [InlineData("http://example.com:8080", true)]
        [InlineData("http://[::1]:11434", true)] // IPv6
        [InlineData("ftp://example.com", false)]
        [InlineData("file:///etc/passwd", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("data:text/html,hi", false)]
        [InlineData("bad host", false)]
        [InlineData(".leadingdot.com", false)]
        [InlineData("trailingdot.com.", false)]
        [InlineData("no-dot-and-no-port", false)]
        public void IsValidUrl_VariousInputs_ReturnsExpected(string input, bool expected)
        {
            UrlValidator.IsValidUrl(input, allowEmpty: false).Should().Be(expected);
        }

        [Fact]
        public void IsValidUrl_PortAboveRange_IsRejected()
        {
            UrlValidator.IsValidUrl("http://example.com:70000", allowEmpty: false).Should().BeFalse();
        }

        [Theory]
        [InlineData("http://localhost:11434", true)]
        [InlineData("http://192.168.1.10:8080", true)]
        [InlineData("http://10.0.0.5", true)]
        [InlineData("http://172.16.0.2", true)]
        [InlineData("http://ollama", true)] // single-label intranet host
        [InlineData("http://[::1]:11434", true)] // IPv6
        [InlineData("bad host", false)]
        [InlineData("ftp://localhost", false)]
        [InlineData("javascript:alert(1)", false)]
        public void IsValidLocalProviderUrl_VariousInputs_ReturnsExpected(string input, bool expected)
        {
            UrlValidator.IsValidLocalProviderUrl(input).Should().Be(expected);
        }

        [Fact]
        public void IsValidUrl_InvalidEscapeSequence_DoesNotThrow()
        {
            // Uri.UnescapeDataString throws on invalid hex; implementation catches and continues.
            UrlValidator.IsValidUrl("http://exa%ZZmple.com", allowEmpty: false).Should().BeFalse();
        }

        [Theory]
        [InlineData("example.com/", "http://example.com")] // trims trailing slash to authority
        [InlineData("http://example.com/", "http://example.com")] // keeps scheme
        [InlineData("https://example.com/path", "https://example.com/path")] // preserves path
        [InlineData("ftp://example.com/file", "ftp://example.com/file")] // non-http(s) untouched
        [InlineData("not a url", "not a url")] // invalid remains as original
        public void NormalizeHttpUrlOrOriginal_Behavior(string input, string expected)
        {
            UrlValidator.NormalizeHttpUrlOrOriginal(input).Should().Be(expected);
        }
    }
}
