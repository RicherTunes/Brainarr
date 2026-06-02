using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using Xunit;

namespace Brainarr.Tests.Services.Enrichment
{
    public class ArtistMbidResolverBestEffortTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{}")
                };
                return Task.FromResult(response);
            }
        }

        // Returns a 200 with a single confident artist match for every request.
        private sealed class OkHandler : HttpMessageHandler
        {
            private readonly string _json;
            public OkHandler(string json) { _json = json; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json)
                };
                return Task.FromResult(response);
            }
        }

        // Pass-through rate limiter that records every resource key it is asked to gate.
        private sealed class SpyRateLimiter : IRateLimiter
        {
            public List<string> Resources { get; } = new List<string>();

            public Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action)
            {
                Resources.Add(resource);
                return action();
            }

            public Task<T> ExecuteAsync<T>(string resource, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
            {
                Resources.Add(resource);
                return action(cancellationToken);
            }

            public void Configure(string resource, int maxRequests, TimeSpan period)
            {
                // no-op
            }
        }

        [Fact]
        public async Task EnrichArtistsAsync_PreservesOriginalRecommendation_WhenLookupFails()
        {
            // Arrange
            var httpClient = new HttpClient(new StubHandler());
            var resolver = new ArtistMbidResolver(TestLogger.CreateNullLogger(), httpClient);
            var rec = new Recommendation { Artist = "Radiohead", Confidence = 0.9 };

            // Act
            var result = await resolver.EnrichArtistsAsync(new List<Recommendation> { rec }, CancellationToken.None);

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Radiohead");
            result[0].ArtistMusicBrainzId.Should().BeNull();
        }

        [Fact]
        public async Task EnrichArtistsAsync_RoutesMusicBrainzQuery_ThroughRateLimiter()
        {
            // Arrange: the fallback MusicBrainz GET (no AttachSearchService) must be gated by the
            // shared "musicbrainz" rate-limiter bucket (1 req/s) — parity with MusicBrainzResolver.
            var handler = new OkHandler("{\"artists\":[{\"id\":\"a-1\",\"name\":\"Bicep\",\"score\":100}]}");
            var spy = new SpyRateLimiter();
            var resolver = new ArtistMbidResolver(TestLogger.CreateNullLogger(), new HttpClient(handler), spy);
            var recs = new List<Recommendation>
            {
                new Recommendation { Artist = "Bicep", Confidence = 0.9 },
                new Recommendation { Artist = "Goldie", Confidence = 0.9 }
            };

            // Act
            var result = await resolver.EnrichArtistsAsync(recs, CancellationToken.None);

            // Assert: every per-artist fallback query went through the limiter under "musicbrainz",
            // and the resolved MBID flowed back onto the recommendation.
            spy.Resources.Should().OnlyContain(r => r == "musicbrainz");
            spy.Resources.Should().HaveCount(2);
            result.Should().HaveCount(2);
            result[0].ArtistMusicBrainzId.Should().Be("a-1");
        }
    }
}
