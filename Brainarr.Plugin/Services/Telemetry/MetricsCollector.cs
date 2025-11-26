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
            // Cleanup old metrics every hour. Guard callback to avoid unhandled exceptions.
            CleanupTimer = new Timer(state =>
            {
                try { CleanupOldMetrics(state); }
                catch (Exception ex)
                {
                    try { Logger.Warn(ex, "Metrics cleanup failed"); }
                    catch
                    {
                        /* Intentionally swallowing logger errors during cleanup - prevents timer termination */
                    }
                }
            },
            null,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1));

            // Ensure graceful shutdown for test hosts and plugin unloads
            try
            {
                AppDomain.CurrentDomain.ProcessExit += (_, __) => Shutdown();
                AppDomain.CurrentDomain.DomainUnload += (_, __) => Shutdown();
            }
            catch
            {
                /* Intentionally ignoring AppDomain event registration failures in restricted hosts */
            }
        }

        public static void Shutdown()
        {
            try { CleanupTimer?.Dispose(); }
            catch
            {
                /* Intentionally swallowing timer disposal errors during shutdown */
            }
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
            // Store timings under the base name; exporter will append units suffix.
            RecordMetric(name, duration.TotalMilliseconds, tags);
        }

        /// <summary>
        /// Increments a counter metric.
        /// </summary>
        public static void IncrementCounter(string name, Dictionary<string, string>? tags = null)
        {
            var aggregator = Metrics.GetOrAdd(name, k => new MetricAggregator(k));
            aggregator.IncrementCounter(tags);
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
                var name = kvp.Key;
                var agg = kvp.Value;
                if (!(pattern == "*" || name.Contains(pattern.Replace("*", "")))) continue;

                // For provider metrics, expand to per-label synthetic keys for compatibility with existing views
                if (name == Services.Telemetry.ProviderMetricsHelper.ProviderLatencyMs ||
                    name == Services.Telemetry.ProviderMetricsHelper.ProviderErrorsTotal ||
                    name == Services.Telemetry.ProviderMetricsHelper.ProviderThrottlesTotal)
                {
                    var perLabel = agg.GetSummariesByLabelSets(windowToUse);
                    foreach (var entry in perLabel)
                    {
                        var labelKey = entry.Key; // e.g., provider=openai,model=gpt-4o-mini
                        var (prov, model) = ParseProviderModel(labelKey);
                        var syntheticKey = $"{name}.{prov}.{model}";
                        result[syntheticKey] = entry.Value;
                    }
                }
                else
                {
                    result[name] = agg.GetSummary(windowToUse);
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
                var name = kvp.Key;
                var agg = kvp.Value;
                var promBase = ToPrometheusBaseName(name);
                var type = name == Services.Telemetry.ProviderMetricsHelper.ProviderErrorsTotal || name == Services.Telemetry.ProviderMetricsHelper.ProviderThrottlesTotal ? "counter" : "gauge";

                // HELP/TYPE lines once per metric
                lines.Add($"# HELP {promBase} {name}");
                lines.Add($"# TYPE {promBase} {type}");

                // For latency, export percentile summaries in base units (seconds).
                if (name == Services.Telemetry.ProviderMetricsHelper.ProviderLatencyMs)
                {
                    var perLabel = agg.GetSummariesByLabelSets(TimeSpan.FromMinutes(1));
                    if (perLabel.Count == 0)
                    {
                        // No data yet
                        lines.Add($"{promBase}_count 0");
                        lines.Add($"{promBase}_sum 0");
                        lines.Add($"{promBase}_avg 0");
                        lines.Add($"{promBase}_min 0");
                        lines.Add($"{promBase}_max 0");
                        continue;
                    }

                    foreach (var entry in perLabel)
                    {
                        var labels = entry.Value.Labels ?? new Dictionary<string, string>();
                        var labelText = labels.Count > 0
                            ? "{" + string.Join(",", labels.OrderBy(k => k.Key).Select(kv => $"{SanitizeLabel(kv.Key)}=\"{SanitizeLabelValue(kv.Value)}\"")) + "}"
                            : string.Empty;

                        lines.Add($"{promBase}_count{labelText} {entry.Value.Count}");
                        lines.Add($"{promBase}_sum{labelText} {entry.Value.Sum / 1000.0}");
                        lines.Add($"{promBase}_avg{labelText} {entry.Value.Average / 1000.0}");
                        lines.Add($"{promBase}_min{labelText} {entry.Value.Min / 1000.0}");
                        lines.Add($"{promBase}_max{labelText} {entry.Value.Max / 1000.0}");
                        if (entry.Value.Percentiles != null)
                        {
                            lines.Add($"{promBase}_p50{labelText} {entry.Value.Percentiles.P50 / 1000.0}");
                            lines.Add($"{promBase}_p95{labelText} {entry.Value.Percentiles.P95 / 1000.0}");
                            lines.Add($"{promBase}_p99{labelText} {entry.Value.Percentiles.P99 / 1000.0}");
                        }
                    }
                    continue;
                }
                // Counters (monotonic totals)
                var totals = agg.GetCounterTotalsByLabelSet();
                if (totals.Count == 0)
                {
                    lines.Add($"{promBase} 0");
                    continue;
                }
                foreach (var entry in totals)
                {
                    var labels = LabelsFromKey(entry.Key);
                    var labelText = labels.Count > 0
                        ? "{" + string.Join(",", labels.OrderBy(k => k.Key).Select(kv => $"{SanitizeLabel(kv.Key)}=\"{SanitizeLabelValue(kv.Value)}\"")) + "}"
                        : string.Empty;
                    lines.Add($"{promBase}{labelText} {entry.Value}");
                }
            }

            return string.Join("\n", lines);
        }

        private static string ToPrometheusBaseName(string name)
        {
            return name switch
            {
                var n when n == Services.Telemetry.ProviderMetricsHelper.ProviderLatencyMs => "provider_latency_seconds",
                var n when n == Services.Telemetry.ProviderMetricsHelper.ProviderErrorsTotal => "provider_errors_total",
                var n when n == Services.Telemetry.ProviderMetricsHelper.ProviderThrottlesTotal => "provider_throttles_total",
                _ => name.Replace(".", "_")
            };
        }

        private static (string provider, string model) ParseProviderModel(string labelKey)
        {
            string provider = "unknown", model = "default";
            if (string.IsNullOrWhiteSpace(labelKey)) return (provider, model);
            foreach (var part in labelKey.Split(','))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0) continue;
                var k = part.Substring(0, idx).Trim();
                var v = part.Substring(idx + 1).Trim();
                if (k.Equals("provider", StringComparison.OrdinalIgnoreCase)) provider = v;
                else if (k.Equals("model", StringComparison.OrdinalIgnoreCase)) model = v;
            }
            return (provider, model);
        }

        private static string SanitizeLabel(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? "key" : key.Replace("-", "_");
        }

        private static string SanitizeLabelValue(string val)
        {
            if (string.IsNullOrEmpty(val)) return "unknown";
            return val
                .Replace("\\", "\\\\")
                .Replace("\n", "\\n")
                .Replace("\"", "\\\"");
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
            private readonly ConcurrentDictionary<string, long> _labelTotals = new();

            public MetricAggregator(string name)
            {
                _name = name;
            }

            public void Record(MetricPoint point)
            {
                _points.Add(point);
            }

            public void IncrementCounter(Dictionary<string, string>? tags, long delta = 1)
            {
                Interlocked.Add(ref _counter, delta);
                var key = KeyFromTags(tags ?? new Dictionary<string, string>());
                _labelTotals.AddOrUpdate(key, delta, (_, v) => v + delta);
                // Optional: record a point enabling short-window views
                _points.Add(new MetricPoint { Timestamp = DateTime.UtcNow, Value = delta, Tags = tags ?? new Dictionary<string, string>() });
            }

            public void RemoveOldPoints(DateTime cutoff)
            {
                // Drain and keep only valid points; ConcurrentBag has no Clear, so rebuild in place.
                var keep = new List<MetricPoint>();
                while (_points.TryTake(out var p))
                {
                    if (p.Timestamp >= cutoff)
                    {
                        keep.Add(p);
                    }
                }
                foreach (var p in keep)
                {
                    _points.Add(p);
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
                        CounterValue = _counter,
                        Labels = new Dictionary<string, string>()
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
                    Labels = new Dictionary<string, string>(),
                    Percentiles = new PercentileValues
                    {
                        P50 = GetPercentile(values, 0.5),
                        P95 = GetPercentile(values, 0.95),
                        P99 = GetPercentile(values, 0.99)
                    }
                };
            }

            public Dictionary<string, MetricsSummary> GetSummariesByLabelSets(TimeSpan window)
            {
                var cutoff = DateTime.UtcNow - window;
                // Opportunistically trim points older than the requested window to bound memory.
                TrimOlderThan(cutoff);
                var recentPoints = _points.Where(p => p.Timestamp >= cutoff).ToList();

                var dict = new Dictionary<string, MetricsSummary>();
                if (!recentPoints.Any()) return dict;

                static string KeyFromTags(Dictionary<string, string> tags)
                {
                    if (tags == null || tags.Count == 0) return string.Empty;
                    return string.Join(",", tags.OrderBy(k => k.Key).Select(kv => $"{kv.Key}={kv.Value}"));
                }

                var groups = recentPoints.GroupBy(p => KeyFromTags(p.Tags ?? new Dictionary<string, string>()));
                foreach (var g in groups)
                {
                    var values = g.Select(p => p.Value).OrderBy(v => v).ToList();
                    var labels = g.FirstOrDefault()?.Tags ?? new Dictionary<string, string>();
                    dict[g.Key] = new MetricsSummary
                    {
                        Name = _name,
                        Labels = new Dictionary<string, string>(labels, StringComparer.Ordinal),
                        Count = values.Count,
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
                return dict;
            }

            private void TrimOlderThan(DateTime cutoff)
            {
                // Drain and keep only points within the window.
                var keep = new List<MetricPoint>();
                while (_points.TryTake(out var p))
                {
                    if (p.Timestamp >= cutoff)
                    {
                        keep.Add(p);
                    }
                }
                foreach (var p in keep)
                {
                    _points.Add(p);
                }
            }

            private double GetPercentile(List<double> sortedValues, double percentile)
            {
                if (!sortedValues.Any())
                    return 0;

                var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
                return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
            }

            public Dictionary<string, long> GetCounterTotalsByLabelSet()
            {
                return new Dictionary<string, long>(_labelTotals);
            }

            private static string KeyFromTags(Dictionary<string, string> tags)
            {
                if (tags == null || tags.Count == 0) return string.Empty;
                const char PairSep = '\u001F';
                const char KVSep = '\u001E';
                return string.Join(PairSep, tags.OrderBy(k => k.Key).Select(kv => $"{kv.Key}{KVSep}{kv.Value}"));
            }
        }

        private class MetricPoint
        {
            public DateTime Timestamp { get; set; }
            public double Value { get; set; }
            public Dictionary<string, string> Tags { get; set; }
            public TimeSpan? Duration { get; set; }
        }

        private static Dictionary<string, string> LabelsFromKey(string key)
        {
            var labels = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(key)) return labels;
            const char PairSep = '\u001F';
            const char KVSep = '\u001E';
            foreach (var part in key.Split(PairSep))
            {
                var idx = part.IndexOf(KVSep);
                if (idx <= 0) continue;
                var k = part.Substring(0, idx);
                var v = part.Substring(idx + 1);
                labels[k] = v;
            }
            return labels;
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
        public Dictionary<string, string> Labels { get; set; }
        public PercentileValues Percentiles { get; set; }
    }

    public class PercentileValues
    {
        public double P50 { get; set; }
        public double P95 { get; set; }
        public double P99 { get; set; }
    }
}
