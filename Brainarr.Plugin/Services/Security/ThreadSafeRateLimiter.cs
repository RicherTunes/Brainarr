using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    public interface IThreadSafeRateLimiter
    {
        void Configure(string key, int maxRequests, TimeSpan period);
        Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken = default);
        Task ExecuteAsync(string key, Func<Task> action, CancellationToken cancellationToken = default);
        RateLimitStatus GetStatus(string key);
        void Reset(string key);
        void ResetAll();
    }

    public class ThreadSafeRateLimiter : IThreadSafeRateLimiter, IDisposable
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets;
        private readonly Timer _cleanupTimer;
        private readonly object _globalLock = new object();
        private volatile bool _disposed;

        public ThreadSafeRateLimiter(Logger logger)
        {
            _logger = logger;
            _buckets = new ConcurrentDictionary<string, RateLimitBucket>();
            
            // Start cleanup timer to remove expired entries
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public void Configure(string key, int maxRequests, TimeSpan period)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Rate limiter key cannot be empty", nameof(key));
            }

            if (maxRequests <= 0)
            {
                throw new ArgumentException("Max requests must be positive", nameof(maxRequests));
            }

            if (period <= TimeSpan.Zero)
            {
                throw new ArgumentException("Period must be positive", nameof(period));
            }

            var bucket = _buckets.AddOrUpdate(key,
                k => new RateLimitBucket(maxRequests, period, _logger),
                (k, existing) =>
                {
                    existing.UpdateConfiguration(maxRequests, period);
                    return existing;
                });

            _logger.Debug($"Rate limiter configured for '{key}': {maxRequests} requests per {period.TotalSeconds}s");
        }

        public async Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ThreadSafeRateLimiter));
            }

            if (!_buckets.TryGetValue(key, out var bucket))
            {
                _logger.Warn($"No rate limit configuration for key '{key}', executing without limit");
                return await action();
            }

            return await bucket.ExecuteAsync(action, cancellationToken);
        }

        public async Task ExecuteAsync(string key, Func<Task> action, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(key, async () =>
            {
                await action();
                return true;
            }, cancellationToken);
        }

        public RateLimitStatus GetStatus(string key)
        {
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                return new RateLimitStatus
                {
                    Key = key,
                    IsConfigured = false
                };
            }

            return bucket.GetStatus(key);
        }

        public void Reset(string key)
        {
            if (_buckets.TryGetValue(key, out var bucket))
            {
                bucket.Reset();
                _logger.Debug($"Rate limiter reset for key '{key}'");
            }
        }

        public void ResetAll()
        {
            foreach (var bucket in _buckets.Values)
            {
                bucket.Reset();
            }
            _logger.Debug("All rate limiters reset");
        }

        private void CleanupExpiredEntries(object state)
        {
            try
            {
                foreach (var kvp in _buckets)
                {
                    kvp.Value.CleanupExpiredSlots();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during rate limiter cleanup: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cleanupTimer?.Dispose();
                    
                    foreach (var bucket in _buckets.Values)
                    {
                        bucket.Dispose();
                    }
                    
                    _buckets.Clear();
                }

                _disposed = true;
            }
        }
    }

    internal class RateLimitBucket : IDisposable
    {
        private readonly Logger _logger;
        private readonly SemaphoreSlim _semaphore;
        private readonly ReaderWriterLockSlim _lock;
        private readonly Queue<DateTime> _requestSlots;
        private int _maxRequests;
        private TimeSpan _period;
        private volatile bool _disposed;

        public RateLimitBucket(int maxRequests, TimeSpan period, Logger logger)
        {
            _maxRequests = maxRequests;
            _period = period;
            _logger = logger;
            _semaphore = new SemaphoreSlim(1, 1);
            _lock = new ReaderWriterLockSlim();
            _requestSlots = new Queue<DateTime>();
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RateLimitBucket));
            }

            // Wait for our turn
            await _semaphore.WaitAsync(cancellationToken);
            
            try
            {
                // Calculate delay if needed
                var delay = CalculateDelay();
                
                if (delay > TimeSpan.Zero)
                {
                    _logger.Debug($"Rate limit: waiting {delay.TotalMilliseconds:F0}ms before execution");
                    await Task.Delay(delay, cancellationToken);
                }

                // Record this request
                RecordRequest();

                // Execute the action
                return await action();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private TimeSpan CalculateDelay()
        {
            _lock.EnterReadLock();
            try
            {
                var now = DateTime.UtcNow;
                
                // Clean expired slots
                CleanExpiredSlotsUnsafe(now);
                
                // If we have room, no delay needed
                if (_requestSlots.Count < _maxRequests)
                {
                    return TimeSpan.Zero;
                }
                
                // Calculate when the oldest request will expire
                var oldestRequest = _requestSlots.Peek();
                var expirationTime = oldestRequest + _period;
                
                if (expirationTime > now)
                {
                    // Need to wait for the oldest request to expire
                    return expirationTime - now;
                }
                
                return TimeSpan.Zero;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private void RecordRequest()
        {
            _lock.EnterWriteLock();
            try
            {
                var now = DateTime.UtcNow;
                
                // Clean expired slots
                CleanExpiredSlotsUnsafe(now);
                
                // Add new request slot
                _requestSlots.Enqueue(now);
                
                // Ensure we don't exceed max capacity (defensive)
                while (_requestSlots.Count > _maxRequests)
                {
                    _requestSlots.Dequeue();
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void CleanupExpiredSlots()
        {
            _lock.EnterWriteLock();
            try
            {
                CleanExpiredSlotsUnsafe(DateTime.UtcNow);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private void CleanExpiredSlotsUnsafe(DateTime now)
        {
            var cutoff = now - _period;
            
            while (_requestSlots.Count > 0 && _requestSlots.Peek() < cutoff)
            {
                _requestSlots.Dequeue();
            }
        }

        public void UpdateConfiguration(int maxRequests, TimeSpan period)
        {
            _lock.EnterWriteLock();
            try
            {
                _maxRequests = maxRequests;
                _period = period;
                
                // Clean up based on new configuration
                CleanExpiredSlotsUnsafe(DateTime.UtcNow);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public RateLimitStatus GetStatus(string key)
        {
            _lock.EnterReadLock();
            try
            {
                var now = DateTime.UtcNow;
                CleanExpiredSlotsUnsafe(now);
                
                return new RateLimitStatus
                {
                    Key = key,
                    IsConfigured = true,
                    MaxRequests = _maxRequests,
                    Period = _period,
                    CurrentRequests = _requestSlots.Count,
                    AvailableSlots = Math.Max(0, _maxRequests - _requestSlots.Count),
                    NextSlotAvailable = _requestSlots.Count > 0 
                        ? _requestSlots.Peek() + _period 
                        : now
                };
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Reset()
        {
            _lock.EnterWriteLock();
            try
            {
                _requestSlots.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _semaphore?.Dispose();
                    _lock?.Dispose();
                }

                _disposed = true;
            }
        }
    }

    public class RateLimitStatus
    {
        public string Key { get; set; }
        public bool IsConfigured { get; set; }
        public int MaxRequests { get; set; }
        public TimeSpan Period { get; set; }
        public int CurrentRequests { get; set; }
        public int AvailableSlots { get; set; }
        public DateTime NextSlotAvailable { get; set; }
        
        public bool IsAtLimit => AvailableSlots == 0;
        public double UtilizationPercent => IsConfigured ? (CurrentRequests * 100.0 / MaxRequests) : 0;
    }

    /// <summary>
    /// Extension methods for rate limiter
    /// </summary>
    public static class RateLimiterExtensions
    {
        /// <summary>
        /// Configure standard rate limits for AI providers
        /// </summary>
        public static void ConfigureStandardProviders(this IThreadSafeRateLimiter rateLimiter)
        {
            // Local providers - higher limits
            rateLimiter.Configure("ollama", 30, TimeSpan.FromMinutes(1));
            rateLimiter.Configure("lmstudio", 30, TimeSpan.FromMinutes(1));
            
            // Cloud providers - respect API limits
            rateLimiter.Configure("openai", 20, TimeSpan.FromMinutes(1));
            rateLimiter.Configure("anthropic", 20, TimeSpan.FromMinutes(1));
            rateLimiter.Configure("gemini", 60, TimeSpan.FromMinutes(1));
            rateLimiter.Configure("mistral", 5, TimeSpan.FromSeconds(1));
            rateLimiter.Configure("groq", 30, TimeSpan.FromMinutes(1));
            rateLimiter.Configure("openrouter", 20, TimeSpan.FromMinutes(1));
            rateLimiter.Configure("perplexity", 20, TimeSpan.FromMinutes(1));
            rateLimiter.Configure("cohere", 10, TimeSpan.FromMinutes(1));
            rateLimiter.Configure("deepseek", 60, TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Execute with automatic retry on rate limit
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            this IThreadSafeRateLimiter rateLimiter,
            string key,
            Func<Task<T>> action,
            int maxRetries = 3,
            CancellationToken cancellationToken = default)
        {
            Exception lastException = null;
            
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await rateLimiter.ExecuteAsync(key, action, cancellationToken);
                }
                catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    
                    if (i < maxRetries - 1)
                    {
                        // Exponential backoff
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, i));
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }

            throw new AggregateException($"Failed after {maxRetries} retries", lastException);
        }
    }
}