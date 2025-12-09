using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Logging;
using CommonRateLimiter = Lidarr.Plugin.Common.Services.Resilience.TokenBucketRateLimiter;
using CommonRateLimitPresets = Lidarr.Plugin.Common.Services.Resilience.RateLimitPresets;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Interface for rate limiting resource access.
    /// Provides token bucket rate limiting for API calls and other throttled resources.
    /// </summary>
    /// <remarks>
    /// This interface is maintained for backwards compatibility within Brainarr.
    /// The implementation delegates to Lidarr.Plugin.Common's TokenBucketRateLimiter.
    /// </remarks>
    public interface IRateLimiter
    {
        Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action);
        Task<T> ExecuteAsync<T>(string resource, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
        void Configure(string resource, int maxRequests, TimeSpan period);
    }

    /// <summary>
    /// Token bucket rate limiter implementation for Brainarr.
    /// Delegates to Lidarr.Plugin.Common's TokenBucketRateLimiter.
    /// </summary>
    /// <remarks>
    /// Token Bucket Algorithm:
    /// - Tokens are added at a fixed rate (refill rate = capacity / period)
    /// - Each request consumes one token
    /// - If no tokens available, request waits until tokens refill
    /// - Maximum tokens capped at capacity
    ///
    /// Example: 10 requests per minute
    /// - Capacity: 10 tokens
    /// - Refill rate: 10/60 = 0.167 tokens per second
    /// - Burst: Can handle 10 requests immediately
    /// - Sustained: ~0.167 requests per second
    ///
    /// Use cases in Brainarr:
    /// - AI provider API calls (OpenAI, Anthropic, etc.)
    /// - Local provider requests (Ollama, LM Studio)
    /// - MusicBrainz validation (strict 1 req/sec)
    /// </remarks>
    public class RateLimiter : IRateLimiter
    {
        private readonly CommonRateLimiter _innerLimiter;

        /// <summary>
        /// Initializes a new instance of the RateLimiter.
        /// </summary>
        /// <param name="logger">NLog Logger for diagnostic information</param>
        public RateLimiter(Logger logger)
        {
            // Adapt NLog Logger to ILogger for the common library
            var ilogger = logger != null ? NLogAdapterFactory.CreateILogger(logger) : null;
            _innerLimiter = new CommonRateLimiter(ilogger);
        }

        /// <summary>
        /// Configures rate limiting for a specific resource.
        /// </summary>
        /// <param name="resource">Resource identifier (e.g., "openai", "musicbrainz")</param>
        /// <param name="maxRequests">Maximum requests allowed in the period</param>
        /// <param name="period">Time period for the rate limit</param>
        public void Configure(string resource, int maxRequests, TimeSpan period)
        {
            _innerLimiter.Configure(resource, maxRequests, period);
        }

        /// <summary>
        /// Executes an operation with rate limiting, waiting if necessary.
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="resource">Resource identifier for rate limiting</param>
        /// <param name="action">The async operation to execute</param>
        /// <returns>Result of the operation</returns>
        public async Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action)
        {
            return await _innerLimiter.ExecuteAsync(resource, action).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes an operation with rate limiting and cancellation support.
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="resource">Resource identifier for rate limiting</param>
        /// <param name="action">The async operation to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation</returns>
        public async Task<T> ExecuteAsync<T>(string resource, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            return await _innerLimiter.ExecuteAsync(resource, action, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the current available tokens for a resource.
        /// </summary>
        /// <param name="resource">Resource identifier</param>
        /// <returns>Number of available tokens, or null if resource not configured</returns>
        public int? GetAvailableTokens(string resource)
        {
            return _innerLimiter.GetAvailableTokens(resource);
        }

        /// <summary>
        /// Resets the rate limiter for a specific resource or all resources.
        /// </summary>
        /// <param name="resource">Resource to reset, or null to reset all</param>
        public void Reset(string resource = null)
        {
            _innerLimiter.Reset(resource);
        }
    }

    /// <summary>
    /// Provider-specific rate limiter configurations for Brainarr.
    /// </summary>
    public static class RateLimiterConfiguration
    {
        private static readonly HashSet<IRateLimiter> _configuredLimiters = new HashSet<IRateLimiter>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Configures default rate limits for all Brainarr providers.
        /// Only configures each rate limiter instance once to prevent log spam.
        /// </summary>
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
