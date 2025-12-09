using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Logging;
using Lidarr.Plugin.Common.Utilities;
using CommonRetryPolicy = Lidarr.Plugin.Common.Services.Resilience.ExponentialBackoffRetryPolicy;
using CommonRetryPolicyOptions = Lidarr.Plugin.Common.Services.Resilience.RetryPolicyOptions;
using CommonRetryExhaustedException = Lidarr.Plugin.Common.Services.Resilience.RetryExhaustedException;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Interface for implementing retry policies with exponential backoff.
    /// Provides resilient execution of operations that may fail due to transient errors.
    /// </summary>
    /// <remarks>
    /// This interface is maintained for backwards compatibility within Brainarr.
    /// The implementation delegates to Lidarr.Plugin.Common's retry policy.
    /// </remarks>
    public interface IRetryPolicy
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName);
    }

    /// <summary>
    /// Implementation of exponential backoff retry policy for resilient operation execution.
    /// Delegates to Lidarr.Plugin.Common's ExponentialBackoffRetryPolicy.
    /// </summary>
    /// <remarks>
    /// Exponential Backoff Algorithm:
    /// - Attempt 1: No delay
    /// - Attempt 2: initialDelay (default 1000ms)
    /// - Attempt 3: initialDelay * 2 (2000ms)
    /// - Attempt 4: initialDelay * 4 (4000ms)
    /// - etc.
    ///
    /// Benefits:
    /// - Prevents thundering herd problems when multiple instances retry simultaneously
    /// - Gives failing services time to recover
    /// - Balances quick recovery with system stability
    ///
    /// Use cases in Brainarr:
    /// - AI provider API calls (network timeouts, rate limits)
    /// - MusicBrainz validation requests
    /// - Local provider health checks
    ///
    /// Retryable exceptions (via RetryUtilities from lidarr.plugin.common):
    /// - HttpRequestException, TimeoutException, SocketException, IOException
    /// - HTTP status codes: 408, 429, 500, 502, 503, 504
    /// - Error messages containing: timeout, connection, network, rate limit
    ///
    /// Non-retryable exceptions (fail fast):
    /// - TaskCanceledException (user cancellation)
    /// - Authentication errors (401, 403)
    /// - Bad request errors (400)
    /// - Not found errors (404)
    /// - Other permanent failures
    /// </remarks>
    public class ExponentialBackoffRetryPolicy : IRetryPolicy
    {
        private readonly CommonRetryPolicy _innerPolicy;

        /// <summary>
        /// Initializes a new instance of the ExponentialBackoffRetryPolicy.
        /// </summary>
        /// <param name="logger">NLog Logger for diagnostic information</param>
        /// <param name="maxRetries">Maximum number of retry attempts (default: from BrainarrConstants)</param>
        /// <param name="initialDelay">Initial delay between retries (default: from BrainarrConstants)</param>
        public ExponentialBackoffRetryPolicy(Logger logger, int? maxRetries = null, TimeSpan? initialDelay = null)
        {
            var options = new CommonRetryPolicyOptions
            {
                MaxRetries = maxRetries ?? BrainarrConstants.MaxRetryAttempts,
                InitialDelay = initialDelay ?? TimeSpan.FromMilliseconds(BrainarrConstants.InitialRetryDelayMs),
                MaxDelay = TimeSpan.FromMilliseconds(BrainarrConstants.MaxRetryDelayMs),
                UseJitter = true,
                // Use RetryUtilities from common library to determine retryable exceptions
                ShouldRetry = RetryUtilities.IsRetryableException
            };

            // Adapt NLog Logger to ILogger for the common library
            var ilogger = logger != null ? NLogAdapterFactory.CreateILogger(logger) : null;
            _innerPolicy = new CommonRetryPolicy(ilogger, options);
        }

        /// <summary>
        /// Executes an operation with exponential backoff retry policy.
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="action">The async operation to execute</param>
        /// <param name="operationName">Human-readable name for logging</param>
        /// <returns>Result of the operation if successful</returns>
        /// <exception cref="RetryExhaustedException">Thrown when all retry attempts are exhausted</exception>
        /// <exception cref="TaskCanceledException">Thrown when operation is cancelled (not retried)</exception>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName)
        {
            try
            {
                return await _innerPolicy.ExecuteAsync(action, operationName);
            }
            catch (CommonRetryExhaustedException ex)
            {
                // Re-throw as Brainarr's RetryExhaustedException for backwards compatibility
                throw new RetryExhaustedException(ex.Message, ex.InnerException);
            }
        }
    }

    /// <summary>
    /// Exception thrown when all retry attempts have been exhausted.
    /// Contains the original exception that caused the failures.
    /// </summary>
    /// <remarks>
    /// This exception type is maintained for backwards compatibility within Brainarr.
    /// It wraps Lidarr.Plugin.Common's RetryExhaustedException.
    /// </remarks>
    public class RetryExhaustedException : Exception
    {
        public RetryExhaustedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Factory for creating Brainarr-specific retry policies.
    /// </summary>
    public static class BrainarrRetryPolicyFactory
    {
        /// <summary>
        /// Creates a default retry policy with Brainarr's standard settings.
        /// </summary>
        public static IRetryPolicy CreateDefault(Logger logger = null)
        {
            return new ExponentialBackoffRetryPolicy(logger);
        }

        /// <summary>
        /// Creates an aggressive retry policy for critical operations.
        /// </summary>
        public static IRetryPolicy CreateAggressive(Logger logger = null)
        {
            return new ExponentialBackoffRetryPolicy(
                logger,
                maxRetries: 5,
                initialDelay: TimeSpan.FromMilliseconds(500));
        }

        /// <summary>
        /// Creates a conservative retry policy to minimize provider load.
        /// </summary>
        public static IRetryPolicy CreateConservative(Logger logger = null)
        {
            return new ExponentialBackoffRetryPolicy(
                logger,
                maxRetries: 2,
                initialDelay: TimeSpan.FromSeconds(2));
        }
    }
}
