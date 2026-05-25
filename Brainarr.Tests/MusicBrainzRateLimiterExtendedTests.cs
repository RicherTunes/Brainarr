using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Plugin.Services.Security;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests
{
    [Collection("RateLimiterTests")]
    [Trait("Category", "Wave20")]
    public class MusicBrainzRateLimiterExtendedTests
    {
        [Fact]
        public async Task GetStats_ReflectsTotalRequestsCount()
        {
            var limiter = new MusicBrainzRateLimiter();

            limiter.GetStats().TotalRequests.Should().Be(0);

            await limiter.ExecuteWithRateLimitAsync<int>(ct => Task.FromResult(1), "ep1", CancellationToken.None);
            limiter.GetStats().TotalRequests.Should().Be(1);

            limiter.Reset();
            limiter.GetStats().TotalRequests.Should().Be(0);
        }

        [Fact]
        public async Task GetStats_RemainingRequestsThisMinute_DecreasesAfterCall()
        {
            var limiter = new MusicBrainzRateLimiter();
            var initial = limiter.GetStats().RemainingRequestsThisMinute;

            await limiter.ExecuteWithRateLimitAsync<int>(ct => Task.FromResult(1), "ep", CancellationToken.None);
            var after = limiter.GetStats().RemainingRequestsThisMinute;

            after.Should().BeLessOrEqualTo(initial);
        }

        [Fact]
        public async Task ExecuteWithRateLimit_SimpleOverload_ReturnsResult()
        {
            var limiter = new MusicBrainzRateLimiter();
            var result = await limiter.ExecuteWithRateLimitAsync(() => Task.FromResult("hi"));
            result.Should().Be("hi");
        }

        [Fact]
        public async Task ExecuteWithRateLimit_RetriesOnHttp429Once()
        {
            var limiter = new MusicBrainzRateLimiter();
            var calls = 0;

            Func<CancellationToken, Task<int>> apiCall = ct =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new HttpRequestException("Server returned 429 Too Many Requests");
                }
                return Task.FromResult(42);
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await limiter.ExecuteWithRateLimitAsync(apiCall, "endpoint", cts.Token);

            result.Should().Be(42);
            calls.Should().Be(2);
            limiter.GetStats().ThrottledRequests.Should().Be(1);
        }

        [Fact]
        public async Task ExecuteWithRateLimit_PropagatesNon429HttpError()
        {
            var limiter = new MusicBrainzRateLimiter();
            Func<CancellationToken, Task<int>> apiCall = ct => throw new HttpRequestException("500 Internal Server Error");

            Func<Task> act = async () => await limiter.ExecuteWithRateLimitAsync(apiCall, "ep", CancellationToken.None);

            await act.Should().ThrowAsync<HttpRequestException>();
        }

        [Fact]
        public async Task Reset_ClearsCountersAndTimestamps()
        {
            var limiter = new MusicBrainzRateLimiter();
            await limiter.ExecuteWithRateLimitAsync<int>(ct => Task.FromResult(1), "ep", CancellationToken.None);

            limiter.GetStats().TotalRequests.Should().BeGreaterOrEqualTo(1);

            limiter.Reset();

            var stats = limiter.GetStats();
            stats.TotalRequests.Should().Be(0);
            stats.ThrottledRequests.Should().Be(0);
            stats.RequestsLastMinute.Should().Be(0);
        }

        [Fact]
        public void CreateMusicBrainzClient_ConfiguresUserAgentAndJsonAccept()
        {
            using var client = MusicBrainzRateLimiter.CreateMusicBrainzClient();

            client.Timeout.Should().Be(TimeSpan.FromSeconds(30));
            client.DefaultRequestHeaders.UserAgent.Should().NotBeEmpty();
            client.DefaultRequestHeaders.Accept.Should().Contain(a => a.MediaType == "application/json");
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            var limiter = new MusicBrainzRateLimiter();
            var act = () => limiter.Dispose();
            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var limiter = new MusicBrainzRateLimiter();
            limiter.Dispose();
            // Second dispose should not throw.
            var act = () => limiter.Dispose();
            act.Should().NotThrow();
        }

        [Fact]
        public async Task GlobalExtension_ExecuteMusicBrainzRequestAsync_WorksFromHttpClient()
        {
            using var client = MusicBrainzRateLimiter.CreateMusicBrainzClient();
            var result = await client.ExecuteMusicBrainzRequestAsync(() => Task.FromResult(7), "ext");
            result.Should().Be(7);

            var stats = MusicBrainzRateLimiterExtensions.GetMusicBrainzRateLimitStats();
            stats.Should().NotBeNull();
            stats.TotalRequests.Should().BeGreaterOrEqualTo(1);
        }

        [Fact]
        public async Task GlobalExtension_WithCancellation_PropagatesToken()
        {
            using var client = MusicBrainzRateLimiter.CreateMusicBrainzClient();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<Task> act = async () =>
                await client.ExecuteMusicBrainzRequestAsync(ct => Task.FromResult(0), "ext", cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
