using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            _limiters[resource] = new ResourceRateLimiter(maxRequests, period, _logger);
            _logger.Info($"Rate limiter configured for {resource}: {maxRequests} requests per {period.TotalSeconds}s");
        }

        public async Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action)
        {
            var limiter = _limiters.GetOrAdd(resource, 
                key => new ResourceRateLimiter(10, TimeSpan.FromMinutes(1), _logger)); // Default: 10 per minute
            
            return await limiter.ExecuteAsync(action);
        }

        private class ResourceRateLimiter
        {
            private readonly int _maxRequests;
            private readonly TimeSpan _period;
            private readonly Queue<DateTime> _requestTimes;
            private readonly SemaphoreSlim _semaphore;
            private readonly Logger _logger;

            public ResourceRateLimiter(int maxRequests, TimeSpan period, Logger logger)
            {
                _maxRequests = maxRequests;
                _period = period;
                _semaphore = new SemaphoreSlim(1, 1); // Use semaphore for thread-safe access
                _requestTimes = new Queue<DateTime>();
                _logger = logger;
            }

            public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
            {
                await _semaphore.WaitAsync();
                
                try
                {
                    // Clean old requests and check if we need to wait
                    await WaitIfNeededAsync();
                    
                    // Record the request time
                    var startTime = DateTime.UtcNow;
                    _requestTimes.Enqueue(startTime);
                    
                    // Execute the action
                    return await action();
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            private async Task WaitIfNeededAsync()
            {
                CleanOldRequests();
                
                if (_requestTimes.Count >= _maxRequests)
                {
                    var oldestRequest = _requestTimes.Peek();
                    var timeSinceOldest = DateTime.UtcNow - oldestRequest;
                    
                    if (timeSinceOldest < _period)
                    {
                        var waitTime = _period - timeSinceOldest;
                        _logger.Debug($"Rate limit reached, waiting {waitTime.TotalSeconds:F1}s");
                        await Task.Delay(waitTime);
                        
                        // Clean again after waiting
                        CleanOldRequests();
                    }
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