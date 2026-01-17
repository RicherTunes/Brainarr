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
}
