using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Represents the health status of an AI provider based on recent performance metrics.
    /// </summary>
    public enum HealthStatus
    {
        /// <summary>
        /// Provider is operating normally with good performance metrics.
        /// </summary>
        Healthy,
        
        /// <summary>
        /// Provider is experiencing some issues but still functional.
        /// </summary>
        Degraded,
        
        /// <summary>
        /// Provider is not functioning properly and should be avoided.
        /// </summary>
        Unhealthy,
        
        /// <summary>
        /// Provider status cannot be determined (insufficient data).
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Contains performance and reliability metrics for an AI provider.
    /// Used to determine provider health status and make failover decisions.
    /// Converted to record type for immutability and value semantics.
    /// </summary>
    public record ProviderMetrics
    {
        public int TotalRequests { get; init; }
        public int SuccessfulRequests { get; init; }
        public int FailedRequests { get; init; }
        public double AverageResponseTimeMs { get; init; }
        public DateTime LastSuccessfulRequest { get; init; }
        public DateTime LastFailedRequest { get; init; }
        public int ConsecutiveFailures { get; init; }
        public string? LastError { get; init; }

        public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests * 100 : 0;

        /// <summary>
        /// Calculates the health status based on current metrics using defined thresholds.
        /// </summary>
        /// <returns>Health status based on failure patterns and success rates</returns>
        /// <remarks>
        /// Health determination algorithm:
        /// - Unhealthy: 5+ consecutive failures
        /// - Degraded: 2+ consecutive failures OR success rate < 50% (min 10 requests)
        /// - Unknown: No request history
        /// - Healthy: All other cases
        /// 
        /// This algorithm prioritizes recent failures over historical success rates
        /// to ensure quick detection of provider issues.
        /// </remarks>
        public HealthStatus GetHealthStatus()
        {
            if (ConsecutiveFailures >= 5) return HealthStatus.Unhealthy;
            if (ConsecutiveFailures >= 2) return HealthStatus.Degraded;
            if (SuccessRate < 50 && TotalRequests > 10) return HealthStatus.Degraded;
            if (TotalRequests == 0) return HealthStatus.Unknown;
            return HealthStatus.Healthy;
        }
    }

    /// <summary>
    /// Interface for monitoring AI provider health and performance metrics.
    /// Enables failover logic by tracking success/failure patterns.
    /// </summary>
    public interface IProviderHealthMonitor
    {
        Task<HealthStatus> CheckHealthAsync(string providerName, string baseUrl);
        void RecordSuccess(string providerName, double responseTimeMs);
        void RecordFailure(string providerName, string error);
        ProviderMetrics GetMetrics(string providerName);
        HealthStatus GetHealthStatus(string providerName);
        bool IsHealthy(string providerName);
    }

    /// <summary>
    /// Monitors AI provider health through performance metrics and periodic health checks.
    /// Implements intelligent caching and circuit breaker patterns for reliability.
    /// </summary>
    /// <remarks>
    /// Key features:
    /// - Metrics-based health assessment (avoids unnecessary network calls)
    /// - Configurable health check intervals (default: 5 minutes)
    /// - Provider-specific health endpoints
    /// - Thread-safe concurrent metrics collection
    /// - Circuit breaker pattern for consecutive failures
    /// 
    /// Health Check Strategy:
    /// 1. Use cached metrics if sufficient data available (5+ requests)
    /// 2. Respect health check intervals to avoid spam
    /// 3. Perform actual connectivity tests when needed
    /// 4. Provider-specific endpoints for accurate health assessment
    /// 
    /// This approach balances accuracy with performance by minimizing
    /// network overhead while maintaining reliable health status.
    /// </remarks>
    public class ProviderHealthMonitor : IProviderHealthMonitor
    {
        private readonly ConcurrentDictionary<string, ProviderMetrics> _metrics;
        private readonly ConcurrentDictionary<string, DateTime> _lastHealthCheck;
        private readonly Logger _logger;
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(5);

        public ProviderHealthMonitor(Logger logger)
        {
            _logger = logger;
            _metrics = new ConcurrentDictionary<string, ProviderMetrics>();
            _lastHealthCheck = new ConcurrentDictionary<string, DateTime>();
        }

        /// <summary>
        /// Performs a comprehensive health check for the specified provider.
        /// </summary>
        /// <param name="providerName">Name of the provider to check</param>
        /// <param name="baseUrl">Base URL of the provider endpoint</param>
        /// <returns>Current health status of the provider</returns>
        /// <remarks>
        /// Health check logic:
        /// 1. If sufficient metrics exist (5+ requests), use metrics-based assessment
        /// 2. If recent health check exists (within 5 minutes), return cached result
        /// 3. Otherwise, perform actual connectivity test
        /// 
        /// This tiered approach minimizes network overhead while ensuring accuracy.
        /// The method is async to support non-blocking health monitoring.
        /// </remarks>
        public async Task<HealthStatus> CheckHealthAsync(string providerName, string baseUrl)
        {
            try
            {
                var metrics = GetMetrics(providerName);
                
                // If we have sufficient metrics data, use that instead of making HTTP calls
                if (metrics.TotalRequests >= 5)
                {
                    _logger.Debug($"Using metrics-based health status for {providerName}: {metrics.SuccessRate:F1}% success rate");
                    return metrics.GetHealthStatus();
                }

                // Check if we've recently done a health check
                if (_lastHealthCheck.TryGetValue(providerName, out var lastCheck))
                {
                    if (DateTime.UtcNow - lastCheck < _healthCheckInterval)
                    {
                        var cachedMetrics = GetMetrics(providerName);
                        return cachedMetrics.GetHealthStatus();
                    }
                }

                // Perform actual health check based on provider type only when necessary
                bool isHealthy = await PerformHealthCheckAsync(providerName, baseUrl);
                
                _lastHealthCheck[providerName] = DateTime.UtcNow;
                
                if (isHealthy)
                {
                    _logger.Debug($"Health check passed for {providerName}");
                    return HealthStatus.Healthy;
                }
                else
                {
                    _logger.Warn($"Health check failed for {providerName}");
                    return HealthStatus.Unhealthy;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Health check error for {providerName}");
                return HealthStatus.Unknown;
            }
        }

        // SECURITY FIX: Reuse HttpClient to prevent socket exhaustion
        private static readonly System.Net.Http.HttpClient _healthCheckClient = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        
        private async Task<bool> PerformHealthCheckAsync(string providerName, string baseUrl)
        {
            // Simple connectivity check - can be enhanced per provider type
            try
            {
                string healthEndpoint = providerName.ToLower() switch
                {
                    "ollama" => $"{baseUrl}/api/tags",
                    "lmstudio" => $"{baseUrl}/v1/models",
                    _ => baseUrl
                };
                
                // Retry with full jitter to smooth transient errors
                var attempts = 0;
                var delay = TimeSpan.FromMilliseconds(150);
                var rng = new Random();
                while (attempts < 3)
                {
                    attempts++;
                    try
                    {
                        var response = await _healthCheckClient.GetAsync(healthEndpoint);
                        if (response.IsSuccessStatusCode) return true;
                    }
                    catch
                    {
                        // swallow and retry
                    }
                    if (attempts < 3)
                    {
                        var sleep = (int)(delay.TotalMilliseconds * rng.NextDouble());
                        await Task.Delay(sleep);
                        delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Records a successful provider operation and updates performance metrics.
        /// </summary>
        /// <param name="providerName">Name of the provider</param>
        /// <param name="responseTimeMs">Response time in milliseconds</param>
        /// <remarks>
        /// Updates performed:
        /// - Increments success counters
        /// - Resets consecutive failure count
        /// - Updates average response time using incremental algorithm
        /// - Records timestamp of last successful operation
        /// 
        /// Response time calculation uses incremental averaging to avoid
        /// storing all historical response times in memory.
        /// </remarks>
        public void RecordSuccess(string providerName, double responseTimeMs)
        {
            _metrics.AddOrUpdate(providerName, 
                _ => new ProviderMetrics 
                { 
                    TotalRequests = 1,
                    SuccessfulRequests = 1,
                    ConsecutiveFailures = 0,
                    LastSuccessfulRequest = DateTime.UtcNow,
                    AverageResponseTimeMs = responseTimeMs
                },
                (_, existingMetrics) =>
                {
                    var newSuccessfulRequests = existingMetrics.SuccessfulRequests + 1;
                    var newAverageResponseTime = existingMetrics.AverageResponseTimeMs == 0 
                        ? responseTimeMs
                        : (existingMetrics.AverageResponseTimeMs * existingMetrics.SuccessfulRequests + responseTimeMs) / newSuccessfulRequests;
                        
                    return existingMetrics with
                    {
                        TotalRequests = existingMetrics.TotalRequests + 1,
                        SuccessfulRequests = newSuccessfulRequests,
                        ConsecutiveFailures = 0,
                        LastSuccessfulRequest = DateTime.UtcNow,
                        AverageResponseTimeMs = newAverageResponseTime
                    };
                });

            var updatedMetrics = _metrics[providerName];
            _logger.Debug($"{providerName}: Success recorded (Response: {responseTimeMs}ms, Success rate: {updatedMetrics.SuccessRate:F1}%)");
        }

        /// <summary>
        /// Records a failed provider operation and updates failure metrics.
        /// </summary>
        /// <param name="providerName">Name of the provider</param>
        /// <param name="error">Error message describing the failure</param>
        /// <remarks>
        /// Updates performed:
        /// - Increments failure counters
        /// - Increments consecutive failure count
        /// - Records error message for diagnostics
        /// - Logs warning for 3+ consecutive failures
        /// 
        /// The consecutive failure count is key for circuit breaker logic
        /// and quick detection of provider degradation.
        /// </remarks>
        public void RecordFailure(string providerName, string error)
        {
            _metrics.AddOrUpdate(providerName,
                _ => new ProviderMetrics
                {
                    TotalRequests = 1,
                    FailedRequests = 1,
                    ConsecutiveFailures = 1,
                    LastFailedRequest = DateTime.UtcNow,
                    LastError = error
                },
                (_, existingMetrics) => existingMetrics with
                {
                    TotalRequests = existingMetrics.TotalRequests + 1,
                    FailedRequests = existingMetrics.FailedRequests + 1,
                    ConsecutiveFailures = existingMetrics.ConsecutiveFailures + 1,
                    LastFailedRequest = DateTime.UtcNow,
                    LastError = error
                });

            var updatedMetrics = _metrics[providerName];
            _logger.Warn($"{providerName}: Failure recorded (Consecutive: {updatedMetrics.ConsecutiveFailures}, Error: {error})");

            if (updatedMetrics.ConsecutiveFailures >= 3)
            {
                _logger.Error($"{providerName} has failed {updatedMetrics.ConsecutiveFailures} times consecutively - provider may be unhealthy");
            }
        }

        public ProviderMetrics GetMetrics(string providerName)
        {
            return _metrics.GetOrAdd(providerName, _ => new ProviderMetrics());
        }

        public bool IsHealthy(string providerName)
        {
            var metrics = GetMetrics(providerName);
            return metrics.GetHealthStatus() != HealthStatus.Unhealthy;
        }

        public HealthStatus GetHealthStatus(string providerName)
        {
            var metrics = GetMetrics(providerName);
            return metrics.GetHealthStatus();
        }
    }
}
