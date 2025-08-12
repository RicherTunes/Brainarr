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
            private readonly SemaphoreSlim _semaphore;
            private readonly int _maxRequests;
            private readonly TimeSpan _period;
            private readonly Queue<DateTime> _requestTimes;
            private readonly object _lock = new object();
            private readonly Logger _logger;

            public ResourceRateLimiter(int maxRequests, TimeSpan period, Logger logger)
            {
                _maxRequests = maxRequests;
                _period = period;
                _semaphore = new SemaphoreSlim(maxRequests, maxRequests);
                _requestTimes = new Queue<DateTime>();
                _logger = logger;
            }

            public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
            {
                await WaitIfNeededAsync();
                
                try
                {
                    var startTime = DateTime.UtcNow;
                    var result = await action();
                    
                    lock (_lock)
                    {
                        _requestTimes.Enqueue(startTime);
                        CleanOldRequests();
                    }
                    
                    return result;
                }
                finally
                {
                    // Schedule semaphore release after the period
                    _ = Task.Delay(_period).ContinueWith(_ => _semaphore.Release());
                }
            }

            private async Task WaitIfNeededAsync()
            {
                await _semaphore.WaitAsync();
                
                lock (_lock)
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
                            Thread.Sleep(waitTime);
                        }
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