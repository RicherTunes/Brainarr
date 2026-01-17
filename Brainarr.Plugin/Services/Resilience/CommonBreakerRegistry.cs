using System;
using System.Collections.Concurrent;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using CommonOptions = Lidarr.Plugin.Common.Services.Resilience.AdvancedCircuitBreakerOptions;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Registry that provides per-provider+model circuit breakers using Common's AdvancedCircuitBreaker.
    /// This is the WS4.2 replacement for BreakerRegistry, delegating to Common while preserving Brainarr semantics.
    /// </summary>
    public sealed class CommonBreakerRegistry : IBreakerRegistry
    {
        private readonly ConcurrentDictionary<ModelKey, ICircuitBreaker> _breakers = new();

        public ICircuitBreaker Get(ModelKey key, Logger logger, CircuitBreakerOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(key.Provider))
                throw new ArgumentException("Provider is required", nameof(key));

            logger ??= LogManager.GetCurrentClassLogger();

            return _breakers.GetOrAdd(key, k =>
            {
                var resource = $"ai:{k.Provider}:{k.ModelId}";
                var commonOptions = MapOptions(options);
                return new BrainarrCircuitBreakerAdapter(resource, commonOptions, logger);
            });
        }

        /// <summary>
        /// Maps Brainarr's CircuitBreakerOptions to Common's AdvancedCircuitBreakerOptions.
        /// Preserves all the configuration knobs while using Common's defaults where not specified.
        /// </summary>
        private static CommonOptions MapOptions(CircuitBreakerOptions? brainarrOptions)
        {
            if (brainarrOptions == null)
            {
                // Use Brainarr's default values from BrainarrConstants
                return new CommonOptions
                {
                    ConsecutiveFailureThreshold = 5,
                    FailureRateThreshold = BrainarrConstants.CircuitBreakerFailureThreshold,
                    MinimumThroughput = BrainarrConstants.CircuitBreakerMinimumThroughput,
                    SamplingWindowSize = BrainarrConstants.CircuitBreakerSamplingWindow,
                    BreakDuration = TimeSpan.FromSeconds(BrainarrConstants.CircuitBreakerDurationSeconds),
                    HalfOpenSuccessThreshold = 3
                };
            }

            return new CommonOptions
            {
                ConsecutiveFailureThreshold = brainarrOptions.FailureThreshold,
                FailureRateThreshold = brainarrOptions.FailureRateThreshold,
                MinimumThroughput = brainarrOptions.MinimumThroughput,
                SamplingWindowSize = brainarrOptions.SamplingWindowSize,
                BreakDuration = brainarrOptions.BreakDuration,
                HalfOpenSuccessThreshold = brainarrOptions.HalfOpenSuccessThreshold
            };
        }
    }
}
