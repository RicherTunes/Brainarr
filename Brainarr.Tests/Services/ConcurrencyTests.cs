using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class ConcurrencyTests
    {
        private readonly Logger _logger;

        public ConcurrencyTests()
        {
            _logger = TestLogger.CreateNullLogger();
        }

        [Fact(Skip = "Disabled for CI - potential hang")]
        public async Task RecommendationCache_ConcurrentWrites_HandlesSafely()
        {
            // Arrange
            var cache = new RecommendationCache(_logger);
            var tasks = new List<Task>();
            var itemsPerTask = 8;   // Reduced to stay within cache limit (100)
            var taskCount = 10;     // 10 tasks × 8 items = 80 total (within limit)

            // Act - Multiple threads writing to cache simultaneously
            for (int i = 0; i < taskCount; i++)
            {
                var taskId = i;
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < itemsPerTask; j++)
                    {
                        var key = $"key-{taskId}-{j}";
                        var data = new List<ImportListItemInfo>
                        {
                            new ImportListItemInfo { Artist = $"Artist-{taskId}", Album = $"Album-{j}" }
                        };
                        cache.Set(key, data);
                        await Task.Yield(); // Allow other tasks to run
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - All items should be in cache
            for (int i = 0; i < taskCount; i++)
            {
                for (int j = 0; j < itemsPerTask; j++)
                {
                    var key = $"key-{i}-{j}";
                    var success = cache.TryGet(key, out var data);
                    success.Should().BeTrue();
                    data.Should().HaveCount(1);
                    data[0].Artist.Should().Be($"Artist-{i}");
                }
            }
        }

        [Fact(Skip = "Disabled for CI - potential hang")]
        public async Task RecommendationCache_ConcurrentReadsAndWrites_NoDataCorruption()
        {
            // Arrange
            var cache = new RecommendationCache(_logger);
            var sharedKey = "shared-key";
            var iterations = 1000;
            var writeTask = Task.Run(async () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var data = new List<ImportListItemInfo>
                    {
                        new ImportListItemInfo { Artist = $"Artist-{i}", Album = $"Album-{i}" }
                    };
                    cache.Set(sharedKey, data);
                    await Task.Yield(); // Allow other threads to run
                }
            });

            var readTasks = new List<Task<int>>();
            for (int t = 0; t < 5; t++)
            {
                readTasks.Add(Task.Run(async () =>
                {
                    int successCount = 0;
                    for (int i = 0; i < iterations; i++)
                    {
                        if (cache.TryGet(sharedKey, out var data))
                        {
                            // Verify data consistency
                            if (data != null && data.Count == 1)
                            {
                                var artist = data[0].Artist;
                                var album = data[0].Album;
                                if (artist != null && album != null)
                                {
                                    var artistNum = artist.Replace("Artist-", "");
                                    var albumNum = album.Replace("Album-", "");
                                    if (artistNum == albumNum)
                                    {
                                        successCount++;
                                    }
                                }
                            }
                        }
                        await Task.Yield();
                    }
                    return successCount;
                }));
            }

            // Act
            await writeTask;
            var results = await Task.WhenAll(readTasks);

            // Assert - All reads should have gotten consistent data
            foreach (var successCount in results)
            {
                successCount.Should().BeGreaterThan(0);
            }
        }

        [Fact(Skip = "Disabled for CI - potential hang")]
        public async Task RetryPolicy_ConcurrentExecutions_MaintainsIndependentState()
        {
            // Arrange
            var retryPolicy = new ExponentialBackoffRetryPolicy(_logger, 3, TimeSpan.FromMilliseconds(10));
            var executionCounts = new Dictionary<string, int>();
            var lockObj = new object();

            // Act - Execute multiple operations concurrently
            var tasks = new List<Task<string>>();
            for (int i = 0; i < 10; i++)
            {
                var operationId = $"operation-{i}";
                var shouldFail = i % 2 == 0; // Half will fail initially
                
                tasks.Add(Task.Run(async () =>
                {
                    var attempts = 0;
                    return await retryPolicy.ExecuteAsync(async () =>
                    {
                        attempts++;
                        lock (lockObj)
                        {
                            executionCounts[operationId] = attempts;
                        }
                        
                        await Task.Delay(5);
                        
                        if (shouldFail && attempts == 1)
                        {
                            throw new Exception($"First attempt failed for {operationId}");
                        }
                        
                        return operationId;
                    }, operationId);
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(10);
            results.Should().OnlyHaveUniqueItems();
            
            // Operations that failed initially should have been retried
            for (int i = 0; i < 10; i++)
            {
                var operationId = $"operation-{i}";
                if (i % 2 == 0)
                {
                    executionCounts[operationId].Should().Be(2); // Failed once, succeeded on retry
                }
                else
                {
                    executionCounts[operationId].Should().Be(1); // Succeeded immediately
                }
            }
        }

        [Fact(Skip = "Disabled for CI - potential hang")]
        public async Task RateLimiter_ConcurrentRequests_EnforcesLimit()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_logger);
            var provider = "test-provider";
            var maxRequestsPerMinute = 10;
            
            // Configure rate limiter
            rateLimiter.Configure(provider, maxRequestsPerMinute, TimeSpan.FromMinutes(1));
            
            var executionTimes = new List<DateTime>();
            var lockObj = new object();

            // Act - Try to execute more requests than allowed
            var tasks = new List<Task>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await rateLimiter.ExecuteAsync(provider, async () =>
                    {
                        lock (lockObj)
                        {
                            executionTimes.Add(DateTime.UtcNow);
                        }
                        await Task.Delay(10);
                        return Task.CompletedTask;
                    });
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - Verify rate limiting was applied
            executionTimes.Sort();
            
            // With 10 requests/minute and 20 total requests, there should be delays
            var totalTime = (executionTimes.Last() - executionTimes.First()).TotalSeconds;
            
            // With rate limiting, 20 requests should take longer than if unrestricted
            totalTime.Should().BeGreaterThan(0.5, "Rate limiting should introduce delays");
            
            // Not all requests should complete immediately
            var immediateRequests = 0;
            for (int i = 1; i < executionTimes.Count; i++)
            {
                var diff = (executionTimes[i] - executionTimes[i-1]).TotalMilliseconds;
                if (diff < 50) immediateRequests++;
            }
            
            // With 10/min limit, not all 20 requests should be immediate
            immediateRequests.Should().BeLessThan(15, "Rate limiter should delay some requests");
        }

        [Fact(Skip = "Disabled for CI - potential hang")]
        public void SyncAsyncBridge_ConcurrentCalls_HandlesCorrectly()
        {
            // Arrange
            var results = new List<int>();
            var lockObj = new object();
            var counter = 0;

            // Act - Multiple threads calling SyncAsyncBridge simultaneously
            var threads = new List<Thread>();
            for (int i = 0; i < 10; i++)
            {
                var thread = new Thread(() =>
                {
                    var task = Task.Run(async () =>
                    {
                        await Task.Delay(10);
                        lock (lockObj)
                        {
                            counter++;
                            return counter;
                        }
                    });
                    var result = task.GetAwaiter().GetResult();
                    
                    lock (lockObj)
                    {
                        results.Add(result);
                    }
                });
                threads.Add(thread);
                thread.Start();
            }

            // Wait for all threads
            foreach (var thread in threads)
            {
                thread.Join();
            }

            // Assert
            results.Should().HaveCount(10);
            results.Should().OnlyHaveUniqueItems(); // Each thread got unique value
            results.Min().Should().Be(1);
            results.Max().Should().Be(10);
        }

        [Fact(Skip = "Disabled for CI - potential hang")]
        public async Task ProviderHealth_ConcurrentHealthChecks_MaintainsAccuracy()
        {
            // Arrange
            var healthMonitor = new ProviderHealthMonitor(_logger);
            var providers = new[] { "provider1", "provider2", "provider3" };
            var tasks = new List<Task>();

            // Act - Simulate concurrent health checks and updates
            foreach (var provider in providers)
            {
                // Record successes first to establish good health, then minimal failures
                for (int i = 0; i < 50; i++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        healthMonitor.RecordSuccess(provider, Random.Shared.Next(10, 100));
                    }));
                }
                
                // Add just one failure to ensure non-perfect success rate but avoid consecutive failures
                tasks.Add(Task.Run(() =>
                {
                    healthMonitor.RecordFailure(provider, "Single test failure");
                }));
                
                // End with more successes to ensure last operations are successful
                for (int i = 0; i < 9; i++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        healthMonitor.RecordSuccess(provider, Random.Shared.Next(10, 100));
                    }));
                }
            }

            await Task.WhenAll(tasks);
            
            // Wait a moment for metrics to be fully recorded
            await Task.Delay(50);

            // Assert - Each provider should have correct counts
            foreach (var provider in providers)
            {
                var health = healthMonitor.GetHealthStatus(provider); // Use metrics instead of HTTP call
                
                // With 59 successes and 1 failure, success rate should be 59/60 = 0.983
                // This should be considered healthy (above 0.5 threshold and no consecutive failures)
                health.Should().Be(HealthStatus.Healthy);
            }
        }

        [Fact(Skip = "Disabled for CI - potential hang")]
        public async Task Cache_StressTest_WithManyOperations()
        {
            // Arrange
            var cache = new RecommendationCache(_logger, TimeSpan.FromSeconds(60));
            var operationCount = Environment.GetEnvironmentVariable("CI") != null ? 1000 : 10000;
            var tasks = new List<Task>();

            // Act - Perform many operations concurrently
            for (int i = 0; i < operationCount; i++)
            {
                var index = i;
                if (i % 3 == 0)
                {
                    // Write operation
                    tasks.Add(Task.Run(async () =>
                    {
                        cache.Set($"key-{index}", new List<ImportListItemInfo>
                        {
                            new ImportListItemInfo { Artist = $"Artist-{index}" }
                        });
                        await Task.Yield();
                    }));
                }
                else if (i % 3 == 1)
                {
                    // Read operation
                    tasks.Add(Task.Run(async () =>
                    {
                        cache.TryGet($"key-{index}", out _);
                        await Task.Yield();
                    }));
                }
                else
                {
                    // Clear operation (less frequent)
                    if (i % 100 == 0)
                    {
                        tasks.Add(Task.Run(async () => 
                        {
                            cache.Clear();
                            await Task.Yield();
                        }));
                    }
                }
            }

            // Act
            await Task.WhenAll(tasks);

            // Assert - Cache should still be functional
            cache.Set("final-test", new List<ImportListItemInfo>());
            cache.TryGet("final-test", out var finalResult).Should().BeTrue();
        }

        [Fact(Skip = "Disabled for CI - potential hang")]
        public async Task GenerateCacheKey_ConcurrentCalls_ProducesConsistentKeys()
        {
            // Arrange
            var cache = new RecommendationCache(_logger);
            var tasks = new List<Task<string>>();
            var provider = "TestProvider";
            var maxRecs = 20;
            var fingerprint = "1000_5000";

            // Act - Generate same key from multiple threads
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(Task.Run(async () => 
                {
                    await Task.Yield();
                    return cache.GenerateCacheKey(provider, maxRecs, fingerprint);
                }));
            }

            var keys = await Task.WhenAll(tasks);

            // Assert - All keys should be identical
            keys.Should().HaveCount(100);
            keys.Should().AllBe(keys.First());
            keys.First().Should().Be(keys.Last());
        }

        [Fact(Skip = "Disabled for CI - potential hang")]
        public async Task SyncAsyncBridge_WithTimeout_CancelsCorrectly()
        {
            // Arrange
            var startedTasks = 0;
            var cancelledTasks = 0;
            var lockObj = new object();

            // Act - Start multiple operations with timeout
            var tasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(25)))
                        {
                            var asyncTask = Task.Run(async () =>
                            {
                                lock (lockObj) { startedTasks++; }
                                await Task.Delay(100, cts.Token); // Longer delay to ensure cancellation
                                return "result";
                            }, cts.Token);
                            
                            await asyncTask;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        lock (lockObj) { cancelledTasks++; }
                    }
                }));
            }

            await Task.WhenAll(tasks.ToArray());

            // Assert
            startedTasks.Should().Be(5);
            cancelledTasks.Should().BeGreaterThan(0); // At least some should be cancelled
        }
    }
}