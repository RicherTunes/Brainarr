using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Parser.Model;
using Brainarr.Tests.Helpers;
using Xunit;

namespace Brainarr.Tests.EdgeCases
{
    /// <summary>
    /// Enhanced concurrency and stress tests for Brainarr components.
    /// Tests thread safety, race conditions, and performance under load.
    /// </summary>
    public class EnhancedConcurrencyTests
    {
        private readonly Mock<Logger> _loggerMock;

        public EnhancedConcurrencyTests()
        {
            _loggerMock = new Mock<Logger>();
        }

        #region Cache Concurrency Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task RecommendationCache_ConcurrentReadWrite_MaintainsDataIntegrity()
        {
            // Arrange
            var cache = new RecommendationCache(_loggerMock.Object);
            var threadCount = 50;
            var operationsPerThread = 100;
            var errors = new ConcurrentBag<Exception>();
            var successfulWrites = 0;
            var successfulReads = 0;

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(() =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    try
                    {
                        var key = $"key_{threadId}_{i % 10}"; // Some keys will collide
                        
                        if (i % 2 == 0)
                        {
                            // Write operation
                            var data = TestDataGenerator.GenerateImportListItems(1);
                            cache.Set(key, data);
                            Interlocked.Increment(ref successfulWrites);
                        }
                        else
                        {
                            // Read operation
                            if (cache.TryGet(key, out var data))
                            {
                                Interlocked.Increment(ref successfulReads);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty("Cache operations should be thread-safe");
            successfulWrites.Should().BeGreaterThan(0);
            successfulReads.Should().BeGreaterThan(0);
        }

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task RecommendationCache_ConcurrentEviction_HandlesGracefully()
        {
            // Arrange
            var cache = new RecommendationCache(_loggerMock.Object);
            var maxEntries = 100; // Assume cache has a limit
            var threadCount = 20;
            var errors = new ConcurrentBag<Exception>();

            // Act - Fill cache beyond capacity from multiple threads
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(() =>
            {
                for (int i = 0; i < maxEntries; i++)
                {
                    try
                    {
                        var key = $"thread_{threadId}_item_{i}";
                        var data = TestDataGenerator.GenerateImportListItems(1);
                        cache.Set(key, data);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty("Cache eviction should handle concurrent access");
        }

        #endregion

        #region Rate Limiter Concurrency Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task RateLimiter_ConcurrentRequests_EnforcesLimits()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_loggerMock.Object);
            var maxRequests = 10;
            var windowSeconds = 1;
            var totalRequests = 100;
            var successCount = 0;
            var blockedCount = 0;

            // Configure rate limiter
            rateLimiter.Configure("test", maxRequests, TimeSpan.FromSeconds(windowSeconds));

            // Act
            var tasks = Enumerable.Range(0, totalRequests).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await rateLimiter.ExecuteAsync("test", async () =>
                    {
                        Interlocked.Increment(ref successCount);
                        await Task.Delay(10); // Simulate work
                        return Task.FromResult<object>(null);
                    });
                }
                catch (Exception ex) when (ex.Message.Contains("Rate limit"))
                {
                    Interlocked.Increment(ref blockedCount);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            successCount.Should().BeLessThanOrEqualTo(maxRequests * 2, 
                "Rate limiter should enforce limits (with some tolerance for timing)");
            blockedCount.Should().BeGreaterThan(0, "Some requests should be rate limited");
            (successCount + blockedCount).Should().Be(totalRequests);
        }

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task RateLimiter_ThunderingHerd_HandlesGracefully()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_loggerMock.Object);
            rateLimiter.Configure("api", 5, TimeSpan.FromSeconds(1));
            
            var clientCount = 100;
            var barrier = new Barrier(clientCount);
            var results = new ConcurrentBag<bool>();

            // Act - All clients start at exactly the same time
            var tasks = Enumerable.Range(0, clientCount).Select(_ => Task.Run(async () =>
            {
                barrier.SignalAndWait(); // Synchronize all threads
                
                try
                {
                    await rateLimiter.ExecuteAsync("api", async () =>
                    {
                        await Task.Delay(10);
                        results.Add(true);
                        return Task.FromResult<object>(null);
                    });
                }
                catch (Exception ex) when (ex.Message.Contains("Rate limit"))
                {
                    results.Add(false);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var successCount = results.Count(r => r);
            successCount.Should().BeLessThanOrEqualTo(10, "Rate limiter should handle thundering herd");
            results.Count.Should().Be(clientCount);
        }

        #endregion

        #region Provider Health Monitor Concurrency Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task ProviderHealthMonitor_ConcurrentHealthChecks_MaintainsConsistency()
        {
            // Arrange
            var healthMonitor = new ProviderHealthMonitor(_loggerMock.Object);
            var providers = Enumerable.Range(0, 10).Select(i => $"provider_{i}").ToList();
            var threadCount = 20;
            var errors = new ConcurrentBag<Exception>();

            // Act - Concurrent health updates
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(() =>
            {
                var random = new Random(threadId);
                for (int i = 0; i < 100; i++)
                {
                    try
                    {
                        var provider = providers[random.Next(providers.Count)];
                        
                        if (random.Next(2) == 0)
                        {
                            healthMonitor.RecordSuccess(provider, random.Next(10, 100));
                        }
                        else
                        {
                            healthMonitor.RecordFailure(provider, "Test failure");
                        }
                        
                        // Also query health status
                        var isHealthy = healthMonitor.IsHealthy(provider);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty("Health monitor should be thread-safe");
            
            // Verify all providers have some health status
            foreach (var provider in providers)
            {
                healthMonitor.GetHealthStatus(provider).Should().BeDefined();
            }
        }

        #endregion

        #region Iterative Strategy Concurrency Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task IterativeStrategy_ConcurrentIterations_HandlesCorrectly()
        {
            // Arrange
            var mockPromptBuilder = new Mock<LibraryAwarePromptBuilder>(Mock.Of<ILibraryAnalyzer>());
            var strategy = new IterativeRecommendationStrategy(_loggerMock.Object, mockPromptBuilder.Object);
            
            var mockProvider = new Mock<IAIProvider>();
            var recommendations = TestDataGenerator.GenerateRecommendations(20);
            
            mockProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() => recommendations.Take(5).ToList());
            
            mockPromptBuilder.Setup(p => p.BuildLibraryAwarePrompt(
                It.IsAny<LibraryProfile>(),
                It.IsAny<List<NzbDrone.Core.Music.Artist>>(),
                It.IsAny<List<NzbDrone.Core.Music.Album>>(),
                It.IsAny<NzbDrone.Core.ImportLists.Brainarr.BrainarrSettings>(),
                It.IsAny<bool>()))
                .Returns("test prompt");

            var settings = TestDataGenerator.GenerateSettings();
            var profile = new LibraryProfile
            {
                TopGenres = new Dictionary<string, int> { { "Rock", 50 } },
                TopArtists = new List<string> { "Artist1" },
                TotalAlbums = 100,
                TotalArtists = 50
            };

            // Act - Multiple concurrent strategy executions
            var tasks = Enumerable.Range(0, 10).Select(_ => 
                strategy.GetIterativeRecommendationsAsync(
                    mockProvider.Object,
                    profile,
                    new List<NzbDrone.Core.Music.Artist>(),
                    new List<NzbDrone.Core.Music.Album>(),
                    settings,
                    false
                )
            ).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(10);
            results.Should().OnlyContain(r => r != null && r.Count > 0);
        }

        #endregion

        #region Validation Concurrency Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task RecommendationValidator_ConcurrentValidation_ThreadSafe()
        {
            // Arrange
            var validator = new RecommendationValidator(_loggerMock.Object);
            var recommendations = TestDataGenerator.GenerateRecommendations(100);
            var errors = new ConcurrentBag<Exception>();
            var results = new ConcurrentBag<bool>();

            // Act - Validate recommendations concurrently
            var tasks = recommendations.Select(rec => Task.Run(() =>
            {
                try
                {
                    var isValid = validator.ValidateRecommendation(rec);
                    results.Add(isValid);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty("Validator should be thread-safe");
            results.Should().HaveCount(recommendations.Count);
        }

        #endregion

        #region Stress Tests

        [Fact]
        [Trait("Category", "Stress")]
        public async Task StressTest_HighConcurrency_SystemRemainsStable()
        {
            // Arrange
            var cache = new RecommendationCache(_loggerMock.Object);
            var rateLimiter = new RateLimiter(_loggerMock.Object);
            var healthMonitor = new ProviderHealthMonitor(_loggerMock.Object);
            
            rateLimiter.Configure("stress", 100, TimeSpan.FromSeconds(1));
            
            var threadCount = Environment.ProcessorCount * 4;
            var operationsPerThread = 1000;
            var errors = new ConcurrentBag<Exception>();
            var startTime = DateTime.UtcNow;

            // Act - Stress test with multiple components
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
            {
                var random = new Random(threadId);
                
                for (int i = 0; i < operationsPerThread; i++)
                {
                    try
                    {
                        // Random operation
                        switch (random.Next(4))
                        {
                            case 0: // Cache operation
                                var cacheKey = $"stress_{threadId}_{i}";
                                if (random.Next(2) == 0)
                                {
                                    cache.Set(cacheKey, TestDataGenerator.GenerateImportListItems(1));
                                }
                                else
                                {
                                    cache.TryGet(cacheKey, out _);
                                }
                                break;
                                
                            case 1: // Rate limiter operation
                                try
                                {
                                    await rateLimiter.ExecuteAsync("stress", async () =>
                                    {
                                        await Task.Delay(1);
                                        return Task.FromResult<object>(null);
                                    });
                                }
                                catch (Exception ex) when (ex.Message.Contains("Rate limit"))
                                {
                                    // Expected
                                }
                                break;
                                
                            case 2: // Health monitor operation
                                var provider = $"provider_{random.Next(10)}";
                                if (random.Next(2) == 0)
                                {
                                    healthMonitor.RecordSuccess(provider, random.Next(10, 100));
                                }
                                else
                                {
                                    healthMonitor.RecordFailure(provider, "Stress test");
                                }
                                break;
                                
                            case 3: // Combined operation
                                var combinedKey = $"combined_{threadId}";
                                cache.TryGet(combinedKey, out _);
                                healthMonitor.IsHealthy($"provider_{threadId % 10}");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            var duration = DateTime.UtcNow - startTime;

            // Assert
            errors.Should().BeEmpty("System should remain stable under stress");
            duration.Should().BeLessThan(TimeSpan.FromSeconds(30), 
                "Stress test should complete in reasonable time");
        }

        [Fact]
        [Trait("Category", "Stress")]
        public async Task StressTest_MemoryPressure_HandlesGracefully()
        {
            // Arrange
            var cache = new RecommendationCache(_loggerMock.Object);
            var largeDataSets = new ConcurrentBag<List<ImportListItemInfo>>();
            var errors = new ConcurrentBag<Exception>();

            // Act - Create large amounts of data
            var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            {
                try
                {
                    // Generate large recommendation sets
                    var largeSet = TestDataGenerator.GenerateImportListItems(1000);
                    largeDataSets.Add(largeSet);
                    
                    // Try to cache them
                    cache.Set(Guid.NewGuid().ToString(), largeSet);
                    
                    // Force some garbage collection pressure
                    if (largeDataSets.Count % 10 == 0)
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                }
                catch (OutOfMemoryException)
                {
                    // Expected under extreme pressure
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            // We don't expect OutOfMemoryException to be in errors collection
            errors.Should().NotContain(e => !(e is OutOfMemoryException), 
                "Only OutOfMemoryException is acceptable under memory pressure");
            largeDataSets.Should().NotBeEmpty("Some operations should succeed");
        }

        #endregion
    }
}