using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy,
        Unknown
    }

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

        public HealthStatus GetHealthStatus()
        {
            if (ConsecutiveFailures >= 5) return HealthStatus.Unhealthy;
            if (ConsecutiveFailures >= 2) return HealthStatus.Degraded;
            if (SuccessRate < 50 && TotalRequests > 10) return HealthStatus.Degraded;
            if (TotalRequests == 0) return HealthStatus.Unknown;
            return HealthStatus.Healthy;
        }
    }

    public interface IProviderHealthMonitor
    {
        Task<HealthStatus> CheckHealthAsync(string providerName, string baseUrl);
        void RecordSuccess(string providerName, double responseTimeMs);
        void RecordFailure(string providerName, string error);
        ProviderMetrics GetMetrics(string providerName);
        bool IsHealthy(string providerName);
    }

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

        public async Task<HealthStatus> CheckHealthAsync(string providerName, string baseUrl)
        {
            try
            {
                // Check if we've recently done a health check
                if (_lastHealthCheck.TryGetValue(providerName, out var lastCheck))
                {
                    if (DateTime.UtcNow - lastCheck < _healthCheckInterval)
                    {
                        var cachedMetrics = GetMetrics(providerName);
                        return cachedMetrics.GetHealthStatus();
                    }
                }

                // Perform actual health check based on provider type
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
    }
}