using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Brainarr.Plugin.Services.Core;
using FluentAssertions;
using Moq;
using NLog;
using Xunit;

namespace Brainarr.Tests.Security
{
    public class SecurityTestSuite
    {
        private readonly Mock<ILogger> _loggerMock = new Mock<ILogger>();
        private readonly ILogger _logger;

        public SecurityTestSuite()
        {
            _logger = _loggerMock.Object;
        }
        
        [Fact]
        public async Task RateLimiter_Should_PreventRaceConditions()
        {
            // Arrange
            var limiter = new RateLimiter((Logger)_logger);
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
            
            // First 5 should execute immediately
            var firstBatch = executionTimes.Take(5).ToList();
            var timeDiff = (firstBatch.Max() - firstBatch.Min()).TotalMilliseconds;
            timeDiff.Should().BeLessThan(100); // Should be nearly simultaneous

            // Next 5 should be delayed
            var secondBatch = executionTimes.Skip(5).Take(5).ToList();
            var delayBetweenBatches = (secondBatch.Min() - firstBatch.Max()).TotalMilliseconds;
            delayBetweenBatches.Should().BeGreaterThan(900); // Should wait ~1 second
        }

        [Fact]
        public async Task RateLimiter_Should_HandleCancellation()
        {
            // Arrange
            var limiter = new RateLimiter((Logger)_logger);
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

            // Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() => task2);
        }

        [Fact]
        public void SecureProvider_Should_DetectSqlInjection()
        {
            // Arrange
            var provider = new TestSecureProvider(_logger);
            var maliciousProfile = new LibraryProfile
            {
                TopGenres = new Dictionary<string, int> 
                { 
                    { "Rock", 10 },
                    { "Pop'; DROP TABLE Artists; --", 5 },
                    { "Jazz", 8 }
                }
            };

            // Act & Assert
            var ex = Assert.Throws<SecurityException>(() =>
                provider.ValidateInputPublic(maliciousProfile, 10));
            
            ex.Message.Should().Contain("SQL injection");
        }

        [Fact]
        public void SecureProvider_Should_DetectXssAttacks()
        {
            // Arrange
            var provider = new TestSecureProvider(_logger);
            var maliciousProfile = new LibraryProfile
            {
                TopArtists = new List<string> 
                { 
                    "The Beatles",
                    "<script>alert('XSS')</script>",
                    "Pink Floyd"
                }
            };

            // Act & Assert
            var ex = Assert.Throws<SecurityException>(() =>
                provider.ValidateInputPublic(maliciousProfile, 10));
            
            ex.Message.Should().Contain("script injection");
        }

        [Fact]
        public void SecureProvider_Should_SanitizeRecommendations()
        {
            // Arrange
            var provider = new TestSecureProvider(_logger);
            var maliciousRecs = new List<Recommendation>
            {
                new Recommendation
                {
                    Artist = "Artist<script>alert('xss')</script>Name",
                    Album = "Album'; DROP TABLE--",
                    Genre = new string('A', 200), // Too long
                    Reason = "Contains\x00null\x01bytes",
                    Confidence = 2.5, // Out of range
                    MusicBrainzId = "not-a-guid",
                    ReleaseYear = 2050 // Future year
                }
            };

            // Act
            var sanitized = provider.SanitizeRecommendationsPublic(maliciousRecs);

            // Assert
            var rec = sanitized.First();
            rec.Artist.Should().Be("ArtistName");
            rec.Album.Should().Be("Album'; DROP TABLE--");
            rec.Genre.Length.Should().BeLessThanOrEqualTo(100);
            rec.Reason.Should().NotContain("\x00");
            rec.Confidence.Should().Be(1.0); // Clamped to max
            rec.MusicBrainzId.Should().BeNull();
            rec.ReleaseYear.Should().BeNull();
        }

        [Fact]
        public void SecureProvider_Should_RedactSensitiveLogging()
        {
            // Arrange
            var provider = new TestSecureProvider(_logger);
            var sensitiveMessage = @"
                API Key: sk-1234567890abcdef1234567890abcdef
                Email: user@example.com
                IP: 192.168.1.1
                Credit Card: 4111 1111 1111 1111
                Password: secret123
                Token: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9
            ";

            // Act
            var sanitized = provider.SanitizeForLoggingPublic(sensitiveMessage);

            // Assert
            sanitized.Should().NotContain("sk-1234567890");
            sanitized.Should().NotContain("user@example.com");
            sanitized.Should().NotContain("192.168.1.1");
            sanitized.Should().NotContain("4111");
            sanitized.Should().NotContain("secret123");
            sanitized.Should().Contain("[REDACTED-KEY]");
            sanitized.Should().Contain("[REDACTED-EMAIL]");
            sanitized.Should().Contain("[REDACTED-IP]");
            sanitized.Should().Contain("[REDACTED-CC]");
            sanitized.Should().Contain("[REDACTED-PASSWORD]");
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
            // Arrange
            var cache = new Brainarr.Plugin.Services.Core.ConcurrentCache<string, string>(
                defaultExpiration: TimeSpan.FromMilliseconds(100));

            // Act
            await cache.GetOrAddAsync("key1", k => Task.FromResult("value1"));
            
            // Should get from cache
            var cached = cache.TryGet("key1", out var value1);
            cached.Should().BeTrue();
            value1.Should().Be("value1");

            // Wait for expiration
            await Task.Delay(150);

            // Should not get from cache (expired)
            var expired = cache.TryGet("key1", out var value2);
            expired.Should().BeFalse();
        }

        [Theory]
        [InlineData("SELECT * FROM users WHERE id = 1 OR 1=1")]
        [InlineData("'; DROP TABLE Artists; --")]
        [InlineData("\" OR \"\"=\"\"")]
        [InlineData("admin'--")]
        [InlineData("' UNION SELECT * FROM passwords --")]
        public void SqlInjectionPatterns_Should_BeDetected(string maliciousInput)
        {
            // Arrange
            var provider = new TestSecureProvider(_logger);
            var profile = new LibraryProfile
            {
                TopGenres = new Dictionary<string, int> { { maliciousInput, 1 } }
            };

            // Act & Assert
            Assert.Throws<SecurityException>(() =>
                provider.ValidateInputPublic(profile, 10));
        }

        [Theory]
        [InlineData("<script>alert('XSS')</script>")]
        [InlineData("<iframe src='evil.com'></iframe>")]
        [InlineData("javascript:alert(1)")]
        [InlineData("<img src=x onerror=alert(1)>")]
        [InlineData("<svg onload=alert(1)>")]
        public void XssPatterns_Should_BeDetected(string maliciousInput)
        {
            // Arrange
            var provider = new TestSecureProvider(_logger);
            var profile = new LibraryProfile
            {
                TopArtists = new List<string> { maliciousInput }
            };

            // Act & Assert
            Assert.Throws<SecurityException>(() =>
                provider.ValidateInputPublic(profile, 10));
        }

        private class TestSecureProvider : SecureProviderBase
        {
            public override string ProviderName => "TestProvider";
            public override bool RequiresApiKey => false;
            public override bool SupportsStreaming => false;
            public override int MaxRecommendations => 50;

            public TestSecureProvider(ILogger logger) : base(logger) { }

            protected override Task<List<Recommendation>> GetRecommendationsInternalAsync(
                LibraryProfile profile, 
                int maxRecommendations, 
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new List<Recommendation>());
            }

            protected override Task<bool> TestConnectionInternalAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(true);
            }

            public override async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
            {
                return await Task.FromResult(new List<Recommendation>());
            }

            public override async Task<bool> TestConnectionAsync()
            {
                return await Task.FromResult(true);
            }

            public override void UpdateModel(string modelName)
            {
                // Test implementation
            }

            // Expose protected methods for testing
            public void ValidateInputPublic(LibraryProfile profile, int maxRecommendations)
                => ValidateInput(profile, maxRecommendations);

            public List<Recommendation> SanitizeRecommendationsPublic(List<Recommendation> recommendations)
                => SanitizeRecommendations(recommendations);

            public string SanitizeForLoggingPublic(string message)
                => SanitizeForLogging(message);
        }
    }

    [Trait("Category", "Performance")]
    public class PerformanceTestSuite
    {
        private readonly Mock<ILogger> _loggerMock = new Mock<ILogger>();
        private readonly ILogger _logger;

        public PerformanceTestSuite()
        {
            _logger = _loggerMock.Object;
        }

        [Fact]
        public async Task RateLimiter_Should_HandleHighConcurrency()
        {
            // Arrange
            var limiter = new RateLimiter((Logger)_logger);
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
            sw.ElapsedMilliseconds.Should().BeLessThan(5000); // Should complete quickly
            
            // Statistics validation - basic RateLimiter doesn't expose stats
            // Test passes if no exceptions were thrown during execution
        }

        [Fact]
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
            (stats.Hits + stats.Misses).Should().BeGreaterThan(600000); // Most reads completed
        }
    }
}