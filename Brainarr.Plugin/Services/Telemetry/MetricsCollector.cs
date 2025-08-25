using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Collects and aggregates metrics for monitoring and alerting.
    /// Provides insights into system performance and health.
    /// </summary>
    public static class MetricsCollector
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly ConcurrentDictionary<string, MetricAggregator> Metrics = new();
        private static readonly Timer CleanupTimer;
        private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(24);
        
        static MetricsCollector()
        {
            // Cleanup old metrics every hour
            CleanupTimer = new Timer(
                CleanupOldMetrics, 
                null, 
                TimeSpan.FromHours(1), 
                TimeSpan.FromHours(1));
        }
        
        /// <summary>
        /// Records a circuit breaker metric.
        /// </summary>
        public static void Record(CircuitBreakerMetric metric)
        {
            var key = $"circuit_breaker.{metric.ResourceName}";
            var aggregator = Metrics.GetOrAdd(key, k => new MetricAggregator(k));
            
            aggregator.Record(new MetricPoint
            {
                Timestamp = metric.Timestamp,
                Value = metric.Success ? 1 : 0,
                Tags = new Dictionary<string, string>
                {
                    ["state"] = metric.State.ToString(),
                    ["consecutive_failures"] = metric.ConsecutiveFailures.ToString(),
                    ["failure_rate"] = metric.FailureRate.ToString("F2")
                },
                Duration = metric.Duration
            });
            
            // Log significant events
            if (metric.State == CircuitState.Open)
            {
                Logger.Warn($"Circuit breaker opened for {metric.ResourceName}");
            }
        }
        
        /// <summary>
        /// Records a generic metric.
        /// </summary>
        public static void RecordMetric(string name, double value, Dictionary<string, string>? tags = null)
        {
            var aggregator = Metrics.GetOrAdd(name, k => new MetricAggregator(k));
            
            aggregator.Record(new MetricPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = value,
                Tags = tags ?? new Dictionary<string, string>()
            });
        }
        
        /// <summary>
        /// Records a timing metric.
        /// </summary>
        public static void RecordTiming(string name, TimeSpan duration, Dictionary<string, string>? tags = null)
        {
            RecordMetric($"{name}.duration_ms", duration.TotalMilliseconds, tags);
        }
        
        /// <summary>
        /// Increments a counter metric.
        /// </summary>
        public static void IncrementCounter(string name, Dictionary<string, string>? tags = null)
        {
            var key = tags != null ? $"{name}.{string.Join(".", tags.Values)}" : name;
            var aggregator = Metrics.GetOrAdd(key, k => new MetricAggregator(k));
            
            aggregator.IncrementCounter();
        }
        
        /// <summary>
        /// Gets aggregated metrics for a time window.
        /// </summary>
        public static MetricsSummary GetSummary(string metricName, TimeSpan window)
        {
            if (!Metrics.TryGetValue(metricName, out var aggregator))
            {
                return new MetricsSummary { Name = metricName };
            }
            
            return aggregator.GetSummary(window);
        }
        
        /// <summary>
        /// Gets all metrics matching a pattern.
        /// </summary>
        public static Dictionary<string, MetricsSummary> GetAllMetrics(string pattern = "*", TimeSpan? window = null)
        {
            var windowToUse = window ?? TimeSpan.FromMinutes(5);
            var result = new Dictionary<string, MetricsSummary>();
            
            foreach (var kvp in Metrics)
            {
                if (pattern == "*" || kvp.Key.Contains(pattern.Replace("*", "")))
                {
                    result[kvp.Key] = kvp.Value.GetSummary(windowToUse);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Exports metrics in Prometheus format.
        /// </summary>
        public static string ExportPrometheus()
        {
            var lines = new List<string>();
            
            foreach (var kvp in Metrics)
            {
                var summary = kvp.Value.GetSummary(TimeSpan.FromMinutes(1));
                var metricName = kvp.Key.Replace(".", "_");
                
                lines.Add($"# HELP {metricName} {kvp.Key}");
                lines.Add($"# TYPE {metricName} gauge");
                lines.Add($"{metricName}_count {summary.Count}");
                lines.Add($"{metricName}_sum {summary.Sum}");
                lines.Add($"{metricName}_avg {summary.Average}");
                lines.Add($"{metricName}_min {summary.Min}");
                lines.Add($"{metricName}_max {summary.Max}");
                
                if (summary.Percentiles != null)
                {
                    lines.Add($"{metricName}_p50 {summary.Percentiles.P50}");
                    lines.Add($"{metricName}_p95 {summary.Percentiles.P95}");
                    lines.Add($"{metricName}_p99 {summary.Percentiles.P99}");
                }
            }
            
            return string.Join("\n", lines);
        }
        
        private static void CleanupOldMetrics(object state)
        {
            var cutoff = DateTime.UtcNow - RetentionPeriod;
            
            foreach (var aggregator in Metrics.Values)
            {
                aggregator.RemoveOldPoints(cutoff);
            }
        }
        
        private class MetricAggregator
        {
            private readonly string _name;
            private readonly ConcurrentBag<MetricPoint> _points = new();
            private long _counter = 0;
            
            public MetricAggregator(string name)
            {
                _name = name;
            }
            
            public void Record(MetricPoint point)
            {
                _points.Add(point);
            }
            
            public void IncrementCounter()
            {
                Interlocked.Increment(ref _counter);
            }
            
            public void RemoveOldPoints(DateTime cutoff)
            {
                var validPoints = _points.Where(p => p.Timestamp >= cutoff).ToList();
                _points.Clear();
                foreach (var point in validPoints)
                {
                    _points.Add(point);
                }
            }
            
            public MetricsSummary GetSummary(TimeSpan window)
            {
                var cutoff = DateTime.UtcNow - window;
                var recentPoints = _points.Where(p => p.Timestamp >= cutoff).ToList();
                
                if (!recentPoints.Any())
                {
                    return new MetricsSummary
                    {
                        Name = _name,
                        Count = 0,
                        CounterValue = _counter
                    };
                }
                
                var values = recentPoints.Select(p => p.Value).OrderBy(v => v).ToList();
                
                return new MetricsSummary
                {
                    Name = _name,
                    Count = values.Count,
                    CounterValue = _counter,
                    Sum = values.Sum(),
                    Average = values.Average(),
                    Min = values.Min(),
                    Max = values.Max(),
                    Percentiles = new PercentileValues
                    {
                        P50 = GetPercentile(values, 0.5),
                        P95 = GetPercentile(values, 0.95),
                        P99 = GetPercentile(values, 0.99)
                    }
                };
            }
            
            private double GetPercentile(List<double> sortedValues, double percentile)
            {
                if (!sortedValues.Any())
                    return 0;
                
                var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
                return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
            }
        }
        
        private class MetricPoint
        {
            public DateTime Timestamp { get; set; }
            public double Value { get; set; }
            public Dictionary<string, string> Tags { get; set; }
            public TimeSpan? Duration { get; set; }
        }
    }
    
    public class CircuitBreakerMetric
    {
        public string ResourceName { get; set; }
        public CircuitState State { get; set; }
        public bool Success { get; set; }
        public TimeSpan Duration { get; set; }
        public int ConsecutiveFailures { get; set; }
        public double FailureRate { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    public class MetricsSummary
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public long CounterValue { get; set; }
        public double Sum { get; set; }
        public double Average { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public PercentileValues Percentiles { get; set; }
    }
    
    public class PercentileValues
    {
        public double P50 { get; set; }
        public double P95 { get; set; }
        public double P99 { get; set; }
    }
}