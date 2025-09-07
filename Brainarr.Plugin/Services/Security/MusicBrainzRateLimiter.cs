using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Brainarr.Plugin.Services.Security
{
    /// <summary>
    /// Rate limiter specifically for MusicBrainz API calls
    /// MusicBrainz requires: 1 request per second average, with User-Agent header
    /// </summary>
    public class MusicBrainzRateLimiter
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentQueue<DateTime> _requestTimestamps;
        private readonly Timer _cleanupTimer;
        private readonly object _lockObject = new object();

        // MusicBrainz rate limits
        private const int MaxRequestsPerSecond = 1;
        private const int MaxRequestsPerMinute = 50; // Being conservative

        // Tracking
        private long _totalRequests = 0;
        private long _throttledRequests = 0;
        private DateTime _lastRequestTime = DateTime.MinValue;

        public MusicBrainzRateLimiter()
        {
            _semaphore = new SemaphoreSlim(1, 1); // Only 1 concurrent request
            _requestTimestamps = new ConcurrentQueue<DateTime>();

            // Cleanup old timestamps every minute
            _cleanupTimer = new Timer(CleanupOldTimestamps, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Execute a MusicBrainz API request with rate limiting
        /// </summary>
        public Task<T> ExecuteWithRateLimitAsync<T>(Func<Task<T>> apiCall, string? endpoint = null)
            => ExecuteWithRateLimitAsync<T>(_ => apiCall(), endpoint, CancellationToken.None);

        /// <summary>
        /// Execute a MusicBrainz API request with rate limiting and cancellation support
        /// </summary>
        public async Task<T> ExecuteWithRateLimitAsync<T>(Func<CancellationToken, Task<T>> apiCall, string? endpoint, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Compute delay to honor 1 rps and per-minute caps
                TimeSpan delayToApply = TimeSpan.Zero;
                lock (_lockObject)
                {
                    var now = DateTime.UtcNow;

                    if (_lastRequestTime != DateTime.MinValue)
                    {
                        var timeSinceLastRequest = now - _lastRequestTime;
                        if (timeSinceLastRequest < TimeSpan.FromSeconds(1))
                        {
                            delayToApply = TimeSpan.FromSeconds(1) - timeSinceLastRequest;
                        }
                    }

                    var oneMinuteAgo = now.AddMinutes(-1);
                    var recentRequests = 0;
                    foreach (var timestamp in _requestTimestamps)
                    {
                        if (timestamp > oneMinuteAgo)
                        {
                            recentRequests++;
                        }
                    }

                    if (recentRequests >= MaxRequestsPerMinute)
                    {
                        var oldestRecentRequest = _requestTimestamps.Where(t => t > oneMinuteAgo).FirstOrDefault();
                        if (oldestRecentRequest != default)
                        {
                            var waitTime = oldestRecentRequest.AddMinutes(1) - now;
                            if (waitTime > TimeSpan.Zero && waitTime > delayToApply)
                            {
                                delayToApply = waitTime;
                            }
                        }
                    }

                    // Reserve the next start time for this request
                    _lastRequestTime = now + delayToApply;
                }

                if (delayToApply > TimeSpan.Zero)
                {
                    _logger.Debug($"Rate limiting: waiting {delayToApply.TotalMilliseconds:F0}ms before next MusicBrainz request");
                    await Task.Delay(delayToApply, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    Interlocked.Increment(ref _totalRequests);
                    _logger.Debug($"Executing MusicBrainz API call to {endpoint ?? "unknown endpoint"}");

                    var result = await apiCall(cancellationToken).ConfigureAwait(false);

                    RecordRequest();
                    return result;
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("429") || ex.Message.Contains("Too Many Requests"))
                {
                    _logger.Warn("MusicBrainz rate limit exceeded, backing off");
                    Interlocked.Increment(ref _throttledRequests);

                    // Back off exponentially with cancellation support (capped)
                    var throttles = Math.Min(8, Interlocked.Read(ref _throttledRequests));
                    var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, throttles)));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                    // Retry once
                    return await apiCall(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Wait for rate limit window
        /// </summary>
        // Removed: WaitForRateLimit now inlined to ensure the semaphore covers action execution

        /// <summary>
        /// Record a request timestamp
        /// </summary>
        private void RecordRequest()
        {
            _requestTimestamps.Enqueue(DateTime.UtcNow);

            // Keep only last 2 minutes of timestamps
            var cutoff = DateTime.UtcNow.AddMinutes(-2);
            while (_requestTimestamps.TryPeek(out var oldest) && oldest < cutoff)
            {
                _requestTimestamps.TryDequeue(out _);
            }
        }

        /// <summary>
        /// Cleanup old timestamps periodically
        /// </summary>
        private void CleanupOldTimestamps(object? state)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-2);
            var removed = 0;

            while (_requestTimestamps.TryPeek(out var oldest) && oldest < cutoff)
            {
                if (_requestTimestamps.TryDequeue(out _))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                _logger.Debug($"Cleaned up {removed} old MusicBrainz request timestamps");
            }
        }

        /// <summary>
        /// Create a properly configured HttpClient for MusicBrainz
        /// </summary>
        public static HttpClient CreateMusicBrainzClient()
        {
            var handler = CertificateValidator.CreateSecureHandler(enableCertificatePinning: false);
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // MusicBrainz requires a User-Agent
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgentHelper.Build());

            // Add required headers
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            return client;
        }

        /// <summary>
        /// Get rate limiting statistics
        /// </summary>
        public RateLimitStats GetStats()
        {
            var now = DateTime.UtcNow;
            var oneMinuteAgo = now.AddMinutes(-1);
            var recentRequests = _requestTimestamps.Count(t => t > oneMinuteAgo);

            return new RateLimitStats
            {
                TotalRequests = _totalRequests,
                ThrottledRequests = _throttledRequests,
                RequestsLastMinute = recentRequests,
                RemainingRequestsThisMinute = Math.Max(0, MaxRequestsPerMinute - recentRequests),
                NextAvailableRequestTime = _lastRequestTime.AddSeconds(1)
            };
        }

        /// <summary>
        /// Reset rate limiter (for testing)
        /// </summary>
        public void Reset()
        {
            lock (_lockObject)
            {
                while (_requestTimestamps.TryDequeue(out _)) { }
                _lastRequestTime = DateTime.MinValue;
                _totalRequests = 0;
                _throttledRequests = 0;
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _semaphore?.Dispose();
        }

        /// <summary>
        /// Rate limit statistics
        /// </summary>
        public class RateLimitStats
        {
            public long TotalRequests { get; set; }
            public long ThrottledRequests { get; set; }
            public int RequestsLastMinute { get; set; }
            public int RemainingRequestsThisMinute { get; set; }
            public DateTime NextAvailableRequestTime { get; set; }
        }
    }

    /// <summary>
    /// Extension methods for easy integration
    /// </summary>
    public static class MusicBrainzRateLimiterExtensions
    {
        private static readonly MusicBrainzRateLimiter _globalRateLimiter = new MusicBrainzRateLimiter();

        /// <summary>
        /// Execute a MusicBrainz API call with global rate limiting
        /// </summary>
        public static Task<T> ExecuteMusicBrainzRequestAsync<T>(this HttpClient client, Func<Task<T>> apiCall, string? endpoint = null)
        {
            return _globalRateLimiter.ExecuteWithRateLimitAsync(apiCall, endpoint);
        }

        /// <summary>
        /// Execute a MusicBrainz API call with global rate limiting and cancellation
        /// </summary>
        public static Task<T> ExecuteMusicBrainzRequestAsync<T>(this HttpClient client, Func<CancellationToken, Task<T>> apiCall, string? endpoint, CancellationToken cancellationToken)
        {
            return _globalRateLimiter.ExecuteWithRateLimitAsync(apiCall, endpoint, cancellationToken);
        }

        /// <summary>
        /// Get global MusicBrainz rate limiter stats
        /// </summary>
        public static MusicBrainzRateLimiter.RateLimitStats GetMusicBrainzRateLimitStats()
        {
            return _globalRateLimiter.GetStats();
        }
    }
}
