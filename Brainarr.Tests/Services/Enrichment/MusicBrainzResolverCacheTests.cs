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
            private readonly HttpStatusCode _statusCode;

            public StubHandler(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
            {
                _content = content;
                _statusCode = statusCode;
            }

            public int Calls => _calls;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);
                var response = new HttpResponseMessage(_statusCode)
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

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public string LastUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastUri = request.RequestUri?.ToString();
                // Empty result set → no match; we only care about the outgoing query here.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"release-groups\":[]}")
                });
            }
        }

        [Fact]
        public async Task EnrichWithMbidsAsync_QueriesMusicBrainz_WithRawAmpersand_NotHtmlEncoded()
        {
            // Regression (#60): an artist like "Simon & Garfunkel" must reach MusicBrainz with the
            // ampersand intact (escaped to %26), NOT HTML-encoded as "&amp;" (which would escape to
            // %26amp%3B and never match). The sanitizer no longer encodes '&'; this pins the query side.
            var handler = new CapturingHandler();
            var resolver = new MusicBrainzResolver(TestLogger.CreateNullLogger(), new HttpClient(handler));

            await resolver.EnrichWithMbidsAsync(
                new List<Recommendation> { new Recommendation { Artist = "Simon & Garfunkel", Album = "Bookends", Confidence = 0.9 } },
                CancellationToken.None);

            handler.LastUri.Should().NotBeNull();
            handler.LastUri.Should().Contain("%26", "the ampersand must be URL-escaped, not dropped");
            handler.LastUri.Should().NotContain("amp%3B", "the name must not be HTML-encoded to &amp; before the query");
            handler.LastUri.Should().NotContain("amp;");
        }

        [Fact]
        public async Task EnrichWithMbidsAsync_DecodesModelEmittedEntity_BeforeQuery()
        {
            // Defense-in-depth: even if a model emits the entity-encoded "Simon &amp; Garfunkel", the
            // resolver HtmlDecodes before querying/matching, so the MusicBrainz query carries the real
            // ampersand (%26) — not "%26amp%3B" which would never match.
            var handler = new CapturingHandler();
            var resolver = new MusicBrainzResolver(TestLogger.CreateNullLogger(), new HttpClient(handler));

            await resolver.EnrichWithMbidsAsync(
                new List<Recommendation> { new Recommendation { Artist = "Simon &amp; Garfunkel", Album = "Bookends", Confidence = 0.9 } },
                CancellationToken.None);

            handler.LastUri.Should().NotBeNull();
            handler.LastUri.Should().Contain("%26");
            handler.LastUri.Should().NotContain("amp%3B", "a model-emitted &amp; must be decoded before the query");
            handler.LastUri.Should().NotContain("amp;");
        }

        [Fact]
        public async Task EnrichWithMbidsAsync_CacheKey_CollapsesAcrossEntityEncoding()
        {
            // Regression (#66, heuristic-s residual of #60): the LRU cache key must be built from the
            // SAME HtmlDecoded text the query/match use — otherwise "Simon & Garfunkel" and the
            // entity-encoded "Simon &amp; Garfunkel" (which resolve to the IDENTICAL MusicBrainz entity)
            // key differently, so the second is a cache MISS and fires a redundant network query.
            var mbJson = "{\n  \"release-groups\": [\n    {\n      \"id\": \"rg-sg\",\n      \"score\": 100,\n      \"title\": \"Bookends\",\n      \"artist-credit\": [{\"artist\": {\"name\": \"Simon & Garfunkel\", \"id\": \"artist-sg\"}}],\n      \"first-release-date\": \"1968-04-03\"\n    }\n  ]\n}";
            var handler = new StubHandler(mbJson);
            var resolver = new MusicBrainzResolver(TestLogger.CreateNullLogger(), new HttpClient(handler));

            var input = new List<Recommendation>
            {
                new Recommendation { Artist = "Simon & Garfunkel", Album = "Bookends", Confidence = 0.9 },
                new Recommendation { Artist = "Simon &amp; Garfunkel", Album = "Bookends", Confidence = 0.8 },
            };

            var result = await resolver.EnrichWithMbidsAsync(input, CancellationToken.None);

            result.Should().HaveCount(2);
            result[0].AlbumMusicBrainzId.Should().Be("rg-sg");
            result[1].AlbumMusicBrainzId.Should().Be("rg-sg", "the entity-encoded duplicate must resolve to the same entity");
            handler.Calls.Should().Be(1, "both encodings normalize to one cache key → a single MusicBrainz query");
        }

        [Fact]
        public async Task EnrichWithMbidsAsync_PreservesOriginalRecommendation_WhenLookupFails()
        {
            // Arrange
            var handler = new StubHandler("{}", HttpStatusCode.ServiceUnavailable);
            var httpClient = new HttpClient(handler);
            var logger = TestLogger.CreateNullLogger();
            var resolver = new MusicBrainzResolver(logger, httpClient);

            var rec = new Recommendation { Artist = "Radiohead", Album = "OK Computer", Confidence = 0.9 };

            // Act
            var result = await resolver.EnrichWithMbidsAsync(new List<Recommendation> { rec }, CancellationToken.None);

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Radiohead");
            result[0].Album.Should().Be("OK Computer");
            result[0].ArtistMusicBrainzId.Should().BeNull();
            result[0].AlbumMusicBrainzId.Should().BeNull();
        }
    }
}
