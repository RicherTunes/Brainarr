using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using Xunit;

namespace Brainarr.Tests.Services.Enrichment
{
    /// <summary>
    /// Resilience: MBID enrichment must PROPAGATE a cancelled run token (so the cancellation-aware
    /// orchestrator path maps cancel → empty result) rather than silently returning a partial,
    /// best-effort-enriched list as if the run completed.
    /// </summary>
    public class EnrichmentCancellationTests
    {
        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new InvalidOperationException("HTTP should not be reached when the token is already cancelled");
        }

        [Fact]
        public async Task MusicBrainzResolver_CancelledToken_ThrowsOperationCanceled()
        {
            var resolver = new MusicBrainzResolver(TestLogger.CreateNullLogger(), new HttpClient(new ThrowingHandler()));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var input = new List<Recommendation>
            {
                new Recommendation { Artist = "Radiohead", Album = "OK Computer", Confidence = 0.9 },
            };

            Func<Task> act = async () => await resolver.EnrichWithMbidsAsync(input, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task ArtistMbidResolver_CancelledToken_ThrowsOperationCanceled()
        {
            var resolver = new ArtistMbidResolver(TestLogger.CreateNullLogger(), new HttpClient(new ThrowingHandler()));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var input = new List<Recommendation>
            {
                new Recommendation { Artist = "Radiohead", Confidence = 0.9 },
            };

            Func<Task> act = async () => await resolver.EnrichArtistsAsync(input, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task MusicBrainzResolver_LiveToken_StillBestEffortOnHttpFailure()
        {
            // Regression: with a live token, a per-item HTTP failure is still swallowed best-effort
            // (original rec preserved) — only RUN cancellation propagates.
            var handler = new StubServiceUnavailable();
            var resolver = new MusicBrainzResolver(TestLogger.CreateNullLogger(), new HttpClient(handler));

            var result = await resolver.EnrichWithMbidsAsync(
                new List<Recommendation> { new Recommendation { Artist = "A", Album = "B", Confidence = 0.9 } },
                CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].ArtistMusicBrainzId.Should().BeNull();
        }

        private sealed class StubServiceUnavailable : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
        }
    }
}
