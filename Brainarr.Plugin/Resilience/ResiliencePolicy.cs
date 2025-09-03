using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Resilience
{
    /// <summary>
    /// Lightweight resilience helper that applies retry with full jitter backoff.
    /// Intended for short provider calls where a couple of retries improve stability.
    /// </summary>
    public static class ResiliencePolicy
    {
        public static async Task<T> RunWithRetriesAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Logger logger,
            string operationName,
            int maxAttempts,
            TimeSpan initialDelay,
            CancellationToken cancellationToken)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

            var attempt = 0;
            var delay = initialDelay;
            var rng = new Random();
            Exception lastError = null;

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                try
                {
                    var result = await operation(cancellationToken).ConfigureAwait(false);
                    if (attempt > 1)
                    {
                        logger.Debug($"{operationName} succeeded on retry #{attempt}");
                    }
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    logger.Warn(ex, $"{operationName} failed on attempt {attempt}/{maxAttempts}");
                }

                if (attempt < maxAttempts)
                {
                    var sleepMs = (int)(delay.TotalMilliseconds * rng.NextDouble());
                    await Task.Delay(sleepMs, cancellationToken).ConfigureAwait(false);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2000));
                }
            }

            if (lastError != null)
            {
                logger.Error(lastError, $"{operationName} failed after {maxAttempts} attempts");
            }
            return default;
        }
    }
}
