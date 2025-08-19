using System;
using System.Collections.Generic;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IStructuredLogger
    {
        void LogRecommendationFetch(string provider, int count, double responseTimeMs, bool cached = false);
        void LogProviderError(string provider, string error, Exception exception = null);
        void LogModelDetection(string provider, string model, bool success);
        void LogCacheOperation(string operation, string key, bool hit);
        void LogHealthCheck(string provider, HealthStatus status, ProviderMetrics metrics);
    }

    public class StructuredLogger : IStructuredLogger
    {
        private readonly Logger _logger;
        private readonly string _sessionId;

        public StructuredLogger(Logger logger)
        {
            _logger = logger;
            _sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public void LogRecommendationFetch(string provider, int count, double responseTimeMs, bool cached = false)
        {
            var logData = new Dictionary<string, object>
            {
                ["event"] = "recommendation_fetch",
                ["session"] = _sessionId,
                ["provider"] = provider,
                ["count"] = count,
                ["response_time_ms"] = responseTimeMs,
                ["cached"] = cached,
                ["timestamp"] = DateTime.UtcNow
            };

            _logger.Info($"[FETCH] Provider={provider} Count={count} Time={responseTimeMs:F1}ms Cached={cached} Session={_sessionId}");
            _logger.Debug(() => SerializeLogData(logData));
        }

        public void LogProviderError(string provider, string error, Exception exception = null)
        {
            var logData = new Dictionary<string, object>
            {
                ["event"] = "provider_error",
                ["session"] = _sessionId,
                ["provider"] = provider,
                ["error"] = error,
                ["exception_type"] = exception?.GetType().Name,
                ["timestamp"] = DateTime.UtcNow
            };

            _logger.Error($"[ERROR] Provider={provider} Error=\"{error}\" Session={_sessionId}");
            if (exception != null)
            {
                _logger.Error(exception, $"Exception details for provider {provider}");
            }
            _logger.Debug(() => SerializeLogData(logData));
        }

        public void LogModelDetection(string provider, string model, bool success)
        {
            var logData = new Dictionary<string, object>
            {
                ["event"] = "model_detection",
                ["session"] = _sessionId,
                ["provider"] = provider,
                ["model"] = model,
                ["success"] = success,
                ["timestamp"] = DateTime.UtcNow
            };

            _logger.Info($"[MODEL] Provider={provider} Model=\"{model}\" Success={success} Session={_sessionId}");
            _logger.Debug(() => SerializeLogData(logData));
        }

        public void LogCacheOperation(string operation, string key, bool hit)
        {
            // Sanitize cache key to prevent information disclosure
            var sanitizedKey = key?.Length > 0 ? 
                $"{key.GetHashCode():X8}" : "empty";
            
            var logData = new Dictionary<string, object>
            {
                ["event"] = "cache_operation",
                ["session"] = _sessionId,
                ["operation"] = operation,
                ["key_hash"] = sanitizedKey,
                ["hit"] = hit,
                ["timestamp"] = DateTime.UtcNow
            };

            _logger.Debug($"[CACHE] Operation={operation} KeyHash={sanitizedKey} Hit={hit} Session={_sessionId}");
            _logger.Trace(() => SerializeLogData(logData));
        }

        public void LogHealthCheck(string provider, HealthStatus status, ProviderMetrics metrics)
        {
            var logData = new Dictionary<string, object>
            {
                ["event"] = "health_check",
                ["session"] = _sessionId,
                ["provider"] = provider,
                ["status"] = status.ToString(),
                ["success_rate"] = metrics.SuccessRate,
                ["consecutive_failures"] = metrics.ConsecutiveFailures,
                ["avg_response_time"] = metrics.AverageResponseTimeMs,
                ["timestamp"] = DateTime.UtcNow
            };

            var level = status == HealthStatus.Healthy ? LogLevel.Debug : 
                       status == HealthStatus.Degraded ? LogLevel.Warn : LogLevel.Error;
                       
            _logger.Log(level, $"[HEALTH] Provider={provider} Status={status} SuccessRate={metrics.SuccessRate:F1}% AvgTime={metrics.AverageResponseTimeMs:F1}ms Session={_sessionId}");
            _logger.Debug(() => SerializeLogData(logData));
        }

        private string SerializeLogData(Dictionary<string, object> data)
        {
            // Simple JSON-like serialization for structured logging
            var items = new List<string>();
            foreach (var kvp in data)
            {
                var value = kvp.Value switch
                {
                    string s => $"\"{s}\"",
                    bool b => b.ToString().ToLower(),
                    DateTime dt => $"\"{dt:yyyy-MM-dd HH:mm:ss}\"",
                    _ => kvp.Value?.ToString() ?? "null"
                };
                items.Add($"\"{kvp.Key}\":{value}");
            }
            return "{" + string.Join(",", items) + "}";
        }
    }
}