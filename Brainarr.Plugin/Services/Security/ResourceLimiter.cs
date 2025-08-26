using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    /// <summary>
    /// SECURITY ENHANCEMENT: Prevents resource exhaustion attacks through strict limits
    /// </summary>
    public class ResourceLimiter
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, ResourceUsage> _usage;
        private readonly Timer _cleanupTimer;
        
        // Resource limits
        private const int MaxConcurrentRequests = 50;
        private const int MaxRequestsPerMinute = 100;
        private const int MaxMemoryMB = 500;
        private const int MaxCpuPercent = 80;
        private const int MaxResponseSizeMB = 10;
        
        // DoS protection
        private readonly SemaphoreSlim _concurrencyLimiter;
        private long _totalRequests;
        private DateTime _windowStart;

        public ResourceLimiter(Logger logger)
        {
            _logger = logger;
            _usage = new ConcurrentDictionary<string, ResourceUsage>();
            _concurrencyLimiter = new SemaphoreSlim(MaxConcurrentRequests);
            _windowStart = DateTime.UtcNow;
            
            // Cleanup old entries every minute
            _cleanupTimer = new Timer(_ => Cleanup(), null, 
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Checks if a request should be allowed based on resource limits
        /// </summary>
        public async Task<bool> TryAcquireAsync(string clientId, int estimatedMemoryKB = 1024)
        {
            // Check global rate limit
            if (!CheckGlobalRateLimit())
            {
                _logger.Warn($"Global rate limit exceeded. Rejecting request from {clientId}");
                return false;
            }

            // Check per-client limits
            var usage = _usage.GetOrAdd(clientId, new ResourceUsage());
            
            lock (usage)
            {
                usage.RequestCount++;
                usage.LastActivity = DateTime.UtcNow;
                
                // Check client-specific rate limit
                if (usage.RequestCount > 20 && 
                    (DateTime.UtcNow - usage.FirstRequest).TotalMinutes < 1)
                {
                    _logger.Warn($"Client {clientId} exceeded rate limit: {usage.RequestCount} requests");
                    return false;
                }
                
                // Check memory usage
                usage.EstimatedMemoryKB += estimatedMemoryKB;
                if (usage.EstimatedMemoryKB > MaxMemoryMB * 1024)
                {
                    _logger.Warn($"Client {clientId} exceeded memory limit: {usage.EstimatedMemoryKB}KB");
                    return false;
                }
            }

            // Try to acquire concurrency slot
            var acquired = await _concurrencyLimiter.WaitAsync(TimeSpan.FromSeconds(5));
            if (!acquired)
            {
                _logger.Warn($"Concurrency limit reached. Rejecting request from {clientId}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Releases resources after request completion
        /// </summary>
        public void Release(string clientId, int actualMemoryKB = 0)
        {
            try
            {
                _concurrencyLimiter.Release();
                
                if (_usage.TryGetValue(clientId, out var usage))
                {
                    lock (usage)
                    {
                        if (actualMemoryKB > 0)
                        {
                            usage.EstimatedMemoryKB = Math.Max(0, 
                                usage.EstimatedMemoryKB - actualMemoryKB);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error releasing resources");
            }
        }

        /// <summary>
        /// Validates response size to prevent memory exhaustion
        /// </summary>
        public bool ValidateResponseSize(long sizeBytes)
        {
            var sizeMB = sizeBytes / (1024.0 * 1024.0);
            if (sizeMB > MaxResponseSizeMB)
            {
                _logger.Error($"Response size {sizeMB:F2}MB exceeds limit of {MaxResponseSizeMB}MB");
                return false;
            }
            return true;
        }

        private bool CheckGlobalRateLimit()
        {
            var now = DateTime.UtcNow;
            
            // Reset window if needed
            if ((now - _windowStart).TotalMinutes >= 1)
            {
                Interlocked.Exchange(ref _totalRequests, 0);
                _windowStart = now;
            }
            
            var currentRequests = Interlocked.Increment(ref _totalRequests);
            return currentRequests <= MaxRequestsPerMinute;
        }

        private void Cleanup()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-5);
                var toRemove = _usage
                    .Where(kvp => kvp.Value.LastActivity < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in toRemove)
                {
                    _usage.TryRemove(key, out _);
                }

                if (toRemove.Count > 0)
                {
                    _logger.Debug($"Cleaned up {toRemove.Count} inactive client entries");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during resource cleanup");
            }
        }

        private class ResourceUsage
        {
            public int RequestCount { get; set; }
            public DateTime FirstRequest { get; } = DateTime.UtcNow;
            public DateTime LastActivity { get; set; } = DateTime.UtcNow;
            public int EstimatedMemoryKB { get; set; }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _concurrencyLimiter?.Dispose();
        }
    }
}