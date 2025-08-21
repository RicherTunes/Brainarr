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
    /// </summary>
    public class ProviderMetrics
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public DateTime LastSuccessfulRequest { get; set; }
        public DateTime LastFailedRequest { get; set; }
        public int ConsecutiveFailures { get; set; }
        public string LastError { get; set; }

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

        private async Task<bool> PerformHealthCheckAsync(string providerName, string baseUrl)
        {
            // Simple connectivity check - can be enhanced per provider type
            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                
                try
                {
                    string healthEndpoint = providerName.ToLower() switch
                    {
                        "ollama" => $"{baseUrl}/api/tags",
                        "lmstudio" => $"{baseUrl}/v1/models",
                        _ => baseUrl
                    };

                    var response = await client.GetAsync(healthEndpoint);
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
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
            var metrics = _metrics.GetOrAdd(providerName, _ => new ProviderMetrics());
            
            metrics.TotalRequests++;
            metrics.SuccessfulRequests++;
            metrics.ConsecutiveFailures = 0;
            metrics.LastSuccessfulRequest = DateTime.UtcNow;
            
            // Update average response time
            if (metrics.AverageResponseTimeMs == 0)
            {
                metrics.AverageResponseTimeMs = responseTimeMs;
            }
            else
            {
                metrics.AverageResponseTimeMs = 
                    (metrics.AverageResponseTimeMs * (metrics.SuccessfulRequests - 1) + responseTimeMs) 
                    / metrics.SuccessfulRequests;
            }

            _logger.Debug($"{providerName}: Success recorded (Response: {responseTimeMs}ms, Success rate: {metrics.SuccessRate:F1}%)");
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
            var metrics = _metrics.GetOrAdd(providerName, _ => new ProviderMetrics());
            
            metrics.TotalRequests++;
            metrics.FailedRequests++;
            metrics.ConsecutiveFailures++;
            metrics.LastFailedRequest = DateTime.UtcNow;
            metrics.LastError = error;

            _logger.Warn($"{providerName}: Failure recorded (Consecutive: {metrics.ConsecutiveFailures}, Error: {error})");

            if (metrics.ConsecutiveFailures >= 3)
            {
                _logger.Error($"{providerName} has failed {metrics.ConsecutiveFailures} times consecutively - provider may be unhealthy");
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