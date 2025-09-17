using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests
{
    public class RateLimiterTests
    {
        [Fact]
        public async Task EnforcesSpacingUnderConcurrency()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var limiter = new RateLimiter(logger);
            limiter.Configure("test", 5, TimeSpan.FromSeconds(1));

            var sw = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => limiter.ExecuteAsync("test", async () => DateTime.UtcNow))
                .ToArray();

            var times = await Task.WhenAll(tasks);
            sw.Stop();

            // With token bucket semantics the last request should complete roughly one second after the first.
            var ordered = times.OrderBy(t => t).ToArray();
            var total = (ordered.Last() - ordered.First()).TotalSeconds;
            total.Should().BeGreaterThan(0.8);
            total.Should().BeLessThan(1.5);

            var gap = (ordered[5] - ordered[4]).TotalMilliseconds;
            gap.Should().BeGreaterThan(50);
        }

        [Fact]
        public async Task CancelsWhileWaiting()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var limiter = new RateLimiter(logger);
            limiter.Configure("cancel", 1, TimeSpan.FromSeconds(5));

            // Consume the first slot
            await limiter.ExecuteAsync("cancel", async ct => 1, CancellationToken.None);

            using var cts = new CancellationTokenSource(100);

            Func<Task> act = async () =>
            {
                await limiter.ExecuteAsync("cancel", async ct => 2, cts.Token);
            };

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
