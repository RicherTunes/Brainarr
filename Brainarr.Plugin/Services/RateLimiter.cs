using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IRateLimiter
    {
        Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action);
        Task<T> ExecuteAsync<T>(string resource, Func<System.Threading.CancellationToken, Task<T>> action, System.Threading.CancellationToken cancellationToken);
        void Configure(string resource, int maxRequests, TimeSpan period);
    }

    public class RateLimiter : IRateLimiter
    {
        private readonly ConcurrentDictionary<string, ResourceRateLimiter> _limiters;
        private readonly Logger _logger;

        public RateLimiter(Logger logger)
        {
            _logger = logger;
            _limiters = new ConcurrentDictionary<string, ResourceRateLimiter>();
        }

        public void Configure(string resource, int maxRequests, TimeSpan period)
        {
            // Validate and sanitize parameters
            if (string.IsNullOrWhiteSpace(resource))
            {
                _logger.Warn("Rate limiter resource name cannot be null or empty, using 'default'");
                resource = "default";
            }
            
            if (maxRequests <= 0)
            {
                _logger.Warn($"Invalid maxRequests ({maxRequests}), using default value of 10");
                maxRequests = 10;
            }
            
            if (period <= TimeSpan.Zero)
            {
                _logger.Warn($"Invalid period ({period}), using default value of 1 minute");
                period = TimeSpan.FromMinutes(1);
            }
            
            _limiters[resource] = new ResourceRateLimiter(maxRequests, period, _logger);
            _logger.Debug($"Rate limiter configured for {resource}: {maxRequests} requests per {period.TotalSeconds}s");
        }

        public async Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action)
        {
            // Get configured limiter or create default if not configured
            if (!_limiters.TryGetValue(resource, out var limiter))
            {
                // By default, do not throttle unless explicitly configured
                return await action().ConfigureAwait(false);
            }

            return await limiter.ExecuteAsync(action).ConfigureAwait(false);
        }

        public async Task<T> ExecuteAsync<T>(string resource, Func<System.Threading.CancellationToken, Task<T>> action, System.Threading.CancellationToken cancellationToken)
        {
            if (!_limiters.TryGetValue(resource, out var limiter))
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            return await limiter.ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
        }

        private class ResourceRateLimiter
        {
            private readonly int _maxRequests;
            private readonly TimeSpan _period;
            private readonly Queue<DateTime> _requestTimes;
            private readonly object _lock = new object();
            private readonly Logger _logger;

            public ResourceRateLimiter(int maxRequests, TimeSpan period, Logger logger)
            {
                _maxRequests = maxRequests;
                _period = period;
                _requestTimes = new Queue<DateTime>();
                _logger = logger;
            }

            public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
            {
                // Enforce smooth minimum spacing between requests (leaky-bucket style)
                DateTime scheduledTime;
                TimeSpan waitTime;

                lock (_lock)
                {
                    CleanOldRequests();

                    var now = DateTime.UtcNow;
                    var minInterval = TimeSpan.FromMilliseconds(Math.Max(1, _period.TotalMilliseconds / Math.Max(1, _maxRequests)));

                    // Default to now; if we have prior scheduled requests, enforce min spacing from the last one
                    scheduledTime = now;
                    if (_requestTimes.Count > 0)
                    {
                        var lastScheduled = _requestTimes.Last();
                        var nextAllowed = lastScheduled.Add(minInterval);
                        if (nextAllowed > scheduledTime)
                        {
                            scheduledTime = nextAllowed;
                        }
                    }

                    _requestTimes.Enqueue(scheduledTime);
                    waitTime = scheduledTime - now;
                }

                if (waitTime > TimeSpan.Zero)
                {
                    _logger.Debug($"Rate limit waiting {waitTime.TotalMilliseconds:F0}ms");
                    await Task.Delay(waitTime).ConfigureAwait(false);
                }

                try
                {
                    return await action().ConfigureAwait(false);
                }
                finally
                {
                    lock (_lock)
                    {
                        CleanOldRequests();
                    }
                }
            }


            private void CleanOldRequests()
            {
                // Sliding Window Cleanup Algorithm
                // Maintains request history for accurate rate limiting while removing expired entries
                // Three categories of requests:
                // 1. Future (scheduled > now): Keep for rate limit enforcement
                // 2. Recent (cutoff < scheduled <= now): Keep for window calculation
                // 3. Expired (scheduled < cutoff): Remove to free memory
                
                var now = DateTime.UtcNow;
                var cutoff = now - _period;
                
                // Process queue from oldest to newest
                while (_requestTimes.Count > 0)
                {
                    var scheduledTime = _requestTimes.Peek();
                    
                    // Future requests must be preserved for rate limiting
                    if (scheduledTime > now)
                        break;
                    
                    // Remove only if outside the sliding window
                    if (scheduledTime < cutoff)
                    {
                        _requestTimes.Dequeue();
                    }
                    else
                    {
                        // This request is within the time window, keep it
                        break;
                    }
                }
            }

            public async Task<T> ExecuteAsync<T>(Func<System.Threading.CancellationToken, Task<T>> action, System.Threading.CancellationToken cancellationToken)
            {
                // Same scheduling as above, but cancellation-aware
                DateTime scheduledTime;
                TimeSpan waitTime;

                lock (_lock)
                {
                    CleanOldRequests();
                    var now = DateTime.UtcNow;
                    var minInterval = TimeSpan.FromMilliseconds(Math.Max(1, _period.TotalMilliseconds / Math.Max(1, _maxRequests)));

                    scheduledTime = now;
                    if (_requestTimes.Count > 0)
                    {
                        var lastScheduled = _requestTimes.Last();
                        var nextAllowed = lastScheduled.Add(minInterval);
                        if (nextAllowed > scheduledTime)
                        {
                            scheduledTime = nextAllowed;
                        }
                    }

                    _requestTimes.Enqueue(scheduledTime);
                    waitTime = scheduledTime - now;
                }

                if (waitTime > TimeSpan.Zero)
                {
                    _logger.Debug($"Rate limit waiting {waitTime.TotalMilliseconds:F0}ms");
                    await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await action(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    lock (_lock)
                    {
                        CleanOldRequests();
                    }
                }
            }
        }
    }

    // Provider-specific rate limiters
    public static class RateLimiterConfiguration
    {
        private static readonly HashSet<IRateLimiter> _configuredLimiters = new HashSet<IRateLimiter>();
        private static readonly object _lock = new object();
        
        public static void ConfigureDefaults(IRateLimiter rateLimiter)
        {
            // Only configure each rate limiter instance once to prevent log spam
            lock (_lock)
            {
                if (_configuredLimiters.Contains(rateLimiter))
                {
                    return;
                }
                
                _configuredLimiters.Add(rateLimiter);
            }
            
            // Ollama - local, can handle more requests
            rateLimiter.Configure("ollama", 30, TimeSpan.FromMinutes(1));
            
            // LM Studio - local, can handle more requests
            rateLimiter.Configure("lmstudio", 30, TimeSpan.FromMinutes(1));
            
            // OpenAI - has strict rate limits
            rateLimiter.Configure("openai", 10, TimeSpan.FromMinutes(1));
            
            // MusicBrainz - requires 1 request per second
            rateLimiter.Configure("musicbrainz", 1, TimeSpan.FromSeconds(1));
        }
    }
}
