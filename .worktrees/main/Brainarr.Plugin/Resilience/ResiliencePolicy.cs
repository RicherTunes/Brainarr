using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;

namespace NzbDrone.Core.ImportLists.Brainarr.Resilience
{
    /// <summary>
    /// Lightweight resilience helper that applies retry with full jitter backoff.
    /// Intended for short provider calls where a couple of retries improve stability.
    /// </summary>
    public static class ResiliencePolicy
    {
        public static Task<T> WithResilienceAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            string origin,
            Logger logger,
            CancellationToken cancellationToken,
            int timeoutSeconds = 30,
            int maxRetries = 3)
        {
            // For now, leverage the jittered retry helper. Circuit breaker can be folded in later.
            var initialDelay = TimeSpan.FromMilliseconds(Configuration.BrainarrConstants.InitialRetryDelayMs);
            return RunWithRetriesAsync(operation, logger, $"{origin}.http", maxRetries, initialDelay, cancellationToken);
        }

        /// <summary>
        /// Specialized resilience for HTTP calls where transient responses should be retried.
        /// Retries on exceptions and on transient status codes (408/429/5xx) by default.
        /// </summary>
        public static async Task<HttpResponse> WithHttpResilienceAsync(
            Func<CancellationToken, Task<HttpResponse>> send,
            string origin,
            Logger logger,
            CancellationToken cancellationToken,
            int maxRetries = 3,
            Func<HttpResponse, bool>? shouldRetry = null)
        {
            if (send == null) throw new ArgumentNullException(nameof(send));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            shouldRetry ??= static (resp) =>
            {
                var code = resp.StatusCode;
                var numeric = (int)code;
                if (code == System.Net.HttpStatusCode.TooManyRequests || code == System.Net.HttpStatusCode.RequestTimeout)
                    return true;
                if (numeric >= 500 && numeric <= 504) return true; // 5xx
                return false;
            };

            var attempt = 0;
            var delayMs = Configuration.BrainarrConstants.InitialRetryDelayMs;
            var maxDelayMs = Configuration.BrainarrConstants.MaxRetryDelayMs;
            Exception? lastError = null;

            while (attempt < Math.Max(1, maxRetries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;
                try
                {
                    var resp = await send(cancellationToken).ConfigureAwait(false);
                    // Record throttles if we see 429
                    if (resp != null && resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        try
                        {
                            var (prov, model) = ParseOrigin(origin);
                            NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.IncrementCounter(ProviderMetricsHelper.BuildThrottleMetric(prov, model));
                            // Adaptive limiter: temporarily cap concurrency for this model, prefer Retry-After
                            var cap = prov is "ollama" or "lmstudio" ? 8 : 2;
                            var ttl = GetRetryAfter(resp) ?? TimeSpan.FromSeconds(60);
                            if (ttl < TimeSpan.FromSeconds(5)) ttl = TimeSpan.FromSeconds(5);
                            if (ttl > TimeSpan.FromMinutes(5)) ttl = TimeSpan.FromMinutes(5);
                            NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.LimiterRegistry.RegisterThrottle($"{prov}:{model}", ttl, cap);
                        }
                        catch { }
                    }
                    if (!shouldRetry(resp) || attempt >= maxRetries)
                    {
                        return resp;
                    }

                    logger.Warn($"{origin}.http transient status {resp.StatusCode} on attempt {attempt}/{maxRetries}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    logger.Warn(ex, $"{origin}.http exception on attempt {attempt}/{maxRetries}");
                }

                if (attempt < maxRetries)
                {
                    var jitter = Random.Shared.Next(50, 200);
                    var sleep = Math.Min(maxDelayMs, delayMs) + jitter;
                    await Task.Delay(sleep, cancellationToken).ConfigureAwait(false);
                    delayMs = Math.Min(maxDelayMs, delayMs * 2);
                }
            }

            if (lastError != null)
            {
                logger.Error(lastError, $"{origin}.http failed after {maxRetries} attempts");
            }
            return default!;
        }

        private static TimeSpan? GetRetryAfter(HttpResponse resp)
        {
            try
            {
                if (resp == null || resp.Headers == null) return null;
                foreach (var h in resp.Headers)
                {
                    if (!h.Key.Equals("Retry-After", StringComparison.OrdinalIgnoreCase)) continue;
                    var v = (h.Value ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(v)) continue;
                    if (int.TryParse(v, out var seconds))
                    {
                        return TimeSpan.FromSeconds(Math.Max(0, seconds));
                    }
                    if (DateTimeOffset.TryParse(v, out var when))
                    {
                        var delta = when - DateTimeOffset.UtcNow;
                        if (delta > TimeSpan.Zero) return delta;
                    }
                }
            }
            catch { }
            return null;
        }

        private static (string provider, string model) ParseOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return ("unknown", "unknown");
            var o = origin.Trim().ToLowerInvariant();
            var idx = o.IndexOf(':');
            if (idx <= 0) return (o, "default");
            var p = o.Substring(0, idx);
            var m = o.Substring(idx + 1);
            return (string.IsNullOrWhiteSpace(p) ? "unknown" : p, string.IsNullOrWhiteSpace(m) ? "default" : m);
        }
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
