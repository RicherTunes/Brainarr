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
            _logger.Info($"Rate limiter configured for {resource}: {maxRequests} requests per {period.TotalSeconds}s");
        }

        public async Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action)
        {
            // Get configured limiter or create default if not configured
            if (!_limiters.TryGetValue(resource, out var limiter))
            {
                limiter = _limiters.GetOrAdd(resource, 
                    key => new ResourceRateLimiter(10, TimeSpan.FromMinutes(1), _logger));
            }
            
            return await limiter.ExecuteAsync(action);
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
                // Calculate wait time and reserve slot
                DateTime scheduledTime;
                lock (_lock)
                {
                    // Clean expired requests first
                    CleanOldRequests();
                    
                    // Calculate minimum interval between requests
                    var minInterval = TimeSpan.FromMilliseconds(_period.TotalMilliseconds / _maxRequests);
                    
                    // Find the next available slot
                    var now = DateTime.UtcNow;
                    scheduledTime = now;
                    
                    // If we have recent requests, ensure minimum spacing
                    if (_requestTimes.Count > 0)
                    {
                        var lastScheduledTime = _requestTimes.Last();
                        var nextAvailableTime = lastScheduledTime.AddMilliseconds(minInterval.TotalMilliseconds);
                        
                        if (nextAvailableTime > scheduledTime)
                        {
                            scheduledTime = nextAvailableTime;
                        }
                    }
                    
                    // Also check if we're at capacity for the time window
                    if (_requestTimes.Count >= _maxRequests)
                    {
                        var oldestRequest = _requestTimes.Peek();
                        var windowExpiry = oldestRequest.Add(_period);
                        
                        if (windowExpiry > scheduledTime)
                        {
                            scheduledTime = windowExpiry;
                        }
                    }
                    
                    // Reserve this time slot
                    _requestTimes.Enqueue(scheduledTime);
                }
                
                // Wait until scheduled time
                var waitTime = scheduledTime - DateTime.UtcNow;
                if (waitTime > TimeSpan.Zero)
                {
                    _logger.Debug($"Rate limit reached, waiting {waitTime.TotalMilliseconds:F0}ms");
                    await Task.Delay(waitTime);
                }
                
                // Execute the action
                try
                {
                    return await action();
                }
                finally
                {
                    // Clean up old requests after execution
                    lock (_lock)
                    {
                        CleanOldRequests();
                    }
                }
            }


            private void CleanOldRequests()
            {
                var now = DateTime.UtcNow;
                var cutoff = now - _period;
                
                // Remove requests that have expired (scheduled time is in the past and older than the period)
                while (_requestTimes.Count > 0)
                {
                    var scheduledTime = _requestTimes.Peek();
                    
                    // If this request is scheduled for the future, keep it
                    if (scheduledTime > now)
                        break;
                    
                    // If this request is in the past and older than the cutoff, remove it
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
        }
    }

    // Provider-specific rate limiters
    public static class RateLimiterConfiguration
    {
        public static void ConfigureDefaults(IRateLimiter rateLimiter)
        {
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