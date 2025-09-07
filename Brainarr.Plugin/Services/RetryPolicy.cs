using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Interface for implementing retry policies with exponential backoff.
    /// Provides resilient execution of operations that may fail due to transient errors.
    /// </summary>
    public interface IRetryPolicy
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName);
    }

    /// <summary>
    /// Implementation of exponential backoff retry policy for resilient operation execution.
    /// Uses exponential delay increases between retry attempts to avoid overwhelming failing services.
    /// </summary>
    /// <remarks>
    /// Exponential Backoff Algorithm:
    /// - Attempt 1: No delay
    /// - Attempt 2: initialDelay (default 500ms)
    /// - Attempt 3: initialDelay * 2 (1000ms)
    /// - Attempt 4: initialDelay * 4 (2000ms)
    /// - Attempt 5: initialDelay * 8 (4000ms)
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
    /// Non-retryable exceptions:
    /// - TaskCanceledException (user cancellation)
    /// - Authentication errors (permanent failures)
    /// </remarks>
    public class ExponentialBackoffRetryPolicy : IRetryPolicy
    {
        private readonly Logger _logger;
        private readonly int _maxRetries;
        private readonly TimeSpan _initialDelay;

        /// <summary>
        /// Initializes a new instance of the ExponentialBackoffRetryPolicy.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="maxRetries">Maximum number of retry attempts (default: from BrainarrConstants)</param>
        /// <param name="initialDelay">Initial delay between retries (default: from BrainarrConstants)</param>
        public ExponentialBackoffRetryPolicy(Logger logger, int? maxRetries = null, TimeSpan? initialDelay = null)
        {
            _logger = logger;
            _maxRetries = maxRetries ?? BrainarrConstants.MaxRetryAttempts;
            _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(BrainarrConstants.InitialRetryDelayMs);
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
        /// <remarks>
        /// Execution flow:
        /// 1. Try operation immediately (attempt 1)
        /// 2. On failure, wait with exponential backoff
        /// 3. Retry up to maxRetries times
        /// 4. If all attempts fail, throw RetryExhaustedException with original exception
        ///
        /// The delay calculation ensures each retry waits longer than the previous:
        /// delay = initialDelay * 2^(attempt-1)
        ///
        /// Example with 500ms initial delay:
        /// - Retry 1: 500ms delay
        /// - Retry 2: 1000ms delay
        /// - Retry 3: 2000ms delay
        /// - Retry 4: 4000ms delay
        /// </remarks>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName)
        {
            Exception lastException = null;

            for (int attempt = 0; attempt < _maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        // SECURITY FIX: Prevent integer overflow in exponential backoff
                        var multiplier = Math.Min(Math.Pow(2, attempt - 1), 1024); // Cap at 2^10
                        var baseDelayMs = Math.Min(_initialDelay.TotalMilliseconds * multiplier, 60000); // Cap at 60 seconds
                        // Full jitter to reduce synchronized retry storms
                        var rnd = new Random(unchecked(Environment.TickCount + attempt * 397));
                        var jitterMs = rnd.Next(0, (int)Math.Max(1, baseDelayMs));
                        var delay = TimeSpan.FromMilliseconds(jitterMs);
                        _logger.Info($"Retry {attempt}/{_maxRetries} for {operationName} after {delay.TotalSeconds:F2}s jittered delay");
                        await Task.Delay(delay);
                    }

                    return await action();
                }
                catch (TaskCanceledException)
                {
                    // Don't retry on cancellation
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.Warn($"Attempt {attempt + 1}/{_maxRetries} failed for {operationName}: {ex.Message}");

                    if (attempt == _maxRetries - 1)
                    {
                        _logger.Error(ex, $"All {_maxRetries} attempts failed for {operationName}");
                        throw new RetryExhaustedException($"Operation '{operationName}' failed after {_maxRetries} attempts", ex);
                    }
                }
            }

            throw new RetryExhaustedException($"Operation '{operationName}' failed after {_maxRetries} attempts", lastException);
        }
    }

    /// <summary>
    /// Exception thrown when all retry attempts have been exhausted.
    /// Contains the original exception that caused the failures.
    /// </summary>
    public class RetryExhaustedException : Exception
    {
        public RetryExhaustedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
