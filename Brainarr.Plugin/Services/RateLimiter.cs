using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Rate limiter interface for controlling API request frequency.
    /// </summary>
    public interface IRateLimiter
    {
        /// <summary>
        /// Executes an action with rate limiting applied.
        /// </summary>
        Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action);
        
        /// <summary>
        /// Configures rate limits for a specific resource.
        /// </summary>
        void Configure(string resource, int maxRequests, TimeSpan period);
    }

    /// <summary>
    /// Implements a token bucket algorithm with sliding window for rate limiting
    /// API requests to prevent quota exhaustion and comply with provider limits.
    /// </summary>
    /// <remarks>
    /// Algorithm overview:
    /// 1. Each resource gets a bucket with maxRequests tokens
    /// 2. Tokens are consumed when requests are made
    /// 3. Tokens regenerate after the configured period
    /// 4. Sliding window tracks actual request times for accuracy
    /// 5. Semaphore prevents concurrent request overflow
    /// 
    /// This hybrid approach combines the simplicity of token bucket with
    /// the precision of sliding window for optimal rate limiting.
    /// </remarks>
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

        /// <summary>
        /// Per-resource rate limiter implementing a hybrid token bucket/sliding window algorithm.
        /// </summary>
        private class ResourceRateLimiter
        {
            // Semaphore acts as the token bucket, limiting concurrent requests
            private readonly SemaphoreSlim _semaphore;
            private readonly int _maxRequests;
            private readonly TimeSpan _period;
            // Sliding window of actual request times for precise rate tracking
            private readonly Queue<DateTime> _requestTimes;
            private readonly object _lock = new object();
            private readonly Logger _logger;

            public ResourceRateLimiter(int maxRequests, TimeSpan period, Logger logger)
            {
                _maxRequests = maxRequests;
                _period = period;
                // Initialize semaphore with max tokens
                _semaphore = new SemaphoreSlim(maxRequests, maxRequests);
                _requestTimes = new Queue<DateTime>();
                _logger = logger;
            }

            /// <summary>
            /// Executes an action with rate limiting, blocking if necessary to comply with limits.
            /// </summary>
            /// <remarks>
            /// Execution flow:
            /// 1. Acquire token from semaphore (may block)
            /// 2. Check sliding window for rate compliance
            /// 3. Execute the action
            /// 4. Record request time in sliding window
            /// 5. Schedule token regeneration after period expires
            /// 
            /// The delayed semaphore release ensures tokens regenerate at the correct rate,
            /// preventing burst exhaustion of the entire quota.
            /// </remarks>
            public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
            {
                // Wait for available token and check sliding window
                await WaitIfNeededAsync();
                
                try
                {
                    var startTime = DateTime.UtcNow;
                    var result = await action();
                    
                    // Record successful request in sliding window
                    lock (_lock)
                    {
                        _requestTimes.Enqueue(startTime);
                        CleanOldRequests();
                    }
                    
                    return result;
                }
                finally
                {
                    // Schedule token regeneration after the rate limit period
                    // This ensures sustained rate compliance over time
                    _ = Task.Delay(_period).ContinueWith(_ => _semaphore.Release());
                }
            }

            /// <summary>
            /// Waits if necessary to comply with rate limits using sliding window verification.
            /// </summary>
            /// <remarks>
            /// This method implements the sliding window check:
            /// 1. Clean expired requests from the window
            /// 2. If window is full (maxRequests reached in period)
            /// 3. Calculate wait time until oldest request expires
            /// 4. Block thread until rate limit allows proceeding
            /// 
            /// Thread.Sleep is used instead of Task.Delay to ensure accurate
            /// rate limiting under high concurrency scenarios.
            /// </remarks>
            private async Task WaitIfNeededAsync()
            {
                // Acquire token from semaphore (may block if all tokens consumed)
                await _semaphore.WaitAsync();
                
                lock (_lock)
                {
                    // Remove requests outside the sliding window
                    CleanOldRequests();
                    
                    // Check if we've hit the rate limit within the window
                    if (_requestTimes.Count >= _maxRequests)
                    {
                        var oldestRequest = _requestTimes.Peek();
                        var timeSinceOldest = DateTime.UtcNow - oldestRequest;
                        
                        // If oldest request is still within the period, we must wait
                        if (timeSinceOldest < _period)
                        {
                            var waitTime = _period - timeSinceOldest;
                            _logger.Debug($"Rate limit reached, waiting {waitTime.TotalSeconds:F1}s");
                            Thread.Sleep(waitTime); // Block to enforce rate limit
                        }
                    }
                }
            }

            /// <summary>
            /// Removes expired requests from the sliding window queue.
            /// </summary>
            /// <remarks>
            /// Maintains the sliding window by removing all requests older than
            /// the rate limit period. This ensures accurate rate calculation and
            /// prevents memory leak from accumulating old request times.
            /// </remarks>
            private void CleanOldRequests()
            {
                var cutoff = DateTime.UtcNow - _period;
                // Remove all requests older than the rate limit period
                while (_requestTimes.Count > 0 && _requestTimes.Peek() < cutoff)
                {
                    _requestTimes.Dequeue();
                }
            }
        }
    }

    /// <summary>
    /// Configures provider-specific rate limits based on documented API quotas.
    /// </summary>
    /// <remarks>
    /// These limits are conservative to account for:
    /// - Other applications using the same API key
    /// - Rate limit headers not always being accurate
    /// - Burst protection to avoid hitting hard limits
    /// </remarks>
    public static class RateLimiterConfiguration
    {
        /// <summary>
        /// Configures default rate limits for all supported AI providers.
        /// </summary>
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