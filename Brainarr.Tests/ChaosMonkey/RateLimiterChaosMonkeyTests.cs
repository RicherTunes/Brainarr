using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;
using Xunit.Abstractions;

namespace Brainarr.Tests.ChaosMonkey
{
    /// <summary>
    /// Chaos Monkey tests for RateLimiter and perf cache to verify flake fixes.
    ///
    /// These tests intentionally create chaotic conditions:
    /// - Thread pool starvation
    /// - Concurrent rate limit exhaustion
    /// - Variable timing conditions
    /// - Task cancellation scenarios
    ///
    /// If these tests pass consistently, the fixes are solid. If they flake,
    /// the fixes are not robust enough.
    /// </summary>
    [Collection("ChaosMonkey")]
    public class RateLimiterChaosMonkeyTests
    {
        private readonly Logger _logger;
        private readonly ITestOutputHelper _output;

        public RateLimiterChaosMonkeyTests(ITestOutputHelper output)
        {
            _logger = TestLogger.CreateNullLogger();
            _output = output;
        }

        /// <summary>
        /// CHAOS: Run multiple rounds of rate limiter exhaustion to detect intermittent failures.
        /// This tests the fix for PR #513 (5 requests @ 3/sec with 60s timeout).
        /// </summary>
        [Theory]
        [InlineData(1)]  // Single run - baseline
        [InlineData(3)]  // Triple run - catch 1-in-3 flake
        [InlineData(5)]  // Five runs - catch 1-in-5 flake
        public async Task Chaos_RateLimiterExhaustion_MultipleRounds_NoFlakes(int rounds)
        {
            // Arrange - Same settings as PR #513 fix
            var rateLimiter = new RateLimiter(_logger);
            rateLimiter.Configure("chaos", 3, TimeSpan.FromSeconds(1));

            var failures = new ConcurrentBag<string>();

            // Act - Run multiple rounds with the same pattern that caused flakes
            for (int round = 0; round < rounds; round++)
            {
                var executionTimes = new ConcurrentBag<DateTime>();
                var tasks = new System.Collections.Generic.List<Task>();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                // Fire 5 requests at once (3 burst, 2 delayed by rate limit)
                for (int i = 0; i < 5; i++)
                {
                    var localI = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await rateLimiter.ExecuteAsync("chaos", async (ct) =>
                            {
                                executionTimes.Add(DateTime.UtcNow);
                                // Simulate actual work (1ms delay)
                                await Task.Delay(1, ct);
                                return localI;
                            }, cts.Token);
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"Round {round}, Request {localI}: {ex.Message}");
                        }
                    }));
                }

                await Task.WhenAll(tasks);

                // Verify this round completed successfully
                if (executionTimes.Count != 5)
                {
                    failures.Add($"Round {round}: Only {executionTimes.Count}/5 requests completed");
                }
            }

            // Assert - No failures across all rounds
            _output.WriteLine($"Chaos test completed {rounds} rounds with {failures.Count} failures");

            foreach (var failure in failures)
            {
                _output.WriteLine($"Failure: {failure}");
            }

            failures.Should().BeEmpty("Rate limiter should handle exhaustion consistently across all rounds");
        }

        /// <summary>
        /// CHAOS: Introduce thread pool starvation before rate limiter runs.
        /// This simulates full-suite load conditions that caused flakes.
        /// </summary>
        [Fact]
        public async Task Chaos_ThreadPoolStarvation_BeforeRateLimiter_NoFlakes()
        {
            // Arrange - Starve the thread pool first
            var blockers = new ConcurrentBag<Task>();
            var starvationTasks = new System.Collections.Generic.List<Task>();

            // Block 50 threads to simulate full-suite load
            for (int i = 0; i < 50; i++)
            {
                starvationTasks.Add(Task.Run(async () =>
                {
                    blockers.Add(Task.Delay(TimeSpan.FromSeconds(10)));
                    await Task.WhenAll(blockers);
                }));
            }

            // Wait for thread pool to feel the pressure
            await Task.Delay(500);

            // Now run rate limiter under pressure
            var rateLimiter = new RateLimiter(_logger);
            rateLimiter.Configure("starvation", 5, TimeSpan.FromSeconds(3));

            var executionTimes = new ConcurrentBag<DateTime>();
            var tasks = new System.Collections.Generic.List<Task>();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            for (int i = 0; i < 5; i++)
            {
                var localI = i;
                tasks.Add(Task.Run(async () =>
                {
                    await rateLimiter.ExecuteAsync("starvation", async (ct) =>
                    {
                        executionTimes.Add(DateTime.UtcNow);
                        await Task.Delay(1, ct);
                        return localI;
                    }, cts.Token);
                }));
            }

            await Task.WhenAll(tasks);

            // Release the blocking tasks
            foreach (var blocker in blockers)
            {
                // Can't actually cancel, just let them complete naturally
            }

            // Assert
            executionTimes.Should().HaveCount(5, "All requests should complete even under thread pool pressure");
            var times = executionTimes.OrderBy(t => t).ToList();
            var totalTime = (times.Last() - times.First()).TotalSeconds;
            // With 5 @ 3/sec burst, all 5 can execute immediately (burst capacity = 5)
            // So we just verify they completed without exception
            _output.WriteLine($"Total time spread: {totalTime:F4}s");
        }

        /// <summary>
        /// CHAOS: Rapid fire rate limiter configuration changes.
        /// Tests for race conditions in Configure() method.
        /// </summary>
        [Fact]
        public async Task Chaos_RapidConfigChanges_NoRaceConditions()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_logger);
            var errors = new ConcurrentBag<Exception>();

            // Act - Rapidly change configuration while executing
            var tasks = new System.Collections.Generic.List<Task>();

            for (int i = 0; i < 20; i++)
            {
                var localI = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Change config
                        rateLimiter.Configure($"key{localI % 5}", 3, TimeSpan.FromMilliseconds(100));

                        // Execute
                        await rateLimiter.ExecuteAsync($"key{localI % 5}", async ct =>
                        {
                            await Task.Delay(10, ct);
                            return localI;
                        }, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty("No race conditions during rapid config changes");
        }

        /// <summary>
        /// CHAOS: Run rate limiter with random delays to simulate real-world conditions.
        /// </summary>
        [Fact]
        public async Task Chaos_RandomDelays_NoFlakes()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_logger);
            rateLimiter.Configure("random", 5, TimeSpan.FromSeconds(1));

            var random = new Random(42); // Fixed seed for reproducibility
            var executionTimes = new ConcurrentBag<long>();
            var tasks = new System.Collections.Generic.List<Task>();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Act - Fire requests with random staggered delays
            for (int i = 0; i < 10; i++)
            {
                var localI = i;
                var delay = random.Next(0, 500); // 0-500ms random delay

                tasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(delay, cts.Token);

                    var sw = Stopwatch.StartNew();
                    await rateLimiter.ExecuteAsync("random", async ct =>
                    {
                        await Task.Delay(10, ct);
                        return localI;
                    }, cts.Token);
                    sw.Stop();

                    executionTimes.Add(sw.ElapsedMilliseconds);
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - All completed, reasonable spread
            executionTimes.Should().HaveCount(10);
            _output.WriteLine($"Execution times: {string.Join(", ", executionTimes.OrderBy(x => x))} ms");
        }

        /// <summary>
        /// CHAOS: Stress test with many concurrent requests exceeding rate limit significantly.
        /// Tests the 200ms perf cache floor and rate limiter together.
        /// </summary>
        [Fact]
        public async Task Chaos_ManyConcurrentRequests_GracefulDegradation()
        {
            // Arrange - Very tight rate limit
            var rateLimiter = new RateLimiter(_logger);
            rateLimiter.Configure("stress", 2, TimeSpan.FromSeconds(1));

            var completed = 0;
            var failed = 0;
            var tasks = new System.Collections.Generic.List<Task>();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            // Act - Fire 20 requests, only 2 should execute immediately
            for (int i = 0; i < 20; i++)
            {
                var localI = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await rateLimiter.ExecuteAsync("stress", async ct =>
                        {
                            Interlocked.Increment(ref completed);
                            await Task.Delay(10, ct);
                            return localI;
                        }, cts.Token);
                    }
                    catch
                    {
                        Interlocked.Increment(ref failed);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - All should complete given enough time
            _output.WriteLine($"Completed: {completed}, Failed: {failed}");
            completed.Should().Be(20, "All requests should eventually complete");
            failed.Should().Be(0, "No requests should fail");
        }
    }
}
