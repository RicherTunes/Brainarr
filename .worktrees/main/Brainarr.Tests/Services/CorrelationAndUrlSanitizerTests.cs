using System;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class CorrelationAndUrlSanitizerTests
    {
        [Fact]
        public void CorrelationScope_restores_previous()
        {
            var original = CorrelationContext.Current;
            string innerId;
            using (var scope = new CorrelationScope("test-corr"))
            {
                CorrelationContext.Current.Should().Be("test-corr");
                innerId = scope.CorrelationId;
            }
            innerId.Should().Be("test-corr");
            CorrelationContext.Current.Should().NotBeNull();
        }

        [Fact]
        public void UrlSanitizer_removes_query_and_fragment()
        {
            var url = "https://api.example.com/path/resource?api_key=SECRET#frag";
            var sanitized = UrlSanitizer.SanitizeUrl(url);
            sanitized.Should().Be("https://api.example.com/path/resource");

            var api = UrlSanitizer.SanitizeApiUrl("https://api.example.com/api/v1?token=ABC123");
            api.Should().Contain("api");
            api.Should().NotContain("ABC123");
        }
    }
}
