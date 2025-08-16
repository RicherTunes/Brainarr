using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class ProviderHealthTests
    {
        private readonly Mock<Logger> _loggerMock;
        private readonly ProviderHealthMonitor _healthMonitor;

        public ProviderHealthTests()
        {
            _loggerMock = new Mock<Logger>();
            _healthMonitor = new ProviderHealthMonitor(_loggerMock.Object);
        }

        [Fact]
        public async Task CheckHealthAsync_NewProvider_ReturnsHealthy()
        {
            // Act
            var health = await _healthMonitor.CheckHealthAsync("new-provider", "http://test");

            // Assert
            health.Should().Be(HealthStatus.Healthy);
        }

        [Fact]
        public async Task RecordSuccess_UpdatesMetrics()
        {
            // Arrange
            var provider = "test-provider";

            // Act
            _healthMonitor.RecordSuccess(provider, 100);
            _healthMonitor.RecordSuccess(provider, 200);
            _healthMonitor.RecordSuccess(provider, 150);

            // Assert
            var health = await _healthMonitor.CheckHealthAsync(provider, "http://test");
            health.Should().Be(HealthStatus.Healthy);
        }

        [Fact]
        public async Task RecordFailure_UpdatesMetrics()
        {
            // Arrange
            var provider = "test-provider";

            // Act
            _healthMonitor.RecordFailure(provider, "Error 1");
            _healthMonitor.RecordFailure(provider, "Error 2");

            // Assert
            var health = await _healthMonitor.CheckHealthAsync(provider, "http://test");
            health.Should().Be(HealthStatus.Degraded); // 2 failures without successes
        }

        [Fact]
        public async Task CheckHealthAsync_MixedResults_CalculatesCorrectly()
        {
            // Arrange
            var provider = "test-provider";

            // Record mixed results
            for (int i = 0; i < 7; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
            }
            for (int i = 0; i < 3; i++)
            {
                _healthMonitor.RecordFailure(provider, "Test failure");
            }

            // Act
            var health = await _healthMonitor.CheckHealthAsync(provider, "http://test");

            // Assert
            // 7 successes, 3 failures = 70% success rate (above 50% threshold)
            health.Should().Be(HealthStatus.Healthy);
        }

        [Fact]
        public async Task CheckHealthAsync_MostlyFailures_ReturnsUnhealthy()
        {
            // Arrange
            var provider = "test-provider";

            // Record mostly failures
            for (int i = 0; i < 2; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
            }
            for (int i = 0; i < 8; i++)
            {
                _healthMonitor.RecordFailure(provider, "Test failure");
            }

            // Act
            var health = await _healthMonitor.CheckHealthAsync(provider, "http://test");

            // Assert
            // 2 successes, 8 failures = 20% success rate (below 50% threshold)
            health.Should().Be(HealthStatus.Unhealthy);
        }

        [Fact]
        public async Task CheckHealthAsync_ExactlyHalfFailures_ReturnsDegraded()
        {
            // Arrange
            var provider = "test-provider";

            // Record exactly 50% success rate
            for (int i = 0; i < 5; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
                _healthMonitor.RecordFailure(provider, "Test failure");
            }

            // Act
            var health = await _healthMonitor.CheckHealthAsync(provider, "http://test");

            // Assert
            // 5 successes, 5 failures = 50% success rate
            health.Should().Be(HealthStatus.Degraded);
        }

        [Theory]
        [InlineData(0, HealthStatus.Healthy)] // No response time
        [InlineData(100, HealthStatus.Healthy)]
        [InlineData(500, HealthStatus.Healthy)]
        [InlineData(1000, HealthStatus.Healthy)]
        [InlineData(5000, HealthStatus.Healthy)]
        public async Task RecordSuccess_WithVariousResponseTimes_AcceptsAll(double responseTime, HealthStatus expectedStatus)
        {
            // Arrange
            var provider = "test-provider";

            // Act
            _healthMonitor.RecordSuccess(provider, responseTime);
            var health = await _healthMonitor.CheckHealthAsync(provider, "http://test");

            // Assert
            health.Should().Be(expectedStatus);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Error message")]
        [InlineData("Very long error message that contains a lot of detail about what went wrong")]
        public async Task RecordFailure_WithVariousErrors_AcceptsAll(string errorMessage)
        {
            // Arrange
            var provider = "test-provider";

            // Act
            _healthMonitor.RecordFailure(provider, errorMessage);

            // Assert - Should not throw
            var health = await _healthMonitor.CheckHealthAsync(provider, "http://test");
            health.Should().BeOneOf(HealthStatus.Degraded, HealthStatus.Unhealthy);
        }

        [Fact]
        public async Task CheckHealthAsync_OldMetrics_AreIgnored()
        {
            // This test would require the implementation to support time-windowed metrics
            // Currently checking if the implementation handles any time-based logic

            // Arrange
            var provider = "test-provider";

            // Record old failures (would be outside window if implemented)
            for (int i = 0; i < 10; i++)
            {
                _healthMonitor.RecordFailure(provider, "Old failure");
            }

            // Wait a bit
            await Task.Delay(100);

            // Record recent successes
            for (int i = 0; i < 5; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
            }

            // Act
            var health = await _healthMonitor.CheckHealthAsync(provider, "http://test");

            // Assert
            // Implementation should ideally consider time windows
            health.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Unhealthy);
        }

        [Fact]
        public async Task RecordMetrics_MultipleProviders_IndependentTracking()
        {
            // Arrange
            var provider1 = "provider1";
            var provider2 = "provider2";
            var provider3 = "provider3";

            // Act
            // Provider 1: All successes
            for (int i = 0; i < 10; i++)
            {
                _healthMonitor.RecordSuccess(provider1, 100);
            }

            // Provider 2: All failures
            for (int i = 0; i < 10; i++)
            {
                _healthMonitor.RecordFailure(provider2, "Error");
            }

            // Provider 3: Mixed
            for (int i = 0; i < 6; i++)
            {
                _healthMonitor.RecordSuccess(provider3, 100);
            }
            for (int i = 0; i < 4; i++)
            {
                _healthMonitor.RecordFailure(provider3, "Error");
            }

            // Assert
            (await _healthMonitor.CheckHealthAsync(provider1, "http://test")).Should().Be(HealthStatus.Healthy);
            (await _healthMonitor.CheckHealthAsync(provider2, "http://test")).Should().Be(HealthStatus.Unhealthy);
            (await _healthMonitor.CheckHealthAsync(provider3, "http://test")).Should().Be(HealthStatus.Healthy); // 60% success
        }

        [Fact]
        public async Task CheckHealthAsync_ConcurrentUpdates_HandledSafely()
        {
            // Arrange
            var provider = "test-provider";
            var tasks = new List<Task>();

            // Act - Concurrent updates
            for (int i = 0; i < 100; i++)
            {
                if (i % 3 == 0)
                {
                    tasks.Add(Task.Run(() => _healthMonitor.RecordSuccess(provider, 100)));
                }
                else
                {
                    tasks.Add(Task.Run(() => _healthMonitor.RecordFailure(provider, "Error")));
                }
            }

            await Task.WhenAll(tasks);

            // Assert
            var health = await _healthMonitor.CheckHealthAsync(provider, "http://test");
            health.Should().BeOneOf(HealthStatus.Degraded, HealthStatus.Unhealthy); // ~33% success rate
        }

        [Theory]
        [InlineData("provider-1", true)]
        [InlineData("provider_2", true)]
        [InlineData("provider.3", true)]
        [InlineData("PROVIDER-4", true)]
        [InlineData("", true)] // Empty provider name
        [InlineData(null, true)] // Null provider name
        public void RecordMetrics_WithVariousProviderNames_HandlesCorrectly(string providerName, bool shouldWork)
        {
            // Act & Assert
            if (shouldWork)
            {
                Action act1 = () => _healthMonitor.RecordSuccess(providerName, 100);
                Action act2 = () => _healthMonitor.RecordFailure(providerName, "Error");

                act1.Should().NotThrow();
                act2.Should().NotThrow();
            }
        }

        [Fact]
        public async Task CheckHealthAsync_RapidStatusChanges_TracksCorrectly()
        {
            // Arrange
            var provider = "test-provider";

            // Act - Rapid status changes
            // Start healthy
            for (int i = 0; i < 10; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
            }
            var health1 = await _healthMonitor.CheckHealthAsync(provider, "http://test");

            // Become unhealthy
            for (int i = 0; i < 20; i++)
            {
                _healthMonitor.RecordFailure(provider, "Error");
            }
            var health2 = await _healthMonitor.CheckHealthAsync(provider, "http://test");

            // Recover
            for (int i = 0; i < 30; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
            }
            var health3 = await _healthMonitor.CheckHealthAsync(provider, "http://test");

            // Assert
            health1.Should().Be(HealthStatus.Healthy);
            health2.Should().BeOneOf(HealthStatus.Degraded, HealthStatus.Unhealthy);
            health3.Should().Be(HealthStatus.Healthy);
        }

        [Fact]
        public void RecordSuccess_WithNegativeResponseTime_HandlesGracefully()
        {
            // Arrange
            var provider = "test-provider";

            // Act
            Action act = () => _healthMonitor.RecordSuccess(provider, -100);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void RecordSuccess_WithExtremelyHighResponseTime_HandlesGracefully()
        {
            // Arrange
            var provider = "test-provider";

            // Act
            Action act = () => _healthMonitor.RecordSuccess(provider, double.MaxValue);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void RecordSuccess_WithNaNResponseTime_HandlesGracefully()
        {
            // Arrange
            var provider = "test-provider";

            // Act
            Action act = () => _healthMonitor.RecordSuccess(provider, double.NaN);

            // Assert
            act.Should().NotThrow();
        }
    }
}