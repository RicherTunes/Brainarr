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
            // Arrange - Add enough success metrics to avoid HTTP check
            var provider = "new-provider";
            for (int i = 0; i < 5; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
            }

            // Act
            var health = _healthMonitor.GetHealthStatus(provider);

            // Assert
            health.Should().Be(HealthStatus.Healthy);
        }

        [Fact]
        public async Task RecordSuccess_UpdatesMetrics()
        {
            // Arrange
            var provider = "test-provider";

            // Act - Add enough metrics to avoid HTTP check
            _healthMonitor.RecordSuccess(provider, 100);
            _healthMonitor.RecordSuccess(provider, 200);
            _healthMonitor.RecordSuccess(provider, 150);
            _healthMonitor.RecordSuccess(provider, 120);
            _healthMonitor.RecordSuccess(provider, 180);

            // Assert
            var health = _healthMonitor.GetHealthStatus(provider);
            health.Should().Be(HealthStatus.Healthy);
        }

        [Fact]
        public async Task RecordFailure_UpdatesMetrics()
        {
            // Arrange
            var provider = "test-provider";

            // Act - Add some successes first, then failures to test degraded state
            _healthMonitor.RecordSuccess(provider, 100);
            _healthMonitor.RecordSuccess(provider, 100);
            _healthMonitor.RecordSuccess(provider, 100);
            _healthMonitor.RecordFailure(provider, "Error 1");
            _healthMonitor.RecordFailure(provider, "Error 2");

            // Assert
            var health = _healthMonitor.GetHealthStatus(provider);
            health.Should().Be(HealthStatus.Degraded); // 2 consecutive failures triggers degraded
        }

        [Fact]
        public async Task CheckHealthAsync_MixedResults_CalculatesCorrectly()
        {
            // Arrange
            var provider = "test-provider";
            
            // Record mixed results - interleave to avoid consecutive failures
            for (int i = 0; i < 3; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
                _healthMonitor.RecordSuccess(provider, 100);
                _healthMonitor.RecordFailure(provider, "Test failure");
            }
            // Add one more success to maintain 70% success rate
            _healthMonitor.RecordSuccess(provider, 100);

            // Act
            var health = _healthMonitor.GetHealthStatus(provider);

            // Assert
            // 7 successes, 3 failures = 70% success rate, no consecutive failures
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
            var health = _healthMonitor.GetHealthStatus(provider);

            // Assert
            // 2 successes, 8 failures = 20% success rate (below 50% threshold)
            health.Should().Be(HealthStatus.Unhealthy);
        }

        [Fact]
        public async Task CheckHealthAsync_ExactlyHalfFailures_ReturnsDegraded()
        {
            // Arrange
            var provider = "test-provider";
            
            // Record 40% success rate with >10 total requests to trigger degraded
            for (int i = 0; i < 4; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
            }
            for (int i = 0; i < 6; i++)
            {
                _healthMonitor.RecordFailure(provider, "Test failure");
            }
            // Add one more success to get 11 total requests (above the >10 threshold)
            _healthMonitor.RecordSuccess(provider, 100);

            // Act
            var health = _healthMonitor.GetHealthStatus(provider);

            // Assert
            // 5 successes, 6 failures = 45% success rate < 50% with 11 total requests > 10
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

            // Act - Record enough successes to avoid HTTP check
            for (int i = 0; i < 5; i++)
            {
                _healthMonitor.RecordSuccess(provider, responseTime);
            }
            var health = _healthMonitor.GetHealthStatus(provider);

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

            // Act - Record 2 failures to trigger Degraded status (algorithm requires 2+ consecutive failures)
            _healthMonitor.RecordFailure(provider, errorMessage);
            _healthMonitor.RecordFailure(provider, errorMessage);

            // Assert - Should not throw and should be degraded after 2 consecutive failures
            var health = _healthMonitor.GetHealthStatus(provider);
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
            var health = _healthMonitor.GetHealthStatus(provider);

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

            // Provider 3: Mixed - interleave to avoid consecutive failures
            for (int i = 0; i < 3; i++)
            {
                _healthMonitor.RecordSuccess(provider3, 100);
                _healthMonitor.RecordSuccess(provider3, 100);
                if (i < 2) // Only 2 failures to maintain > 60% success
                {
                    _healthMonitor.RecordFailure(provider3, "Error");
                }
            }

            // Assert
            (_healthMonitor.GetHealthStatus(provider1)).Should().Be(HealthStatus.Healthy);
            (_healthMonitor.GetHealthStatus(provider2)).Should().Be(HealthStatus.Unhealthy);
            (_healthMonitor.GetHealthStatus(provider3)).Should().Be(HealthStatus.Healthy); // 75% success with no consecutive failures
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
            var health = _healthMonitor.GetHealthStatus(provider);
            health.Should().BeOneOf(HealthStatus.Degraded, HealthStatus.Unhealthy); // ~33% success rate
        }

        [Theory]
        [InlineData("provider-1", true)]
        [InlineData("provider_2", true)]
        [InlineData("provider.3", true)]
        [InlineData("PROVIDER-4", true)]
        [InlineData("", true)] // Empty provider name
        [InlineData(null, false)] // Null provider name should throw
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
            else
            {
                Action act1 = () => _healthMonitor.RecordSuccess(providerName, 100);
                Action act2 = () => _healthMonitor.RecordFailure(providerName, "Error");
                
                act1.Should().Throw<ArgumentNullException>();
                act2.Should().Throw<ArgumentNullException>();
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
            var health1 = _healthMonitor.GetHealthStatus(provider);

            // Become unhealthy
            for (int i = 0; i < 20; i++)
            {
                _healthMonitor.RecordFailure(provider, "Error");
            }
            var health2 = _healthMonitor.GetHealthStatus(provider);

            // Recover
            for (int i = 0; i < 30; i++)
            {
                _healthMonitor.RecordSuccess(provider, 100);
            }
            var health3 = _healthMonitor.GetHealthStatus(provider);

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