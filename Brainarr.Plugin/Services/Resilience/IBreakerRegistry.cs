using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Registry for obtaining per-provider+model circuit breakers.
    /// </summary>
    public interface IBreakerRegistry
    {
        ICircuitBreaker Get(ModelKey key, Logger logger, CircuitBreakerOptions? options = null);
    }
}
