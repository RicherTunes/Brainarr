using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Statistics about circuit breaker state and operation history.
    /// </summary>
    public class CircuitBreakerStatistics
    {
        public string ResourceName { get; set; }
        public CircuitState State { get; set; }
        public int ConsecutiveFailures { get; set; }
        public double FailureRate { get; set; }
        public int TotalOperations { get; set; }
        public DateTime LastStateChange { get; set; }
        public DateTime? NextHalfOpenAttempt { get; set; }

        /// <summary>
        /// Recent operation history. May be null if not supported by the implementation.
        /// </summary>
        /// <remarks>
        /// After WS4.2 migration to Common's AdvancedCircuitBreaker, this field is not populated
        /// since Common doesn't expose per-operation history. This is documented behavior.
        /// </remarks>
        public object RecentOperations { get; set; }
    }
}
