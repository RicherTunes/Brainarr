using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace Brainarr.Tests.Helpers
{
    /// <summary>
    /// A pass-through circuit breaker that executes all operations without any circuit-breaking behavior.
    /// Used in unit tests where circuit breaker behavior is not being tested.
    /// </summary>
    public sealed class PassThroughCircuitBreaker : ICircuitBreaker
    {
        public string ResourceName => "test";
        public CircuitState State => CircuitState.Closed;
        public DateTime LastStateChange => DateTime.UtcNow;
        public int ConsecutiveFailures => 0;
        public double FailureRate => 0;

        public event EventHandler<CircuitBreakerEventArgs> CircuitOpened { add { } remove { } }
        public event EventHandler<CircuitBreakerEventArgs> CircuitClosed { add { } remove { } }

        public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
            => operation();

        public async Task<T> ExecuteWithFallbackAsync<T>(Func<Task<T>> operation, T fallbackValue, CancellationToken cancellationToken = default)
        {
            try
            {
                return await operation();
            }
            catch
            {
                return fallbackValue;
            }
        }

        public CircuitBreakerStatistics GetStatistics() => new()
        {
            ResourceName = ResourceName,
            State = State,
            ConsecutiveFailures = 0,
            FailureRate = 0,
            TotalOperations = 0,
            LastStateChange = LastStateChange,
            NextHalfOpenAttempt = null,
            RecentOperations = null
        };

        public void Reset() { }
    }

    /// <summary>
    /// Creates a mock IBreakerRegistry that always returns a PassThroughCircuitBreaker.
    /// Usage: var registryMock = PassThroughBreakerRegistry.CreateMock();
    /// </summary>
    public static class PassThroughBreakerRegistry
    {
        /// <summary>
        /// Creates a configured mock IBreakerRegistry that returns PassThroughCircuitBreaker instances.
        /// </summary>
        public static Mock<IBreakerRegistry> CreateMock()
        {
            var mock = new Mock<IBreakerRegistry>();
            mock.Setup(r => r.Get(It.IsAny<ModelKey>(), It.IsAny<Logger>(), It.IsAny<CircuitBreakerOptions?>()))
                .Returns(new PassThroughCircuitBreaker());
            return mock;
        }
    }
}
