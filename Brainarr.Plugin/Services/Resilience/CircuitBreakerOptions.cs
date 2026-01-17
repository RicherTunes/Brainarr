using System;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
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
}
