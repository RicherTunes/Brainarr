using System;
using System.Collections.Concurrent;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    public interface IBreakerRegistry
    {
        ICircuitBreaker Get(ModelKey key, Logger logger, CircuitBreakerOptions? options = null);
    }

    /// <summary>
    /// Provides per-provider+model circuit breakers with consistent options.
    /// </summary>
    public sealed class BreakerRegistry : IBreakerRegistry
    {
        private readonly ConcurrentDictionary<ModelKey, ICircuitBreaker> _breakers = new();

        public ICircuitBreaker Get(ModelKey key, Logger logger, CircuitBreakerOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(key.Provider)) throw new ArgumentException("Provider is required", nameof(key));
            if (logger == null) logger = LogManager.GetCurrentClassLogger();

            var opts = options ?? CircuitBreakerOptions.Default;
            return _breakers.GetOrAdd(key, k =>
            {
                var resource = $"ai:{k.Provider}:{k.ModelId}";
                return new CircuitBreaker(resource, opts, logger);
            });
        }
    }
}
