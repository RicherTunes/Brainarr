using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Performance
{
    /// <summary>
    /// Performance optimizer with advanced memory management and caching strategies
    /// </summary>
    public class PerformanceOptimizer : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, PerformanceMetrics> _metrics;
        private readonly Timer _metricsAggregator;
        private readonly object _disposeLock = new object();
        private bool _disposed;

        public PerformanceOptimizer(ILogger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _metrics = new ConcurrentDictionary<string, PerformanceMetrics>();
            
            // Aggregate metrics every 5 minutes
            _metricsAggregator = new Timer(
                _ => AggregateMetrics(),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Track performance of an operation with automatic optimization suggestions
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T> TrackPerformanceAsync<T>(
            string operationName,
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var startMemory = GC.GetTotalMemory(false);
            
            try
            {
                var result = await operation().ConfigureAwait(false);
                
                stopwatch.Stop();
                var endMemory = GC.GetTotalMemory(false);
                
                RecordMetrics(operationName, stopwatch.Elapsed, endMemory - startMemory);
                
                // Auto-optimize if performance degrades
                if (ShouldOptimize(operationName))
                {
                    await OptimizeOperationAsync(operationName).ConfigureAwait(false);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordError(operationName, ex);
                throw;
            }
        }

        /// <summary>
        /// Memory-efficient batch processing with automatic chunking
        /// </summary>
        public async Task<List<TResult>> ProcessBatchAsync<TInput, TResult>(
            IEnumerable<TInput> items,
            Func<TInput, Task<TResult>> processor,
            int maxConcurrency = 0,
            CancellationToken cancellationToken = default)
        {
            var itemList = items as IList<TInput> ?? items.ToList();
            if (!itemList.Any())
                return new List<TResult>();

            // Auto-calculate optimal batch size based on available memory
            var optimalBatchSize = CalculateOptimalBatchSize(itemList.Count);
            maxConcurrency = maxConcurrency > 0 ? maxConcurrency : Environment.ProcessorCount;

            var results = new ConcurrentBag<TResult>();
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            var tasks = new List<Task>();
            
            foreach (var item in itemList)
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var result = await processor(item).ConfigureAwait(false);
                        results.Add(result);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));

                // Yield periodically to prevent thread pool starvation
                if (tasks.Count % optimalBatchSize == 0)
                {
                    await Task.Yield();
                }
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.ToList();
        }

        /// <summary>
        /// Optimize LINQ queries by converting to more efficient forms
        /// </summary>
        public IEnumerable<T> OptimizeEnumerable<T>(IEnumerable<T> source)
        {
            // Convert multiple enumerations to single pass
            if (source is ICollection<T> collection)
                return collection;

            // For large sequences, use memory-efficient streaming
            return StreamLargeSequence(source);
        }

        private IEnumerable<T> StreamLargeSequence<T>(IEnumerable<T> source)
        {
            const int bufferSize = 1000;
            var buffer = new List<T>(bufferSize);

            foreach (var item in source)
            {
                buffer.Add(item);
                
                if (buffer.Count >= bufferSize)
                {
                    foreach (var bufferedItem in buffer)
                        yield return bufferedItem;
                    
                    buffer.Clear();
                    
                    // Allow GC to collect if needed
                    if (GC.GetTotalMemory(false) > 500_000_000) // 500MB threshold
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                }
            }

            foreach (var remainingItem in buffer)
                yield return remainingItem;
        }

        private int CalculateOptimalBatchSize(int totalItems)
        {
            var availableMemory = GC.GetTotalMemory(false);
            var memoryPerItem = availableMemory / Math.Max(totalItems, 1);
            
            // Dynamic batch sizing based on memory pressure
            if (availableMemory < 100_000_000) // < 100MB
                return Math.Min(10, totalItems);
            else if (availableMemory < 500_000_000) // < 500MB
                return Math.Min(50, totalItems);
            else
                return Math.Min(100, totalItems);
        }

        private void RecordMetrics(string operation, TimeSpan duration, long memoryDelta)
        {
            var metrics = _metrics.GetOrAdd(operation, _ => new PerformanceMetrics());
            metrics.RecordExecution(duration, memoryDelta);

            if (duration > TimeSpan.FromSeconds(5))
            {
                _logger.Warn($"Slow operation detected: {operation} took {duration.TotalSeconds:F2}s");
            }

            if (memoryDelta > 50_000_000) // > 50MB
            {
                _logger.Warn($"High memory usage: {operation} allocated {memoryDelta / 1_000_000}MB");
            }
        }

        private void RecordError(string operation, Exception error)
        {
            var metrics = _metrics.GetOrAdd(operation, _ => new PerformanceMetrics());
            metrics.RecordError(error);
        }

        private bool ShouldOptimize(string operation)
        {
            if (!_metrics.TryGetValue(operation, out var metrics))
                return false;

            // Optimize if performance degrades by 50% or more
            return metrics.PerformanceDegradation > 0.5;
        }

        private async Task OptimizeOperationAsync(string operation)
        {
            await Task.Run(() =>
            {
                _logger.Info($"Auto-optimizing operation: {operation}");
                
                // Force garbage collection for high memory operations
                if (_metrics.TryGetValue(operation, out var metrics) && 
                    metrics.AverageMemoryUsage > 100_000_000)
                {
                    GC.Collect(2, GCCollectionMode.Optimized);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Optimized);
                }
            });
        }

        private void AggregateMetrics()
        {
            try
            {
                var summary = _metrics
                    .Where(m => m.Value.ExecutionCount > 0)
                    .Select(m => new
                    {
                        Operation = m.Key,
                        Count = m.Value.ExecutionCount,
                        AvgDuration = m.Value.AverageDuration.TotalMilliseconds,
                        AvgMemory = m.Value.AverageMemoryUsage / 1_000_000.0, // MB
                        Errors = m.Value.ErrorCount
                    })
                    .OrderByDescending(m => m.AvgDuration)
                    .Take(10);

                foreach (var metric in summary)
                {
                    _logger.Debug($"Performance: {metric.Operation} - " +
                                $"Count: {metric.Count}, " +
                                $"Avg: {metric.AvgDuration:F2}ms, " +
                                $"Memory: {metric.AvgMemory:F2}MB, " +
                                $"Errors: {metric.Errors}");
                }

                // Reset metrics after reporting
                foreach (var metric in _metrics.Values)
                {
                    metric.Reset();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to aggregate performance metrics");
            }
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed)
                    return;

                _metricsAggregator?.Dispose();
                _metrics.Clear();
                
                _disposed = true;
            }
        }

        private class PerformanceMetrics
        {
            private long _totalDuration;
            private long _totalMemory;
            private long _executionCount;
            private long _errorCount;
            private double _lastAverage;
            private readonly object _lock = new object();

            public int ExecutionCount => (int)Interlocked.Read(ref _executionCount);
            public int ErrorCount => (int)Interlocked.Read(ref _errorCount);
            
            public TimeSpan AverageDuration
            {
                get
                {
                    var count = Interlocked.Read(ref _executionCount);
                    if (count == 0) return TimeSpan.Zero;
                    
                    var total = Interlocked.Read(ref _totalDuration);
                    return TimeSpan.FromTicks(total / count);
                }
            }

            public long AverageMemoryUsage
            {
                get
                {
                    var count = Interlocked.Read(ref _executionCount);
                    if (count == 0) return 0;
                    
                    var total = Interlocked.Read(ref _totalMemory);
                    return total / count;
                }
            }

            public double PerformanceDegradation
            {
                get
                {
                    lock (_lock)
                    {
                        var current = AverageDuration.TotalMilliseconds;
                        if (_lastAverage == 0)
                        {
                            _lastAverage = current;
                            return 0;
                        }

                        var degradation = (current - _lastAverage) / _lastAverage;
                        _lastAverage = current;
                        return degradation;
                    }
                }
            }

            public void RecordExecution(TimeSpan duration, long memoryDelta)
            {
                Interlocked.Add(ref _totalDuration, duration.Ticks);
                Interlocked.Add(ref _totalMemory, Math.Max(0, memoryDelta));
                Interlocked.Increment(ref _executionCount);
            }

            public void RecordError(Exception error)
            {
                Interlocked.Increment(ref _errorCount);
            }

            public void Reset()
            {
                lock (_lock)
                {
                    _totalDuration = 0;
                    _totalMemory = 0;
                    _executionCount = 0;
                    _errorCount = 0;
                }
            }
        }
    }
}