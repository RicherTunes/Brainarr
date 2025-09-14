using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Unit")]
    public class UrlSanitizerAdditionalTests
    {
        [Fact]
        public void SanitizeUrl_Fallback_Removes_Query_And_Fragment_When_Invalid()
        {
            var bad = "http://exa%ZZmple.com/path?api_key=ABC123#frag";
            var sanitized = UrlSanitizer.SanitizeUrl(bad);
            sanitized.Should().Be("http://exa%ZZmple.com/path");
        }

        [Fact]
        public void SanitizeApiUrl_Masks_Common_Tokens_In_Path()
        {
            var api = "https://service.test/api/token=ABC123/resource?id=1";
            var sanitized = UrlSanitizer.SanitizeApiUrl(api);
            sanitized.Should().StartWith("https://service.test");
            sanitized.Should().Contain("/api/");
            sanitized.Should().Contain("token=***");
        }
    }
}

