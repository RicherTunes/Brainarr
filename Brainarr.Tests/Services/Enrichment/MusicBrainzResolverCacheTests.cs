using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using Xunit;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;

namespace Brainarr.Tests.Services.Enrichment
{
    public class MusicBrainzResolverCacheTests
    {
        private class StubHandler : HttpMessageHandler
        {
            private int _calls;
            private readonly string _content;

            public StubHandler(string content)
            {
                _content = content;
            }

            public int Calls => _calls;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_content)
                };
                return Task.FromResult(response);
            }
        }

        [Fact]
        public async Task EnrichWithMbidsAsync_UsesInProcessCache_ForDuplicateItems()
        {
            // Arrange
            var mbJson = "{\n  \"release-groups\": [\n    {\n      \"id\": \"rg-1\",\n      \"score\": 100,\n      \"title\": \"OK Computer\",\n      \"artist-credit\": [{\"artist\": {\"name\": \"Radiohead\", \"id\": \"artist-123\"}}],\n      \"first-release-date\": \"1997-06-16\"\n    }\n  ]\n}";
            var handler = new StubHandler(mbJson);
            var httpClient = new HttpClient(handler);
            var logger = TestLogger.CreateNullLogger();
            var resolver = new MusicBrainzResolver(logger, httpClient);

            var rec1 = new Recommendation { Artist = "Radiohead", Album = "OK Computer", Confidence = 0.9 };
            var rec2 = new Recommendation { Artist = "Radiohead", Album = "OK Computer", Confidence = 0.8 };
            var input = new List<Recommendation> { rec1, rec2 };

            // Act
            var result = await resolver.EnrichWithMbidsAsync(input, CancellationToken.None);

            // Assert
            result.Should().HaveCount(2);
            handler.Calls.Should().Be(1); // Second item served from in-process cache
        }
    }
}
