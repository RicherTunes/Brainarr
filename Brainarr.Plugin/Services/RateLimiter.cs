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

    public class RateLimiter : IRateLimiter, IDisposable
    {
        private readonly ConcurrentDictionary<string, ResourceRateLimiter> _limiters;
        private readonly Logger _logger;
        private bool _disposed;

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
            if (_disposed)
                throw new ObjectDisposedException(nameof(RateLimiter));

            var limiter = _limiters.GetOrAdd(resource, 
                key => new ResourceRateLimiter(10, TimeSpan.FromMinutes(1), _logger)); // Default: 10 per minute
            
            return await limiter.ExecuteAsync(action);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var limiter in _limiters.Values)
                {
                    limiter.Dispose();
                }
                _limiters.Clear();
                _disposed = true;
            }
        }

        private class ResourceRateLimiter : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private readonly int _maxRequests;
            private readonly TimeSpan _period;
            private readonly Queue<DateTime> _requestTimes;
            private readonly ReaderWriterLockSlim _lock;
            private readonly Logger _logger;
            private readonly Timer _cleanupTimer;
            private bool _disposed;

            public ResourceRateLimiter(int maxRequests, TimeSpan period, Logger logger)
            {
                _maxRequests = maxRequests;
                _period = period;
                _semaphore = new SemaphoreSlim(maxRequests, maxRequests);
                _requestTimes = new Queue<DateTime>();
                _lock = new ReaderWriterLockSlim();
                _logger = logger;
                
                // Periodic cleanup to prevent memory leaks
                _cleanupTimer = new Timer(_ => CleanOldRequestsSafe(), null, 
                    TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }

            public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ResourceRateLimiter));

                await WaitIfNeededAsync();
                
                try
                {
                    var startTime = DateTime.UtcNow;
                    
                    // Record the request time before executing
                    _lock.EnterWriteLock();
                    try
                    {
                        _requestTimes.Enqueue(startTime);
                        CleanOldRequests();
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                    
                    // Execute the action
                    var result = await action();
                    
                    return result;
                }
                finally
                {
                    // Release immediately - the rate limiting is handled by the queue tracking
                    _semaphore.Release();
                }
            }

            private async Task WaitIfNeededAsync()
            {
                // Wait for a slot to be available
                await _semaphore.WaitAsync();
                
                // Check if we need to wait based on the sliding window
                TimeSpan? waitTime = null;
                
                _lock.EnterReadLock();
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
                        }
                    }
                }
                finally
                {
                    _lock.ExitReadLock();
                }
                
                // Wait outside of the lock if needed
                if (waitTime.HasValue && waitTime.Value > TimeSpan.Zero)
                {
                    _logger.Debug($"Rate limit reached, waiting {waitTime.Value.TotalSeconds:F1}s");
                    await Task.Delay(waitTime.Value);
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

            private void CleanOldRequestsSafe()
            {
                if (_disposed)
                    return;

                _lock.EnterWriteLock();
                try
                {
                    CleanOldRequests();
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _cleanupTimer?.Dispose();
                    _semaphore?.Dispose();
                    _lock?.Dispose();
                    _disposed = true;
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
            
            // Anthropic - reasonable rate limits
            rateLimiter.Configure("anthropic", 15, TimeSpan.FromMinutes(1));
            
            // Google Gemini - generous rate limits
            rateLimiter.Configure("gemini", 20, TimeSpan.FromMinutes(1));
            
            // Groq - fast inference, reasonable limits
            rateLimiter.Configure("groq", 30, TimeSpan.FromMinutes(1));
            
            // DeepSeek - moderate rate limits
            rateLimiter.Configure("deepseek", 15, TimeSpan.FromMinutes(1));
            
            // Perplexity - moderate rate limits
            rateLimiter.Configure("perplexity", 10, TimeSpan.FromMinutes(1));
            
            // OpenRouter - depends on underlying model
            rateLimiter.Configure("openrouter", 10, TimeSpan.FromMinutes(1));
            
            // MusicBrainz - requires 1 request per second
            rateLimiter.Configure("musicbrainz", 1, TimeSpan.FromSeconds(1));
        }
    }
}