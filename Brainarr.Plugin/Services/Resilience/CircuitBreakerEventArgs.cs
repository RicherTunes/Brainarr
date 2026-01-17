using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Event arguments for circuit breaker state change events.
    /// </summary>
    public class CircuitBreakerEventArgs : EventArgs
    {
        public string ResourceName { get; set; }
        public CircuitState State { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
