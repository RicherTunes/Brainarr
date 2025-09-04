using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Performance
{
    /// <summary>
    /// Performance metrics tracking for the Brainarr plugin.
    /// Provides insights into provider response times, cache hit rates, and overall performance.
    /// Thread-safe implementation suitable for high-concurrency scenarios.
    /// </summary>
    public interface IPerformanceMetrics
    {
        /// <summary>
        /// Records the response time for an AI provider request.
        /// Used to track provider performance and identify slow providers.
        /// </summary>
        /// <param name="provider">Name of the AI provider (e.g., "OpenAI", "Ollama")</param>
        /// <param name="duration">Time taken for the provider to respond</param>
        void RecordProviderResponseTime(string provider, TimeSpan duration);
        
        /// <summary>
        /// Records a cache hit event for performance tracking.
        /// </summary>
        /// <param name="cacheKey">The cache key that was found</param>
        void RecordCacheHit(string cacheKey);
        
        /// <summary>
        /// Records a cache miss event for performance tracking.
        /// </summary>
        /// <param name="cacheKey">The cache key that was not found</param>
        void RecordCacheMiss(string cacheKey);
        
        /// <summary>
        /// Records the number of recommendations generated in a batch.
        /// </summary>
        /// <param name="count">Number of recommendations produced</param>
        void RecordRecommendationCount(int count);
        
        /// <summary>
        /// Records the number of duplicate recommendations that were removed.
        /// Helps track the effectiveness of deduplication logic.
        /// </summary>
        /// <param name="count">Number of duplicates removed from the batch</param>
        void RecordDuplicatesRemoved(int count);
        
        /// <summary>
        /// Gets a complete snapshot of current performance metrics.
        /// Provides aggregate statistics for monitoring and analysis.
        /// </summary>
        /// <returns>Immutable snapshot of all collected metrics</returns>
        PerformanceSnapshot GetSnapshot();
        
        /// <summary>
        /// Resets all collected metrics back to zero.
        /// Useful for starting fresh performance monitoring periods.
        /// </summary>
        void Reset();

        // Artist-mode MBID promotion metrics
        void RecordArtistModePromotions(int promotedCount);
        
        // Snapshot convenience already covers totals; explicit getters not necessary
    }

    public class PerformanceMetrics : IPerformanceMetrics
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, ProviderMetrics> _providerMetrics;
        private readonly ConcurrentDictionary<string, CacheMetrics> _cacheMetrics;
        private long _totalRecommendations;
        private long _totalDuplicatesRemoved;
        private long _artistModeGatingEvents;
        private long _artistModePromoted;
        private readonly DateTime _startTime;

        public PerformanceMetrics(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _providerMetrics = new ConcurrentDictionary<string, ProviderMetrics>();
            _cacheMetrics = new ConcurrentDictionary<string, CacheMetrics>();
            _startTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Records the response time for an AI provider request with automatic performance warnings.
        /// Logs warnings for responses taking longer than 10 seconds.
        /// </summary>
        /// <param name="provider">Name of the AI provider</param>
        /// <param name="duration">Actual response time measured</param>
        public void RecordProviderResponseTime(string provider, TimeSpan duration)
        {
            var metrics = _providerMetrics.GetOrAdd(provider, _ => new ProviderMetrics());
            metrics.RecordResponse(duration);
            
            if (duration.TotalSeconds > 10)
            {
                _logger.Warn($"Provider {provider} took {duration.TotalSeconds:F1}s to respond");
            }
        }

        public void RecordCacheHit(string cacheKey)
        {
            var metrics = _cacheMetrics.GetOrAdd("global", _ => new CacheMetrics());
            metrics.RecordHit();
        }

        public void RecordCacheMiss(string cacheKey)
        {
            var metrics = _cacheMetrics.GetOrAdd("global", _ => new CacheMetrics());
            metrics.RecordMiss();
        }

        public void RecordRecommendationCount(int count)
        {
            Interlocked.Add(ref _totalRecommendations, count);
        }

        public void RecordDuplicatesRemoved(int count)
        {
            Interlocked.Add(ref _totalDuplicatesRemoved, count);
        }

        /// <summary>
        /// Gets a comprehensive snapshot of all performance metrics collected since startup or last reset.
        /// Calculates aggregate statistics including averages, hit rates, and duplication rates.
        /// </summary>
        /// <returns>Thread-safe snapshot containing all performance data</returns>
        public PerformanceSnapshot GetSnapshot()
        {
            var uptime = DateTime.UtcNow - _startTime;
            var cacheMetrics = _cacheMetrics.GetOrAdd("global", _ => new CacheMetrics());
            
            return new PerformanceSnapshot
            {
                UptimeMinutes = uptime.TotalMinutes,
                TotalRecommendations = _totalRecommendations,
                TotalDuplicatesRemoved = _totalDuplicatesRemoved,
                CacheHitRate = cacheMetrics.HitRate,
                TotalCacheHits = cacheMetrics.Hits,
                TotalCacheMisses = cacheMetrics.Misses,
                ProviderStats = _providerMetrics.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.GetStats()
                ),
                DuplicationRate = _totalRecommendations > 0 
                    ? (double)_totalDuplicatesRemoved / (_totalRecommendations + _totalDuplicatesRemoved)
                    : 0,
                ArtistModeGatingEvents = _artistModeGatingEvents,
                ArtistModePromotedRecommendations = _artistModePromoted
            };
        }

        public void Reset()
        {
            _providerMetrics.Clear();
            _cacheMetrics.Clear();
            _totalRecommendations = 0;
            _totalDuplicatesRemoved = 0;
            _artistModeGatingEvents = 0;
            _artistModePromoted = 0;
            _logger.Info("Performance metrics reset");
        }

        public void RecordArtistModePromotions(int promotedCount)
        {
            if (promotedCount <= 0) return;
            Interlocked.Increment(ref _artistModeGatingEvents);
            Interlocked.Add(ref _artistModePromoted, promotedCount);
        }

        private class ProviderMetrics
        {
            private long _requestCount;
            private long _totalMilliseconds;
            private long _minMilliseconds = long.MaxValue;
            private long _maxMilliseconds;

            public void RecordResponse(TimeSpan duration)
            {
                var ms = (long)duration.TotalMilliseconds;
                Interlocked.Increment(ref _requestCount);
                Interlocked.Add(ref _totalMilliseconds, ms);
                
                // Update min/max (not perfectly thread-safe but good enough for metrics)
                if (ms < _minMilliseconds)
                    _minMilliseconds = ms;
                if (ms > _maxMilliseconds)
                    _maxMilliseconds = ms;
            }

            public ProviderStats GetStats()
            {
                var count = _requestCount;
                return new ProviderStats
                {
                    RequestCount = count,
                    AverageResponseMs = count > 0 ? _totalMilliseconds / (double)count : 0,
                    MinResponseMs = _minMilliseconds == long.MaxValue ? 0 : _minMilliseconds,
                    MaxResponseMs = _maxMilliseconds
                };
            }
        }

        private class CacheMetrics
        {
            private long _hits;
            private long _misses;

            public long Hits => _hits;
            public long Misses => _misses;
            
            public double HitRate
            {
                get
                {
                    var total = _hits + _misses;
                    return total > 0 ? (double)_hits / total : 0;
                }
            }

            public void RecordHit() => Interlocked.Increment(ref _hits);
            public void RecordMiss() => Interlocked.Increment(ref _misses);
        }
    }

    public class PerformanceSnapshot
    {
        public double UptimeMinutes { get; set; }
        public long TotalRecommendations { get; set; }
        public long TotalDuplicatesRemoved { get; set; }
        public double DuplicationRate { get; set; }
        public double CacheHitRate { get; set; }
        public long TotalCacheHits { get; set; }
        public long TotalCacheMisses { get; set; }
        public Dictionary<string, ProviderStats> ProviderStats { get; set; }
        public long ArtistModeGatingEvents { get; set; }
        public long ArtistModePromotedRecommendations { get; set; }
    }

    public class ProviderStats
    {
        public long RequestCount { get; set; }
        public double AverageResponseMs { get; set; }
        public long MinResponseMs { get; set; }
        public long MaxResponseMs { get; set; }
    }

    /// <summary>
    /// Stopwatch helper for timing operations with automatic metrics recording.
    /// </summary>
    public class MetricStopwatch : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly IPerformanceMetrics _metrics;
        private readonly string _provider;
        private readonly Logger _logger;

        public MetricStopwatch(IPerformanceMetrics metrics, string provider, Logger logger = null)
        {
            _metrics = metrics;
            _provider = provider;
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _metrics?.RecordProviderResponseTime(_provider, _stopwatch.Elapsed);
            
            if (_stopwatch.Elapsed.TotalSeconds > 5)
            {
                _logger?.Debug($"{_provider} operation took {_stopwatch.Elapsed.TotalSeconds:F1}s");
            }
        }
    }
}
