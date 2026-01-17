using System;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Circuit breaker states following the standard pattern.
    /// </summary>
    public enum CircuitState
    {
        Closed,    // Normal operation
        Open,      // Blocking requests
        HalfOpen   // Testing recovery
    }

    /// <summary>
    /// Configuration options for circuit breaker behavior.
    /// </summary>
    public class CircuitBreakerOptions
    {
        public int FailureThreshold { get; set; } = 5;
        public double FailureRateThreshold { get; set; } = BrainarrConstants.CircuitBreakerFailureThreshold;
        public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(BrainarrConstants.CircuitBreakerDurationSeconds);
        public int HalfOpenSuccessThreshold { get; set; } = 3;
        public int SamplingWindowSize { get; set; } = BrainarrConstants.CircuitBreakerSamplingWindow;
        public int MinimumThroughput { get; set; } = BrainarrConstants.CircuitBreakerMinimumThroughput;

        public static CircuitBreakerOptions Default => new CircuitBreakerOptions();

        public static CircuitBreakerOptions Aggressive => new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            FailureRateThreshold = 0.3,
            BreakDuration = TimeSpan.FromMinutes(5)
        };

        public static CircuitBreakerOptions Lenient => new CircuitBreakerOptions
        {
            FailureThreshold = 10,
            FailureRateThreshold = 0.75,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
    }

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

    /// <summary>
    /// Registry for obtaining per-provider+model circuit breakers.
    /// </summary>
    public interface IBreakerRegistry
    {
        ICircuitBreaker Get(Core.ModelKey key, NLog.Logger logger, CircuitBreakerOptions? options = null);
    }
}
