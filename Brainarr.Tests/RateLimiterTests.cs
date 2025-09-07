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

            // With 5 req/s and 10 items, last completion should be ~1.8s.
            sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(1500));
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

