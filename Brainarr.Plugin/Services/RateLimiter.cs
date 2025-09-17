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
            private readonly object _lock = new object();
            private readonly Logger _logger;
            private readonly double _maxTokens;
            private readonly double _refillRatePerSecond;
            private double _availableTokens;
            private DateTime _lastRefill;

            public ResourceRateLimiter(int maxRequests, TimeSpan period, Logger logger)
            {
                _logger = logger;
                _maxTokens = Math.Max(1, maxRequests);
                var seconds = Math.Max(0.001, period.TotalSeconds);
                _refillRatePerSecond = _maxTokens / seconds;
                _availableTokens = _maxTokens;
                _lastRefill = DateTime.UtcNow;
            }

            private TimeSpan ReserveSlot()
            {
                var now = DateTime.UtcNow;

                RefillTokens(now);

                if (_availableTokens >= 1d)
                {
                    _availableTokens -= 1d;
                    return TimeSpan.Zero;
                }

                var tokensNeeded = 1d - _availableTokens;
                var secondsToWait = tokensNeeded / _refillRatePerSecond;
                if (secondsToWait < 0d)
                {
                    secondsToWait = 0d;
                }

                _availableTokens -= 1d;
                return TimeSpan.FromSeconds(secondsToWait);
            }

            private void RefillTokens(DateTime now)
            {
                if (now <= _lastRefill)
                {
                    return;
                }

                var elapsedSeconds = (now - _lastRefill).TotalSeconds;
                if (elapsedSeconds <= 0d)
                {
                    return;
                }

                _availableTokens = Math.Min(_maxTokens, _availableTokens + elapsedSeconds * _refillRatePerSecond);
                _lastRefill = now;
            }

            public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
            {
                TimeSpan wait;
                lock (_lock)
                {
                    wait = ReserveSlot();
                }

                if (wait > TimeSpan.Zero)
                {
                    _logger.Debug($"Rate limit waiting {wait.TotalMilliseconds:F0}ms");
                    await Task.Delay(wait).ConfigureAwait(false);
                }

                return await action().ConfigureAwait(false);
            }

            public async Task<T> ExecuteAsync<T>(Func<System.Threading.CancellationToken, Task<T>> action, System.Threading.CancellationToken cancellationToken)
            {
                TimeSpan wait;
                lock (_lock)
                {
                    wait = ReserveSlot();
                }

                if (wait > TimeSpan.Zero)
                {
                    _logger.Debug($"Rate limit waiting {wait.TotalMilliseconds:F0}ms");
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return await action(cancellationToken).ConfigureAwait(false);
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
