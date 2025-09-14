using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Support
{
    public class MinimalResponseParserTests
    {
        private class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Return minimal JSON depending on endpoint
                var content = "{ }";
                var url = request.RequestUri?.ToString() ?? string.Empty;
                if (url.Contains("/artist"))
                {
                    content = "{ \"artists\": [ { \"id\": \"mbid-1\", \"name\": \"Artist A\", \"tags\": [ { \"name\": \"rock\" } ] } ] }";
                }
                else if (url.Contains("/release-group"))
                {
                    content = "{ \"release-groups\": [ { \"title\": \"Album A\" } ] }";
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
            }
        }

        [Fact]
        public async Task ParseAndEnrichAsync_with_one_artist_returns_recommendation()
        {
            var http = new HttpClient(new StubHandler());
            var parser = new MinimalResponseParser(LogManager.GetCurrentClassLogger(), httpClient: http);
            var response = "[\"Artist A\"]";
            var list = await parser.ParseAndEnrichAsync(response);
            list.Should().NotBeNull();
            list.Should().HaveCount(1);
            list[0].Artist.Should().Be("Artist A");
        }

        [Fact]
        public async Task ParseAndEnrichAsync_with_no_artists_returns_empty()
        {
            var http = new HttpClient(new StubHandler());
            var parser = new MinimalResponseParser(LogManager.GetCurrentClassLogger(), httpClient: http);
            var response = "{}";
            var list = await parser.ParseAndEnrichAsync(response);
            list.Should().BeEmpty();
        }
    }
}
