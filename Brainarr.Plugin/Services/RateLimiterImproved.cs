using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Brainarr.Plugin.Services
{
    /// <summary>
    /// Improved thread-safe rate limiter with proper async/await patterns
    /// </summary>
    public class RateLimiterImproved : IRateLimiter
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, ResourceRateLimiter> _limiters;
        private static readonly Dictionary<string, (int requests, TimeSpan period)> DefaultLimits = new()
        {
            { "OpenAI", (10, TimeSpan.FromMinutes(1)) },
            { "Anthropic", (20, TimeSpan.FromMinutes(1)) },
            { "Google", (60, TimeSpan.FromMinutes(1)) },
            { "Mistral", (30, TimeSpan.FromMinutes(1)) },
            { "Groq", (30, TimeSpan.FromMinutes(1)) },
            { "OpenRouter", (60, TimeSpan.FromMinutes(1)) },
            { "Cohere", (100, TimeSpan.FromMinutes(1)) },
            { "Ollama", (30, TimeSpan.FromMinutes(1)) },
            { "LMStudio", (30, TimeSpan.FromMinutes(1)) },
            { "MusicBrainz", (1, TimeSpan.FromSeconds(1)) } // New: MusicBrainz rate limiting
        };

        public RateLimiterImproved(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _limiters = new ConcurrentDictionary<string, ResourceRateLimiter>();
        }

        public async Task<T> ExecuteAsync<T>(
            string resource, 
            Func<Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(resource))
                throw new ArgumentException("Resource cannot be null or empty", nameof(resource));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var limiter = GetOrCreateLimiter(resource);
            return await limiter.ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
        }

        private ResourceRateLimiter GetOrCreateLimiter(string resource)
        {
            return _limiters.GetOrAdd(resource, key =>
            {
                if (DefaultLimits.TryGetValue(key, out var limits))
                {
                    _logger.Debug($"Creating rate limiter for {key}: {limits.requests} requests per {limits.period}");
                    return new ResourceRateLimiter(limits.requests, limits.period, _logger);
                }
                
                // Default fallback for unknown resources
                _logger.Warn($"No rate limit defined for {key}, using default: 30 req/min");
                return new ResourceRateLimiter(30, TimeSpan.FromMinutes(1), _logger);
            });
        }

        public void SetCustomLimit(string resource, int maxRequests, TimeSpan period)
        {
            if (string.IsNullOrWhiteSpace(resource))
                throw new ArgumentException("Resource cannot be null or empty", nameof(resource));
            if (maxRequests <= 0)
                throw new ArgumentException("Max requests must be positive", nameof(maxRequests));
            if (period <= TimeSpan.Zero)
                throw new ArgumentException("Period must be positive", nameof(period));

            var limiter = new ResourceRateLimiter(maxRequests, period, _logger);
            _limiters.AddOrUpdate(resource, limiter, (k, v) => limiter);
            _logger.Info($"Custom rate limit set for {resource}: {maxRequests} requests per {period}");
        }

        public RateLimiterStats GetStats(string resource = null)
        {
            if (resource != null)
            {
                if (_limiters.TryGetValue(resource, out var limiter))
                {
                    return limiter.GetStats();
                }
                return new RateLimiterStats { Resource = resource };
            }

            // Aggregate stats for all resources
            var allStats = new RateLimiterStats
            {
                Resource = "All",
                TotalRequests = _limiters.Sum(kvp => kvp.Value.GetStats().TotalRequests),
                ThrottledRequests = _limiters.Sum(kvp => kvp.Value.GetStats().ThrottledRequests),
                AverageWaitTime = _limiters.Any() 
                    ? TimeSpan.FromMilliseconds(_limiters.Average(kvp => kvp.Value.GetStats().AverageWaitTime.TotalMilliseconds))
                    : TimeSpan.Zero
            };
            return allStats;
        }

        private class ResourceRateLimiter
        {
            private readonly SemaphoreSlim _semaphore;
            private readonly int _maxRequests;
            private readonly TimeSpan _period;
            private readonly Queue<DateTime> _requestTimes;
            private readonly SemaphoreSlim _queueLock;
            private readonly ILogger _logger;
            
            // Statistics
            private long _totalRequests;
            private long _throttledRequests;
            private double _totalWaitTimeMs;

            public ResourceRateLimiter(int maxRequests, TimeSpan period, ILogger logger)
            {
                _maxRequests = maxRequests;
                _period = period;
                _semaphore = new SemaphoreSlim(maxRequests, maxRequests);
                _requestTimes = new Queue<DateTime>();
                _queueLock = new SemaphoreSlim(1, 1);
                _logger = logger;
            }

            public async Task<T> ExecuteAsync<T>(
                Func<Task<T>> action, 
                CancellationToken cancellationToken)
            {
                var startTime = DateTime.UtcNow;
                var wasThrottled = false;
                var waitTime = TimeSpan.Zero;

                try
                {
                    await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    
                    // Use async-safe lock
                    await _queueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        CleanOldRequests();
                        
                        if (_requestTimes.Count >= _maxRequests)
                        {
                            var oldestRequest = _requestTimes.Peek();
                            var timeSinceOldest = DateTime.UtcNow - oldestRequest;
                            
                            if (timeSinceOldest < _period)
                            {
                                waitTime = _period - timeSinceOldest;
                                wasThrottled = true;
                                _logger.Debug($"Rate limit reached, waiting {waitTime.TotalSeconds:F1}s");
                            }
                        }
                    }
                    finally
                    {
                        _queueLock.Release();
                    }

                    // Wait outside of lock if needed
                    if (wasThrottled)
                    {
                        Interlocked.Increment(ref _throttledRequests);
                        Interlocked.Add(ref _totalWaitTimeMs, (long)waitTime.TotalMilliseconds);
                        await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
                    }

                    // Execute the action
                    Interlocked.Increment(ref _totalRequests);
                    var result = await action().ConfigureAwait(false);
                    
                    // Record successful request
                    await _queueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        _requestTimes.Enqueue(startTime);
                    }
                    finally
                    {
                        _queueLock.Release();
                    }
                    
                    return result;
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            private void CleanOldRequests()
            {
                var cutoff = DateTime.UtcNow - _period;
                while (_requestTimes.Count > 0 && _requestTimes.Peek() < cutoff)
                {
                    _requestTimes.Dequeue();
                }
            }

            public RateLimiterStats GetStats()
            {
                return new RateLimiterStats
                {
                    TotalRequests = Interlocked.Read(ref _totalRequests),
                    ThrottledRequests = Interlocked.Read(ref _throttledRequests),
                    AverageWaitTime = _throttledRequests > 0 
                        ? TimeSpan.FromMilliseconds(_totalWaitTimeMs / _throttledRequests)
                        : TimeSpan.Zero,
                    CurrentQueueSize = _requestTimes.Count
                };
            }
        }
    }

    public class RateLimiterStats
    {
        public string Resource { get; set; }
        public long TotalRequests { get; set; }
        public long ThrottledRequests { get; set; }
        public TimeSpan AverageWaitTime { get; set; }
        public int CurrentQueueSize { get; set; }
        
        public double ThrottleRate => TotalRequests > 0 
            ? (double)ThrottledRequests / TotalRequests * 100 
            : 0;
    }

    public interface IRateLimiter
    {
        Task<T> ExecuteAsync<T>(
            string resource, 
            Func<Task<T>> action,
            CancellationToken cancellationToken = default);
            
        void SetCustomLimit(string resource, int maxRequests, TimeSpan period);
        
        RateLimiterStats GetStats(string resource = null);
    }
}