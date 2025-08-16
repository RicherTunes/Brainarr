using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;
using VoidResult = NzbDrone.Core.ImportLists.Brainarr.Services.VoidResult;

namespace Brainarr.Tests.Services
{
    public class RateLimiterTests
    {
        private readonly Mock<Logger> _loggerMock;
        private readonly RateLimiter _rateLimiter;

        public RateLimiterTests()
        {
            _loggerMock = new Mock<Logger>();
            _rateLimiter = new RateLimiter(_loggerMock.Object);
        }

        [Fact]
        public async Task ExecuteAsync_WithinLimit_ExecutesImmediately()
        {
            // Arrange
            _rateLimiter.Configure("test", 5, TimeSpan.FromMinutes(1)); // 5 requests per minute
            var executed = false;

            // Act
            await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
            {
                executed = true;
                await Task.Delay(1);
                return VoidResult.Instance;
            });

            // Assert
            executed.Should().BeTrue();
        }

        [Fact]
        public async Task ExecuteAsync_ExceedsBurst_DelaysExecution()
        {
            // Arrange
            _rateLimiter.Configure("test", 2, TimeSpan.FromMinutes(1)); // 2 requests per minute
            var executionTimes = new List<DateTime>();

            // Act - Execute 3 requests (exceeding 2 per minute)
            for (int i = 0; i < 3; i++)
            {
                await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
                {
                    executionTimes.Add(DateTime.UtcNow);
                    await Task.Delay(1);
                    return VoidResult.Instance;
                });
            }

            // Assert
            executionTimes.Should().HaveCount(3);

            // First two should be immediate
            var firstTwoDiff = (executionTimes[1] - executionTimes[0]).TotalMilliseconds;
            firstTwoDiff.Should().BeLessThan(100);

            // Third should be delayed
            var thirdDiff = (executionTimes[2] - executionTimes[1]).TotalMilliseconds;
            thirdDiff.Should().BeGreaterThan(900); // Rate limited
        }

        [Fact]
        public async Task ExecuteAsync_DifferentProviders_IndependentLimits()
        {
            // Arrange
            _rateLimiter.Configure("provider1", 2, TimeSpan.FromMinutes(1));
            _rateLimiter.Configure("provider2", 2, TimeSpan.FromMinutes(1));

            var provider1Times = new List<DateTime>();
            var provider2Times = new List<DateTime>();

            // Act - Execute on both providers simultaneously
            var tasks = new List<Task>();

            for (int i = 0; i < 3; i++)
            {
                tasks.Add(_rateLimiter.ExecuteAsync<VoidResult>("provider1", async () =>
                {
                    provider1Times.Add(DateTime.UtcNow);
                    await Task.Delay(1);
                    return VoidResult.Instance;
                }));

                tasks.Add(_rateLimiter.ExecuteAsync<VoidResult>("provider2", async () =>
                {
                    provider2Times.Add(DateTime.UtcNow);
                    await Task.Delay(1);
                    return VoidResult.Instance;
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - Both providers should have independent rate limiting
            provider1Times.Should().HaveCount(3);
            provider2Times.Should().HaveCount(3);

            // Both should have rate limiting applied
            var provider1ThirdDiff = (provider1Times[2] - provider1Times[1]).TotalMilliseconds;
            var provider2ThirdDiff = (provider2Times[2] - provider2Times[1]).TotalMilliseconds;

            provider1ThirdDiff.Should().BeGreaterThan(900);
            provider2ThirdDiff.Should().BeGreaterThan(900);
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsValue_Correctly()
        {
            // Arrange
            _rateLimiter.Configure("test", 5, TimeSpan.FromMinutes(1));
            var expectedValue = 42;

            // Act
            var result = await _rateLimiter.ExecuteAsync("test", async () =>
            {
                await Task.Delay(1);
                return expectedValue;
            });

            // Assert
            result.Should().Be(expectedValue);
        }

        [Fact]
        public async Task ExecuteAsync_WithException_PropagatesException()
        {
            // Arrange
            _rateLimiter.Configure("test", 5, TimeSpan.FromMinutes(1));
            var expectedException = new InvalidOperationException("Test exception");

            // Act
            Func<Task> act = async () => await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
            {
                await Task.Delay(1);
                throw expectedException;
            });

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Test exception");
        }

        [Fact]
        public async Task ExecuteAsync_UnconfiguredProvider_UsesDefaults()
        {
            // Arrange
            var executed = false;

            // Act - Use provider without configuring it
            await _rateLimiter.ExecuteAsync<VoidResult>("unconfigured", async () =>
            {
                executed = true;
                await Task.Delay(1);
                return VoidResult.Instance;
            });

            // Assert
            executed.Should().BeTrue();
        }

        [Fact]
        public async Task Configure_UpdatesExistingConfiguration()
        {
            // Arrange
            _rateLimiter.Configure("test", 5, TimeSpan.FromMinutes(1)); // Initial config

            // Act - Execute some requests
            for (int i = 0; i < 5; i++)
            {
                await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
                {
                    await Task.Delay(1);
                    return VoidResult.Instance;
                });
            }

            // Reconfigure with stricter limits
            _rateLimiter.Configure("test", 2, TimeSpan.FromSeconds(4)); // Stricter: 2 requests per 4 seconds

            var stopwatch = Stopwatch.StartNew();
            await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
            {
                await Task.Delay(1);
                return VoidResult.Instance;
            });
            stopwatch.Stop();

            // Assert - Should use new configuration
            // After 5 requests with new config, should be rate limited
            stopwatch.ElapsedMilliseconds.Should().BeGreaterThan(1500); // Rate limited
        }

        [Theory]
        [InlineData(60, 1000)] // 60 per minute = 1 second between requests
        [InlineData(120, 500)] // 120 per minute = 0.5 seconds between requests
        [InlineData(30, 2000)] // 30 per minute = 2 seconds between requests
        [InlineData(600, 100)] // 600 per minute = 0.1 seconds between requests
        public async Task ExecuteAsync_RateCalculation_IsCorrect(int requestsPerMinute, int expectedDelayMs)
        {
            // Arrange
            _rateLimiter.Configure("test", requestsPerMinute, TimeSpan.FromMinutes(1)); // Configure rate per minute

            // Execute first request
            await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
            {
                await Task.Delay(1);
                return VoidResult.Instance;
            });

            // Act - Measure second request delay
            var stopwatch = Stopwatch.StartNew();
            await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
            {
                await Task.Delay(1);
                return VoidResult.Instance;
            });
            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeCloseTo(expectedDelayMs, 200); // Allow 200ms variance
        }

        [Fact]
        public async Task ExecuteAsync_BurstRefill_WorksCorrectly()
        {
            // Arrange
            _rateLimiter.Configure("test", 3, TimeSpan.FromSeconds(3)); // 3 requests per 3 seconds

            // Use up burst
            for (int i = 0; i < 3; i++)
            {
                await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
                {
                    await Task.Delay(1);
                    return VoidResult.Instance;
                });
            }

            // Wait for burst to refill
            await Task.Delay(3100);

            // Act - Should be able to burst again
            var stopwatch = Stopwatch.StartNew();
            await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
            {
                await Task.Delay(1);
                return VoidResult.Instance;
            });
            stopwatch.Stop();

            // Assert - Should execute immediately (burst refilled)
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
        }

        [Fact]
        public async Task ExecuteAsync_ConcurrentRequests_MaintainsRateLimit()
        {
            // Arrange
            _rateLimiter.Configure("test", 2, TimeSpan.FromMinutes(1)); // 2 requests per minute
            var executionTimes = new List<DateTime>();
            var lockObj = new object();

            // Act - Launch concurrent requests
            var tasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
                    {
                        lock (lockObj)
                        {
                            executionTimes.Add(DateTime.UtcNow);
                        }
                        await Task.Delay(10);
                        return VoidResult.Instance;
                    });
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            executionTimes.Sort();
            executionTimes.Should().HaveCount(5);

            // Check that rate limiting was applied
            var totalTime = (executionTimes[4] - executionTimes[0]).TotalSeconds;
            totalTime.Should().BeGreaterThan(3); // Should take at least 3 seconds for 5 requests at 2/min
        }

        [Fact]
        public void Configure_WithInvalidParameters_HandlesGracefully()
        {
            // Act & Assert - Should not throw
            Action act1 = () => _rateLimiter.Configure("test", -1, TimeSpan.FromMinutes(1)); // Negative rate
            Action act2 = () => _rateLimiter.Configure("test", 60, TimeSpan.FromSeconds(-1)); // Negative period
            Action act3 = () => _rateLimiter.Configure("test", 0, TimeSpan.Zero); // Zero values
            Action act4 = () => _rateLimiter.Configure(null, 60, TimeSpan.FromMinutes(1)); // Null provider
            Action act5 = () => _rateLimiter.Configure("", 60, TimeSpan.FromMinutes(1)); // Empty provider

            act1.Should().NotThrow();
            act2.Should().NotThrow();
            act3.Should().NotThrow();
            act4.Should().NotThrow();
            act5.Should().NotThrow();
        }

        [Fact]
        public async Task ExecuteAsync_AfterLongPause_ResetsCorrectly()
        {
            // Arrange
            _rateLimiter.Configure("test", 60, TimeSpan.FromMinutes(1)); // 60 requests per minute

            // Use up some requests
            for (int i = 0; i < 2; i++)
            {
                await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
                {
                    await Task.Delay(1);
                    return VoidResult.Instance;
                });
            }

            // Wait a bit (simulating long pause)
            await Task.Delay(100);

            // Act - Should still respect rate limit
            var stopwatch = Stopwatch.StartNew();
            await _rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
            {
                await Task.Delay(1);
                return VoidResult.Instance;
            });
            stopwatch.Stop();

            // Assert - With 60/min rate, delay should be minimal
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(50);
        }
    }
}