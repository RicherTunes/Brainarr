using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Brainarr.Plugin.Services.Core;
using FluentAssertions;
using NLog;
using Brainarr.Tests.Helpers;
using Xunit;

namespace Brainarr.Tests.Security
{
    public class SecurityTestSuite
    {
        private readonly Logger _logger;

        public SecurityTestSuite()
        {
            _logger = TestLogger.CreateNullLogger();
        }

        [Fact]
        public async Task RateLimiter_Should_PreventRaceConditions()
        {
            // Arrange
            var limiter = new RateLimiter(_logger);
            limiter.Configure("TestResource", 5, TimeSpan.FromSeconds(1));

            var executionTimes = new List<DateTime>();
            var tasks = new List<Task>();

            // Act - Try to execute 10 requests simultaneously
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(limiter.ExecuteAsync("TestResource", async () =>
                {
                    lock (executionTimes)
                    {
                        executionTimes.Add(DateTime.UtcNow);
                    }
                    await Task.Delay(10);
                    return true;
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - Verify rate limiting worked
            executionTimes.Should().HaveCount(10);

            // First 5 should execute in burst (allow more time for test environment)
            var firstBatch = executionTimes.Take(5).ToList();
            var timeDiff = (firstBatch.Max() - firstBatch.Min()).TotalMilliseconds;
            var ci = Environment.GetEnvironmentVariable("CI") != null;
            var firstBatchUpper = ci ? 2000 : 120000; // Allow large headroom on slower environments
            timeDiff.Should().BeLessThan(firstBatchUpper);

            // Next 5 should be delayed by rate limiting
            var secondBatch = executionTimes.Skip(5).Take(5).ToList();
            var delayBetweenBatches = (secondBatch.Min() - firstBatch.Max()).TotalMilliseconds;
            var minDelay = ci ? 50 : 10;
            delayBetweenBatches.Should().BeGreaterThan(minDelay);
        }

        [Fact]
        public async Task RateLimiter_Should_HandleCancellation()
        {
            // Arrange
            var limiter = new RateLimiter(_logger);
            limiter.Configure("Slow", 1, TimeSpan.FromSeconds(5));

            using var cts = new CancellationTokenSource();

            // Act
            var task1 = limiter.ExecuteAsync("Slow", async () =>
            {
                await Task.Delay(100);
                return "first";
            });

            await task1; // First completes

            var task2 = limiter.ExecuteAsync("Slow", async () =>
            {
                await Task.Delay(100, cts.Token);
                return "second";
            });

            cts.Cancel(); // Cancel while waiting

            // Assert - TaskCanceledException derives from OperationCanceledException
            await Assert.ThrowsAsync<TaskCanceledException>(() => task2);
        }

        [Fact]
        public async Task ConcurrentCache_Should_PreventCacheStampede()
        {
            // Arrange
            var cache = new Brainarr.Plugin.Services.Core.ConcurrentCache<string, string>(maxSize: 100);
            var factoryCallCount = 0;
            var tasks = new List<Task<string>>();

            // Act - 100 threads try to get same key simultaneously
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(cache.GetOrAddAsync("key1", async key =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    await Task.Delay(100); // Simulate expensive operation
                    return "value1";
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert - Factory should only be called once (cache stampede prevented)
            factoryCallCount.Should().Be(1);
            results.Should().AllBeEquivalentTo("value1");
        }

        [Fact]
        public async Task ConcurrentCache_Should_EvictLeastRecentlyUsed()
        {
            // Arrange
            var cache = new Brainarr.Plugin.Services.Core.ConcurrentCache<int, string>(maxSize: 3);

            // Act - Add 4 items to cache with max size 3
            await cache.GetOrAddAsync(1, k => Task.FromResult("one"));
            await cache.GetOrAddAsync(2, k => Task.FromResult("two"));
            await cache.GetOrAddAsync(3, k => Task.FromResult("three"));

            // Access item 1 to make it recently used
            cache.TryGet(1, out _);

            // Add 4th item - should evict item 2 (least recently used)
            await cache.GetOrAddAsync(4, k => Task.FromResult("four"));

            // Assert
            cache.TryGet(1, out var val1).Should().BeTrue();
            cache.TryGet(2, out var val2).Should().BeFalse(); // Evicted
            cache.TryGet(3, out var val3).Should().BeTrue();
            cache.TryGet(4, out var val4).Should().BeTrue();
        }

        [Fact]
        public async Task ConcurrentCache_Should_HandleExpiration()
        {
            // Arrange — use 200ms TTL with 600ms wait (3x margin) for CI reliability.
            // The previous 100ms/300ms was too tight on slow CI runners.
            var cache = new Brainarr.Plugin.Services.Core.ConcurrentCache<string, string>(
                defaultExpiration: TimeSpan.FromMilliseconds(200));

            // Act
            await cache.GetOrAddAsync("key1", k => Task.FromResult("value1"));

            // Should get from cache
            var cached = cache.TryGet("key1", out var value1);
            cached.Should().BeTrue();
            value1.Should().Be("value1");

            // Wait for expiration with generous margin for slow CI
            await Task.Delay(600);

            // Should not get from cache (expired)
            var expired = cache.TryGet("key1", out var value2);
            expired.Should().BeFalse();
        }

    }

    [Trait("Category", "Performance")]
    public class PerformanceTestSuite
    {
        private readonly Logger _logger;

        public PerformanceTestSuite()
        {
            _logger = TestLogger.CreateNullLogger();
        }

        [Fact]
        public async Task RateLimiter_Should_HandleHighConcurrency()
        {
            // Arrange
            var limiter = new RateLimiter(_logger);
            var tasks = new List<Task<int>>();
            var random = new Random();

            // Act - Simulate 1000 concurrent requests across 10 resources
            for (int i = 0; i < 1000; i++)
            {
                var resource = $"Resource{i % 10}";
                var taskId = i;
                tasks.Add(limiter.ExecuteAsync(resource, async () =>
                {
                    await Task.Delay(random.Next(1, 10));
                    return taskId;
                }));
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            results.Should().HaveCount(1000);
            results.Distinct().Should().HaveCount(1000);
            var perfUpper = Environment.GetEnvironmentVariable("CI") != null ? 15000 : 120000;
            sw.ElapsedMilliseconds.Should().BeLessThan(perfUpper); // Should complete in reasonable time

            // Statistics validation - basic RateLimiter doesn't expose stats
            // Test passes if no exceptions were thrown during execution
        }

        [Fact]
        [Trait("State", "Quarantined")] // OOM-crashes the test host under memory pressure
        public async Task Cache_Should_HandleMillionOperations()
        {
            // Arrange
            var cache = new Brainarr.Plugin.Services.Core.ConcurrentCache<int, string>(maxSize: 10000);
            var tasks = new List<Task>();
            var random = new Random();

            // Act - Stress test cache operations (reduced for CI performance)
            var iterations = Environment.GetEnvironmentVariable("CI") != null ? 10000 : 1000000;
            for (int i = 0; i < iterations; i++)
            {
                var key = random.Next(0, 20000); // Some keys will repeat
                if (i % 3 == 0)
                {
                    // Write operation
                    tasks.Add(Task.Run(() =>
                        cache.Set(key, $"value{key}")));
                }
                else
                {
                    // Read operation
                    tasks.Add(Task.Run(() =>
                        cache.TryGet(key, out _)));
                }

                // Process in batches to avoid too many tasks
                if (tasks.Count >= 1000)
                {
                    await Task.WhenAll(tasks);
                    tasks.Clear();
                }
            }

            await Task.WhenAll(tasks);

            // Assert
            var stats = cache.GetStatistics();
            stats.Size.Should().BeLessThanOrEqualTo(10000); // Respects max size

            // Read operations occur for ~2/3 of iterations (writes are 1/3)
            var expectedReads = iterations - (iterations / 3);
            (stats.Hits + stats.Misses)
                .Should()
                .BeGreaterThanOrEqualTo(expectedReads - 100, "most reads should complete");
        }
    }
}
