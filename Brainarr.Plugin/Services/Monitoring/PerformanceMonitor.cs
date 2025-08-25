using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Brainarr.Plugin.Services.Monitoring
{
    /// <summary>
    /// Advanced performance monitoring service with metrics collection and analysis.
    /// </summary>
    public class PerformanceMonitor : IPerformanceMonitor
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, PerformanceMetric> _metrics;
        private readonly ConcurrentDictionary<string, CircularBuffer<double>> _histograms;
        private readonly Timer _reportingTimer;
        private readonly object _statsLock = new object();
        
        // Performance thresholds
        private const int SlowRequestThresholdMs = 5000;
        private const int CriticalRequestThresholdMs = 10000;
        private const double HighMemoryThresholdMB = 500;
        private const double HighCpuThresholdPercent = 80;
        
        public PerformanceMonitor(ILogger logger)
        {
            _logger = logger;
            _metrics = new ConcurrentDictionary<string, PerformanceMetric>();
            _histograms = new ConcurrentDictionary<string, CircularBuffer<double>>();
            
            // Start periodic reporting
            _reportingTimer = new Timer(
                ReportMetrics, 
                null, 
                TimeSpan.FromMinutes(5), 
                TimeSpan.FromMinutes(5));
            
            // Initialize system metrics collection
            _ = Task.Run(CollectSystemMetrics);
        }
        
        /// <summary>
        /// Records the duration of an operation.
        /// </summary>
        public void RecordDuration(string operation, TimeSpan duration)
        {
            var metric = _metrics.GetOrAdd(operation, _ => new PerformanceMetric(operation));
            metric.Record(duration.TotalMilliseconds);
            
            // Store in histogram for percentile calculations
            var histogram = _histograms.GetOrAdd(operation, _ => new CircularBuffer<double>(1000));
            histogram.Add(duration.TotalMilliseconds);
            
            // Log slow operations
            if (duration.TotalMilliseconds > CriticalRequestThresholdMs)
            {
                _logger.Error($"CRITICAL: Operation '{operation}' took {duration.TotalMilliseconds:F2}ms");
                OnCriticalPerformanceEvent(operation, duration);
            }
            else if (duration.TotalMilliseconds > SlowRequestThresholdMs)
            {
                _logger.Warn($"SLOW: Operation '{operation}' took {duration.TotalMilliseconds:F2}ms");
            }
        }
        
        /// <summary>
        /// Records a counter increment.
        /// </summary>
        public void IncrementCounter(string counter, long value = 1)
        {
            var metric = _metrics.GetOrAdd(counter, _ => new PerformanceMetric(counter, MetricType.Counter));
            metric.Increment(value);
        }
        
        /// <summary>
        /// Records a gauge value.
        /// </summary>
        public void RecordGauge(string gauge, double value)
        {
            var metric = _metrics.GetOrAdd(gauge, _ => new PerformanceMetric(gauge, MetricType.Gauge));
            metric.SetGauge(value);
        }
        
        /// <summary>
        /// Tracks memory allocation for an operation.
        /// </summary>
        public async Task<T> TrackMemoryAsync<T>(string operation, Func<Task<T>> func)
        {
            var startMemory = GC.GetTotalMemory(false);
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var result = await func().ConfigureAwait(false);
                
                var endMemory = GC.GetTotalMemory(false);
                var memoryUsed = (endMemory - startMemory) / (1024.0 * 1024.0); // Convert to MB
                
                RecordGauge($"{operation}.memory_mb", memoryUsed);
                RecordDuration(operation, stopwatch.Elapsed);
                
                if (memoryUsed > HighMemoryThresholdMB)
                {
                    _logger.Warn($"High memory usage in '{operation}': {memoryUsed:F2} MB");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                IncrementCounter($"{operation}.errors");
                _logger.Error(ex, $"Error in tracked operation '{operation}'");
                throw;
            }
        }
        
        /// <summary>
        /// Gets current metrics snapshot.
        /// </summary>
        public PerformanceSnapshot GetSnapshot()
        {
            lock (_statsLock)
            {
                var snapshot = new PerformanceSnapshot
                {
                    Timestamp = DateTime.UtcNow,
                    Metrics = _metrics.Values.Select(m => m.GetSummary()).ToList(),
                    SystemMetrics = GetSystemMetrics()
                };
                
                // Calculate percentiles for key operations
                foreach (var kvp in _histograms)
                {
                    var values = kvp.Value.GetAll().OrderBy(v => v).ToArray();
                    if (values.Length > 0)
                    {
                        snapshot.Percentiles[kvp.Key] = new PercentileData
                        {
                            P50 = GetPercentile(values, 50),
                            P90 = GetPercentile(values, 90),
                            P95 = GetPercentile(values, 95),
                            P99 = GetPercentile(values, 99)
                        };
                    }
                }
                
                return snapshot;
            }
        }
        
        /// <summary>
        /// Analyzes performance trends and detects anomalies.
        /// </summary>
        public PerformanceAnalysis AnalyzePerformance()
        {
            var analysis = new PerformanceAnalysis();
            
            foreach (var metric in _metrics.Values)
            {
                var summary = metric.GetSummary();
                
                // Detect performance degradation
                if (metric.Type == MetricType.Timer)
                {
                    if (summary.Average > SlowRequestThresholdMs)
                    {
                        analysis.Issues.Add(new PerformanceIssue
                        {
                            Severity = IssueSeverity.Warning,
                            Operation = summary.Name,
                            Message = $"Average duration {summary.Average:F2}ms exceeds threshold",
                            Recommendation = "Consider optimizing this operation or adding caching"
                        });
                    }
                    
                    // Check for high variance (unstable performance)
                    if (summary.Count > 10 && summary.StandardDeviation > summary.Average * 0.5)
                    {
                        analysis.Issues.Add(new PerformanceIssue
                        {
                            Severity = IssueSeverity.Info,
                            Operation = summary.Name,
                            Message = "High performance variance detected",
                            Recommendation = "Investigate inconsistent response times"
                        });
                    }
                }
                
                // Detect high error rates
                if (metric.Name.EndsWith(".errors") && summary.Total > 0)
                {
                    var successMetric = _metrics.Values.FirstOrDefault(m => 
                        m.Name == metric.Name.Replace(".errors", ""));
                    
                    if (successMetric != null)
                    {
                        var errorRate = summary.Total / (double)(successMetric.GetSummary().Count + summary.Total);
                        if (errorRate > 0.05) // 5% error rate threshold
                        {
                            analysis.Issues.Add(new PerformanceIssue
                            {
                                Severity = IssueSeverity.Critical,
                                Operation = metric.Name,
                                Message = $"High error rate: {errorRate:P}",
                                Recommendation = "Investigate and fix errors immediately"
                            });
                        }
                    }
                }
            }
            
            // Check system metrics
            var systemMetrics = GetSystemMetrics();
            if (systemMetrics.CpuUsagePercent > HighCpuThresholdPercent)
            {
                analysis.Issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Warning,
                    Operation = "System",
                    Message = $"High CPU usage: {systemMetrics.CpuUsagePercent:F1}%",
                    Recommendation = "Consider scaling resources or optimizing CPU-intensive operations"
                });
            }
            
            if (systemMetrics.MemoryUsageMB > HighMemoryThresholdMB)
            {
                analysis.Issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Warning,
                    Operation = "System",
                    Message = $"High memory usage: {systemMetrics.MemoryUsageMB:F1} MB",
                    Recommendation = "Review memory allocations and consider implementing object pooling"
                });
            }
            
            analysis.Summary = GeneratePerformanceSummary();
            return analysis;
        }
        
        private void ReportMetrics(object state)
        {
            try
            {
                var snapshot = GetSnapshot();
                var analysis = AnalyzePerformance();
                
                _logger.Info("=== Performance Report ===");
                _logger.Info($"Timestamp: {snapshot.Timestamp:yyyy-MM-dd HH:mm:ss}");
                _logger.Info($"Total Operations: {snapshot.Metrics.Sum(m => m.Count)}");
                
                // Report top slow operations
                var slowOps = snapshot.Metrics
                    .Where(m => m.Type == MetricType.Timer && m.Average > 1000)
                    .OrderByDescending(m => m.Average)
                    .Take(5);
                
                foreach (var op in slowOps)
                {
                    _logger.Info($"  {op.Name}: Avg={op.Average:F2}ms, Max={op.Max:F2}ms, Count={op.Count}");
                }
                
                // Report issues
                if (analysis.Issues.Any())
                {
                    _logger.Warn($"Performance issues detected: {analysis.Issues.Count}");
                    foreach (var issue in analysis.Issues.Where(i => i.Severity >= IssueSeverity.Warning))
                    {
                        _logger.Warn($"  [{issue.Severity}] {issue.Operation}: {issue.Message}");
                    }
                }
                
                // Clear old histogram data to prevent memory growth
                foreach (var histogram in _histograms.Values)
                {
                    if (histogram.Count > 500)
                    {
                        histogram.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating performance report");
            }
        }
        
        private async Task CollectSystemMetrics()
        {
            while (true)
            {
                try
                {
                    var process = Process.GetCurrentProcess();
                    
                    // CPU usage (requires two samples)
                    var startTime = DateTime.UtcNow;
                    var startCpuTime = process.TotalProcessorTime;
                    
                    await Task.Delay(1000).ConfigureAwait(false);
                    
                    var endTime = DateTime.UtcNow;
                    var endCpuTime = process.TotalProcessorTime;
                    
                    var cpuUsedMs = (endCpuTime - startCpuTime).TotalMilliseconds;
                    var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                    var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                    
                    RecordGauge("system.cpu_percent", cpuUsageTotal * 100);
                    RecordGauge("system.memory_mb", process.WorkingSet64 / (1024.0 * 1024.0));
                    RecordGauge("system.threads", process.Threads.Count);
                    RecordGauge("system.handles", process.HandleCount);
                    
                    // GC metrics
                    RecordGauge("gc.gen0_collections", GC.CollectionCount(0));
                    RecordGauge("gc.gen1_collections", GC.CollectionCount(1));
                    RecordGauge("gc.gen2_collections", GC.CollectionCount(2));
                    RecordGauge("gc.total_memory_mb", GC.GetTotalMemory(false) / (1024.0 * 1024.0));
                    
                    await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error collecting system metrics");
                    await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                }
            }
        }
        
        private SystemMetrics GetSystemMetrics()
        {
            return new SystemMetrics
            {
                CpuUsagePercent = _metrics.TryGetValue("system.cpu_percent", out var cpu) 
                    ? cpu.GetSummary().Current : 0,
                MemoryUsageMB = _metrics.TryGetValue("system.memory_mb", out var mem) 
                    ? mem.GetSummary().Current : 0,
                ThreadCount = _metrics.TryGetValue("system.threads", out var threads) 
                    ? (int)threads.GetSummary().Current : 0,
                GCGen0Collections = _metrics.TryGetValue("gc.gen0_collections", out var gc0) 
                    ? (long)gc0.GetSummary().Current : 0,
                GCGen2Collections = _metrics.TryGetValue("gc.gen2_collections", out var gc2) 
                    ? (long)gc2.GetSummary().Current : 0
            };
        }
        
        private double GetPercentile(double[] sortedValues, int percentile)
        {
            if (sortedValues.Length == 0) return 0;
            
            var index = (percentile / 100.0) * (sortedValues.Length - 1);
            var lower = (int)Math.Floor(index);
            var upper = (int)Math.Ceiling(index);
            
            if (lower == upper) return sortedValues[lower];
            
            var weight = index - lower;
            return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
        }
        
        private string GeneratePerformanceSummary()
        {
            var totalOps = _metrics.Values.Where(m => m.Type == MetricType.Timer).Sum(m => m.GetSummary().Count);
            var avgResponseTime = _metrics.Values
                .Where(m => m.Type == MetricType.Timer && m.GetSummary().Count > 0)
                .Select(m => m.GetSummary().Average)
                .DefaultIfEmpty(0)
                .Average();
            
            var errorCount = _metrics.Values
                .Where(m => m.Name.EndsWith(".errors"))
                .Sum(m => m.GetSummary().Total);
            
            return $"Total operations: {totalOps}, Avg response: {avgResponseTime:F2}ms, Errors: {errorCount}";
        }
        
        private void OnCriticalPerformanceEvent(string operation, TimeSpan duration)
        {
            // Raise event for external monitoring
            CriticalPerformanceDetected?.Invoke(this, new PerformanceEventArgs
            {
                Operation = operation,
                Duration = duration,
                Timestamp = DateTime.UtcNow
            });
        }
        
        public event EventHandler<PerformanceEventArgs> CriticalPerformanceDetected;
        
        public void Dispose()
        {
            _reportingTimer?.Dispose();
        }
    }
    
    /// <summary>
    /// Individual performance metric tracking.
    /// </summary>
    internal class PerformanceMetric
    {
        private long _count;
        private double _sum;
        private double _sumSquares;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private double _current;
        private readonly object _lock = new object();
        
        public string Name { get; }
        public MetricType Type { get; }
        
        public PerformanceMetric(string name, MetricType type = MetricType.Timer)
        {
            Name = name;
            Type = type;
        }
        
        public void Record(double value)
        {
            lock (_lock)
            {
                _count++;
                _sum += value;
                _sumSquares += value * value;
                _min = Math.Min(_min, value);
                _max = Math.Max(_max, value);
                _current = value;
            }
        }
        
        public void Increment(long value = 1)
        {
            lock (_lock)
            {
                _count++;
                _sum += value;
                _current += value;
            }
        }
        
        public void SetGauge(double value)
        {
            lock (_lock)
            {
                _current = value;
                _count++;
            }
        }
        
        public MetricSummary GetSummary()
        {
            lock (_lock)
            {
                if (_count == 0)
                {
                    return new MetricSummary { Name = Name, Type = Type };
                }
                
                var average = _sum / _count;
                var variance = (_sumSquares / _count) - (average * average);
                
                return new MetricSummary
                {
                    Name = Name,
                    Type = Type,
                    Count = _count,
                    Total = _sum,
                    Average = average,
                    Min = _min == double.MaxValue ? 0 : _min,
                    Max = _max == double.MinValue ? 0 : _max,
                    Current = _current,
                    StandardDeviation = Math.Sqrt(Math.Max(0, variance))
                };
            }
        }
    }
    
    /// <summary>
    /// Circular buffer for histogram data.
    /// </summary>
    internal class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private int _position;
        private int _count;
        private readonly object _lock = new object();
        
        public int Count => _count;
        
        public CircularBuffer(int capacity)
        {
            _buffer = new T[capacity];
        }
        
        public void Add(T item)
        {
            lock (_lock)
            {
                _buffer[_position] = item;
                _position = (_position + 1) % _buffer.Length;
                _count = Math.Min(_count + 1, _buffer.Length);
            }
        }
        
        public T[] GetAll()
        {
            lock (_lock)
            {
                var result = new T[_count];
                if (_count < _buffer.Length)
                {
                    Array.Copy(_buffer, result, _count);
                }
                else
                {
                    Array.Copy(_buffer, _position, result, 0, _buffer.Length - _position);
                    Array.Copy(_buffer, 0, result, _buffer.Length - _position, _position);
                }
                return result;
            }
        }
        
        public void Clear()
        {
            lock (_lock)
            {
                _position = 0;
                _count = 0;
            }
        }
    }
    
    #region Data Models
    
    public interface IPerformanceMonitor : IDisposable
    {
        void RecordDuration(string operation, TimeSpan duration);
        void IncrementCounter(string counter, long value = 1);
        void RecordGauge(string gauge, double value);
        Task<T> TrackMemoryAsync<T>(string operation, Func<Task<T>> func);
        PerformanceSnapshot GetSnapshot();
        PerformanceAnalysis AnalyzePerformance();
        event EventHandler<PerformanceEventArgs> CriticalPerformanceDetected;
    }
    
    public enum MetricType
    {
        Timer,
        Counter,
        Gauge
    }
    
    public class MetricSummary
    {
        public string Name { get; set; }
        public MetricType Type { get; set; }
        public long Count { get; set; }
        public double Total { get; set; }
        public double Average { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Current { get; set; }
        public double StandardDeviation { get; set; }
    }
    
    public class PerformanceSnapshot
    {
        public DateTime Timestamp { get; set; }
        public List<MetricSummary> Metrics { get; set; } = new List<MetricSummary>();
        public Dictionary<string, PercentileData> Percentiles { get; set; } = new Dictionary<string, PercentileData>();
        public SystemMetrics SystemMetrics { get; set; }
    }
    
    public class PercentileData
    {
        public double P50 { get; set; }
        public double P90 { get; set; }
        public double P95 { get; set; }
        public double P99 { get; set; }
    }
    
    public class SystemMetrics
    {
        public double CpuUsagePercent { get; set; }
        public double MemoryUsageMB { get; set; }
        public int ThreadCount { get; set; }
        public long GCGen0Collections { get; set; }
        public long GCGen2Collections { get; set; }
    }
    
    public class PerformanceAnalysis
    {
        public string Summary { get; set; }
        public List<PerformanceIssue> Issues { get; set; } = new List<PerformanceIssue>();
    }
    
    public class PerformanceIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Operation { get; set; }
        public string Message { get; set; }
        public string Recommendation { get; set; }
    }
    
    public enum IssueSeverity
    {
        Info,
        Warning,
        Critical
    }
    
    public class PerformanceEventArgs : EventArgs
    {
        public string Operation { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    #endregion
}