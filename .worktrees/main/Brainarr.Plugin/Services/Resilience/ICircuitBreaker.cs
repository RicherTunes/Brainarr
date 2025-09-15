using System;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Defines the contract for circuit breaker implementations.
    /// </summary>
    public interface ICircuitBreaker
    {
        string ResourceName { get; }
        CircuitState State { get; }
        DateTime LastStateChange { get; }
        int ConsecutiveFailures { get; }
        double FailureRate { get; }

        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);
        Task<T> ExecuteWithFallbackAsync<T>(Func<Task<T>> operation, T fallbackValue, CancellationToken cancellationToken = default);

        CircuitBreakerStatistics GetStatistics();
        void Reset();

        event EventHandler<CircuitBreakerEventArgs> CircuitOpened;
        event EventHandler<CircuitBreakerEventArgs> CircuitClosed;
    }

    public class CircuitBreakerStatistics
    {
        public string ResourceName { get; set; }
        public CircuitState State { get; set; }
        public int ConsecutiveFailures { get; set; }
        public double FailureRate { get; set; }
        public int TotalOperations { get; set; }
        public DateTime LastStateChange { get; set; }
        public DateTime? NextHalfOpenAttempt { get; set; }
        public object RecentOperations { get; set; }
    }

    public class CircuitBreakerEventArgs : EventArgs
    {
        public string ResourceName { get; set; }
        public CircuitState State { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }
}
