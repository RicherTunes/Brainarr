using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Thread-safe, async-first rate limiter with backpressure control
    /// </summary>
    public interface IAsyncRateLimiter
    {
        Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action, CancellationToken cancellationToken = default);
        void Configure(string resource, RateLimitConfiguration config);
        RateLimitStatistics GetStatistics(string resource);
    }

    public class RateLimitConfiguration
    {
        public int MaxRequests { get; set; } = 10;
        public TimeSpan Period { get; set; } = TimeSpan.FromMinutes(1);
        public int MaxQueueSize { get; set; } = 100;
        public TimeSpan? Timeout { get; set; } = TimeSpan.FromMinutes(2);
    }

    public class RateLimitStatistics
    {
        public int CurrentRequests { get; set; }
        public int QueuedRequests { get; set; }
        public int RejectedRequests { get; set; }
        public DateTime? LastRequestTime { get; set; }
        public double AverageWaitTime { get; set; }
    }

    public class AsyncRateLimiter : IAsyncRateLimiter, IDisposable
    {
        private readonly ConcurrentDictionary<string, ResourceRateLimiter> _limiters;
        private readonly Logger _logger;
        private readonly Timer _cleanupTimer;

        public AsyncRateLimiter(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _limiters = new ConcurrentDictionary<string, ResourceRateLimiter>();
            
            // Cleanup old request records every minute
            _cleanupTimer = new Timer(
                _ => CleanupOldRecords(), 
                null, 
                TimeSpan.FromMinutes(1), 
                TimeSpan.FromMinutes(1));
        }

        public void Configure(string resource, RateLimitConfiguration config)
        {
            if (string.IsNullOrEmpty(resource))
                throw new ArgumentNullException(nameof(resource));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _limiters[resource] = new ResourceRateLimiter(config, _logger);
            _logger.Info($"Rate limiter configured for {resource}: {config.MaxRequests} requests per {config.Period.TotalSeconds}s");
        }

        public async Task<T> ExecuteAsync<T>(
            string resource, 
            Func<Task<T>> action, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resource))
                throw new ArgumentNullException(nameof(resource));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var limiter = _limiters.GetOrAdd(resource, key => 
                new ResourceRateLimiter(
                    new RateLimitConfiguration 
                    { 
                        MaxRequests = 10, 
                        Period = TimeSpan.FromMinutes(1) 
                    }, 
                    _logger));

            return await limiter.ExecuteAsync(action, cancellationToken);
        }

        public RateLimitStatistics GetStatistics(string resource)
        {
            if (_limiters.TryGetValue(resource, out var limiter))
            {
                return limiter.GetStatistics();
            }

            return new RateLimitStatistics();
        }

        private void CleanupOldRecords()
        {
            try
            {
                foreach (var limiter in _limiters.Values)
                {
                    limiter.CleanupOldRecords();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error during rate limiter cleanup");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            
            foreach (var limiter in _limiters.Values)
            {
                limiter.Dispose();
            }
        }

        private class ResourceRateLimiter : IDisposable
        {
            private readonly RateLimitConfiguration _config;
            private readonly SemaphoreSlim _semaphore;
            private readonly Queue<DateTime> _requestTimes;
            private readonly SemaphoreSlim _queueSemaphore;
            private readonly Logger _logger;
            
            private int _queuedRequests = 0;
            private int _rejectedRequests = 0;
            private readonly object _statsLock = new object();
            private readonly List<double> _waitTimes = new List<double>();

            public ResourceRateLimiter(RateLimitConfiguration config, Logger logger)
            {
                _config = config ?? throw new ArgumentNullException(nameof(config));
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
                
                _semaphore = new SemaphoreSlim(_config.MaxRequests, _config.MaxRequests);
                _queueSemaphore = new SemaphoreSlim(_config.MaxQueueSize, _config.MaxQueueSize);
                _requestTimes = new Queue<DateTime>();
            }

            public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
            {
                // Check queue size limit
                if (!await _queueSemaphore.WaitAsync(0, cancellationToken))
                {
                    Interlocked.Increment(ref _rejectedRequests);
                    throw new RateLimitExceededException($"Queue is full ({_config.MaxQueueSize} requests waiting)");
                }

                try
                {
                    Interlocked.Increment(ref _queuedRequests);
                    var waitStartTime = DateTime.UtcNow;

                    // Apply timeout if configured
                    using var cts = _config.Timeout.HasValue 
                        ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                        : null;
                    
                    if (cts != null)
                    {
                        cts.CancelAfter(_config.Timeout.Value);
                    }

                    var effectiveToken = cts?.Token ?? cancellationToken;

                    // Wait for rate limit slot
                    await WaitForSlotAsync(effectiveToken);
                    
                    var waitTime = (DateTime.UtcNow - waitStartTime).TotalMilliseconds;
                    RecordWaitTime(waitTime);

                    try
                    {
                        // Execute the action
                        var startTime = DateTime.UtcNow;
                        var result = await action();
                        
                        // Record successful request
                        lock (_statsLock)
                        {
                            _requestTimes.Enqueue(startTime);
                        }
                        
                        return result;
                    }
                    finally
                    {
                        // Schedule semaphore release after the period
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(_config.Period);
                            _semaphore.Release();
                        });
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _queuedRequests);
                    _queueSemaphore.Release();
                }
            }

            private async Task WaitForSlotAsync(CancellationToken cancellationToken)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Try to acquire semaphore
                    if (await _semaphore.WaitAsync(100, cancellationToken))
                    {
                        // Check if we need to wait based on request history
                        TimeSpan? additionalWait = null;
                        
                        lock (_statsLock)
                        {
                            CleanupOldRecordsInternal();
                            
                            if (_requestTimes.Count >= _config.MaxRequests)
                            {
                                var oldestRequest = _requestTimes.Peek();
                                var timeSinceOldest = DateTime.UtcNow - oldestRequest;
                                
                                if (timeSinceOldest < _config.Period)
                                {
                                    additionalWait = _config.Period - timeSinceOldest;
                                }
                            }
                        }
                        
                        if (additionalWait.HasValue)
                        {
                            _logger.Debug($"Rate limit reached, waiting {additionalWait.Value.TotalSeconds:F1}s");
                            await Task.Delay(additionalWait.Value, cancellationToken);
                        }
                        
                        return;
                    }
                }
                
                throw new OperationCanceledException("Rate limit wait was cancelled");
            }

            private void RecordWaitTime(double milliseconds)
            {
                lock (_statsLock)
                {
                    _waitTimes.Add(milliseconds);
                    
                    // Keep only last 100 wait times
                    if (_waitTimes.Count > 100)
                    {
                        _waitTimes.RemoveAt(0);
                    }
                }
            }

            public void CleanupOldRecords()
            {
                lock (_statsLock)
                {
                    CleanupOldRecordsInternal();
                }
            }

            private void CleanupOldRecordsInternal()
            {
                var cutoff = DateTime.UtcNow - _config.Period;
                while (_requestTimes.Count > 0 && _requestTimes.Peek() < cutoff)
                {
                    _requestTimes.Dequeue();
                }
            }

            public RateLimitStatistics GetStatistics()
            {
                lock (_statsLock)
                {
                    CleanupOldRecordsInternal();
                    
                    return new RateLimitStatistics
                    {
                        CurrentRequests = _requestTimes.Count,
                        QueuedRequests = _queuedRequests,
                        RejectedRequests = _rejectedRequests,
                        LastRequestTime = _requestTimes.Count > 0 ? 
                            _requestTimes.ToArray()[_requestTimes.Count - 1] : 
                            (DateTime?)null,
                        AverageWaitTime = _waitTimes.Count > 0 ? 
                            _waitTimes.ToArray().Average() : 
                            0
                    };
                }
            }

            public void Dispose()
            {
                _semaphore?.Dispose();
                _queueSemaphore?.Dispose();
            }
        }
    }

    public class RateLimitExceededException : Exception
    {
        public RateLimitExceededException(string message) : base(message) { }
    }

    /// <summary>
    /// Configuration helper for provider-specific rate limits
    /// </summary>
    public static class RateLimiterDefaults
    {
        public static void ConfigureProviderDefaults(IAsyncRateLimiter rateLimiter)
        {
            // Local providers - higher limits
            rateLimiter.Configure("ollama", new RateLimitConfiguration
            {
                MaxRequests = 30,
                Period = TimeSpan.FromMinutes(1),
                MaxQueueSize = 50
            });

            rateLimiter.Configure("lmstudio", new RateLimitConfiguration
            {
                MaxRequests = 30,
                Period = TimeSpan.FromMinutes(1),
                MaxQueueSize = 50
            });

            // Cloud providers - respect API limits
            rateLimiter.Configure("openai", new RateLimitConfiguration
            {
                MaxRequests = 10,
                Period = TimeSpan.FromMinutes(1),
                MaxQueueSize = 20,
                Timeout = TimeSpan.FromMinutes(2)
            });

            rateLimiter.Configure("anthropic", new RateLimitConfiguration
            {
                MaxRequests = 10,
                Period = TimeSpan.FromMinutes(1),
                MaxQueueSize = 20,
                Timeout = TimeSpan.FromMinutes(2)
            });

            rateLimiter.Configure("gemini", new RateLimitConfiguration
            {
                MaxRequests = 60,
                Period = TimeSpan.FromMinutes(1),
                MaxQueueSize = 30
            });

            rateLimiter.Configure("groq", new RateLimitConfiguration
            {
                MaxRequests = 30,
                Period = TimeSpan.FromMinutes(1),
                MaxQueueSize = 50
            });

            // MusicBrainz - strict 1 req/sec
            rateLimiter.Configure("musicbrainz", new RateLimitConfiguration
            {
                MaxRequests = 1,
                Period = TimeSpan.FromSeconds(1),
                MaxQueueSize = 10
            });
        }
    }
}