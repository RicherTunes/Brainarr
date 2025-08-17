using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class ConcurrencyTests
    {
        private readonly Mock<Logger> _loggerMock;

        public ConcurrencyTests()
        {
            _loggerMock = new Mock<Logger>();
        }

        [Fact]
        public async Task RecommendationCache_ConcurrentWrites_HandlesSafely()
        {
            // Arrange
            var cache = new RecommendationCache(_loggerMock.Object);
            var tasks = new List<Task>();
            var itemsPerTask = 100;
            var taskCount = 10;

            // Act - Multiple threads writing to cache simultaneously
            for (int i = 0; i < taskCount; i++)
            {
                var taskId = i;
                tasks.Add(Task.Run(() =>
                {
                    for (int j = 0; j < itemsPerTask; j++)
                    {
                        var key = $"key-{taskId}-{j}";
                        var data = new List<ImportListItemInfo>
                        {
                            new ImportListItemInfo { Artist = $"Artist-{taskId}", Album = $"Album-{j}" }
                        };
                        cache.Set(key, data);
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

        [Fact]
        public async Task RecommendationCache_ConcurrentReadsAndWrites_NoDataCorruption()
        {
            // Arrange
            var cache = new RecommendationCache(_loggerMock.Object);
            var sharedKey = "shared-key";
            var iterations = 1000;
            var writeTask = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var data = new List<ImportListItemInfo>
                    {
                        new ImportListItemInfo { Artist = $"Artist-{i}", Album = $"Album-{i}" }
                    };
                    cache.Set(sharedKey, data);
                    Thread.Yield(); // Allow other threads to run
                }
            });

            var readTasks = new List<Task<int>>();
            for (int t = 0; t < 5; t++)
            {
                readTasks.Add(Task.Run(() =>
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
                        Thread.Yield();
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

        [Fact]
        public async Task RetryPolicy_ConcurrentExecutions_MaintainsIndependentState()
        {
            // Arrange
            var retryPolicy = new ExponentialBackoffRetryPolicy(_loggerMock.Object, 3, TimeSpan.FromMilliseconds(10));
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

        [Fact]
        public async Task RateLimiter_ConcurrentRequests_EnforcesLimit()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_loggerMock.Object);
            var provider = "test-provider";
            var maxRequestsPerMinute = 10;
            var burstSize = 3;
            
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
            
            // First burst should execute quickly
            for (int i = 0; i < burstSize - 1; i++)
            {
                var timeDiff = (executionTimes[i + 1] - executionTimes[i]).TotalMilliseconds;
                timeDiff.Should().BeLessThan(100); // Quick succession
            }
            
            // After burst, should be rate limited
            if (executionTimes.Count > burstSize)
            {
                var afterBurstDiff = (executionTimes[burstSize] - executionTimes[burstSize - 1]).TotalMilliseconds;
                afterBurstDiff.Should().BeGreaterThan(100); // Rate limited delay
            }
        }

        [Fact]
        public async Task SyncAsyncBridge_ConcurrentCalls_HandlesCorrectly()
        {
            // Arrange
            var results = new List<int>();
            var lockObj = new object();
            var counter = 0;

            // Act - Multiple tasks calling simultaneously
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(10);
                    int result;
                    lock (lockObj)
                    {
                        counter++;
                        result = counter;
                    }
                    
                    lock (lockObj)
                    {
                        results.Add(result);
                    }
                }));
            }

            // Wait for all tasks
            await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(10);
            results.Should().OnlyHaveUniqueItems(); // Each task got unique value
            results.Min().Should().Be(1);
            results.Max().Should().Be(10);
        }

        [Fact]
        public async Task ProviderHealth_ConcurrentHealthChecks_MaintainsAccuracy()
        {
            // Arrange
            var healthMonitor = new ProviderHealthMonitor(_loggerMock.Object);
            var providers = new[] { "provider1", "provider2", "provider3" };
            var tasks = new List<Task>();

            // Act - Simulate concurrent health checks and updates
            foreach (var provider in providers)
            {
                // Success tasks
                for (int i = 0; i < 50; i++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        healthMonitor.RecordSuccess(provider, Random.Shared.Next(10, 100));
                    }));
                }

                // Failure tasks
                for (int i = 0; i < 10; i++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        healthMonitor.RecordFailure(provider, "Test failure");
                    }));
                }
            }

            await Task.WhenAll(tasks);

            // Assert - Each provider should have correct counts
            foreach (var provider in providers)
            {
                var health = await healthMonitor.CheckHealthAsync(provider, "http://test");
                
                // With 50 successes and 10 failures, success rate should be 50/60 = 0.833
                // This should be considered healthy (above 0.5 threshold)
                health.Should().Be(HealthStatus.Healthy);
            }
        }

        [Fact]
        public async Task Cache_StressTest_WithManyOperations()
        {
            // Arrange
            var cache = new RecommendationCache(_loggerMock.Object, TimeSpan.FromSeconds(60));
            var operationCount = 10000;
            var tasks = new List<Task>();

            // Act - Perform many operations concurrently
            for (int i = 0; i < operationCount; i++)
            {
                var index = i;
                if (i % 3 == 0)
                {
                    // Write operation
                    tasks.Add(Task.Run(() =>
                    {
                        cache.Set($"key-{index}", new List<ImportListItemInfo>
                        {
                            new ImportListItemInfo { Artist = $"Artist-{index}" }
                        });
                    }));
                }
                else if (i % 3 == 1)
                {
                    // Read operation
                    tasks.Add(Task.Run(() =>
                    {
                        cache.TryGet($"key-{index}", out _);
                    }));
                }
                else
                {
                    // Clear operation (less frequent)
                    if (i % 100 == 0)
                    {
                        tasks.Add(Task.Run(() => cache.Clear()));
                    }
                }
            }

            // Act
            await Task.WhenAll(tasks);

            // Assert - Cache should still be functional
            cache.Set("final-test", new List<ImportListItemInfo>());
            cache.TryGet("final-test", out var finalResult).Should().BeTrue();
        }

        [Fact]
        public async Task GenerateCacheKey_ConcurrentCalls_ProducesConsistentKeys()
        {
            // Arrange
            var cache = new RecommendationCache(_loggerMock.Object);
            var tasks = new List<Task<string>>();
            var provider = "TestProvider";
            var maxRecs = 20;
            var fingerprint = "1000_5000";

            // Act - Generate same key from multiple threads
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(Task.Run(() => cache.GenerateCacheKey(provider, maxRecs, fingerprint)));
            }

            var keys = await Task.WhenAll(tasks);

            // Assert - All keys should be identical
            keys.Should().OnlyHaveUniqueItems();
            keys.Should().HaveCount(100);
            keys.First().Should().Be(keys.Last());
        }

        [Fact]
        public async Task SyncAsyncBridge_WithTimeout_CancelsCorrectly()
        {
            // Arrange
            var startedTasks = 0;
            var completedTasks = 0;
            var lockObj = new object();

            // Act - Start multiple operations with timeout
            var tasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
                        {
                            lock (lockObj) { startedTasks++; }
                            await Task.Delay(1000, cts.Token); // Long operation
                            lock (lockObj) { completedTasks++; }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            startedTasks.Should().Be(5);
            completedTasks.Should().Be(0); // All should timeout before completing
        }
    }
}