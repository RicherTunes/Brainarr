using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Plugin.Services.Security;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests
{
    [Collection("RateLimiterTests")]
    public class MusicBrainzRateLimiterTests
    {
        [Fact]
        public async Task EnforcesOneRequestPerSecond()
        {
            var limiter = new MusicBrainzRateLimiter();

            var sw = Stopwatch.StartNew();
            await limiter.ExecuteWithRateLimitAsync<int>(ct => Task.FromResult(0), "t1", CancellationToken.None);
            await limiter.ExecuteWithRateLimitAsync<int>(ct => Task.FromResult(0), "t2", CancellationToken.None);
            sw.Stop();
            sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
        }

        [Fact]
        public async Task CancelsDuringWait()
        {
            var limiter = new MusicBrainzRateLimiter();

            // First call consumes the current slot
            await limiter.ExecuteWithRateLimitAsync<int>(ct => Task.FromResult(0), "first", CancellationToken.None);

            using var cts = new CancellationTokenSource(50);
            Func<Task> act = async () =>
            {
                await limiter.ExecuteWithRateLimitAsync(ct => Task.FromResult(1), "second", cts.Token);
            };

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
