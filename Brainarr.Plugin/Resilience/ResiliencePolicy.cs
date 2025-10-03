using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Performance;
using Lidarr.Plugin.Common.Utilities;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Core.ImportLists.Brainarr.Services; // for InfoWithCorrelation extensions

namespace NzbDrone.Core.ImportLists.Brainarr.Resilience
{
    /// <summary>
    /// Lightweight resilience helper that applies retry with full jitter backoff.
    /// Intended for short provider calls where a couple of retries improve stability.
    /// </summary>
    public static class ResiliencePolicy
    {
        private static IUniversalAdaptiveRateLimiter? _adaptiveLimiter;

        public static void ConfigureAdaptiveLimiter(IUniversalAdaptiveRateLimiter limiter)
        {
            _adaptiveLimiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        }

        public static Task<T> WithResilienceAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            string origin,
            Logger logger,
            CancellationToken cancellationToken,
            int timeoutSeconds = 30,
            int maxRetries = 3)
        {
            var initialDelay = TimeSpan.FromMilliseconds(Configuration.BrainarrConstants.InitialRetryDelayMs);
            return RunWithRetriesAsync(operation, logger, $"{origin}.http", maxRetries, initialDelay, cancellationToken);
        }

        /// <summary>
        /// Resilient executor for HTTP requests built on Lidarr.Plugin.Common's GenericResilienceExecutor.
        /// </summary>
        public static async Task<HttpResponse> WithHttpResilienceAsync(
            HttpRequest templateRequest,
            Func<HttpRequest, CancellationToken, Task<HttpResponse>> send,
            string origin,
            Logger logger,
            CancellationToken cancellationToken,
            int maxRetries = 3,
            Func<HttpResponse, bool>? shouldRetry = null,
            IUniversalAdaptiveRateLimiter? limiter = null,
            TimeSpan? retryBudget = null,
            int maxConcurrencyPerHost = 8,
            TimeSpan? perRequestTimeout = null)
        {
            if (templateRequest == null) throw new ArgumentNullException(nameof(templateRequest));
            if (send == null) throw new ArgumentNullException(nameof(send));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            shouldRetry ??= DefaultShouldRetry;
            var effectiveLimiter = limiter ?? _adaptiveLimiter;

            var (serviceFromOrigin, _) = ParseOrigin(origin);
            var serviceName = !string.IsNullOrWhiteSpace(serviceFromOrigin) && !string.Equals(serviceFromOrigin, "unknown", StringComparison.OrdinalIgnoreCase)
                ? serviceFromOrigin
                : templateRequest.Url?.Host ?? "unknown";
            var endpoint = string.IsNullOrWhiteSpace(templateRequest.Url?.Path) ? "/" : templateRequest.Url.Path;

            if (effectiveLimiter != null)
            {
                await effectiveLimiter.WaitIfNeededAsync(serviceName, endpoint, cancellationToken).ConfigureAwait(false);
            }

            var sw = Stopwatch.StartNew();
            var response = await GenericResilienceExecutor.ExecuteWithResilienceAsync(
                templateRequest,
                (request, token) => send(request, token),
                CloneHttpRequestAsync,
                request => request.Url?.Host,
                resp => EvaluateStatusCode(resp, shouldRetry),
                GetRetryAfter,
                maxRetries,
                retryBudget ?? TimeSpan.FromSeconds(12),
                maxConcurrencyPerHost,
                perRequestTimeout ?? GetTimeoutOrDefault(templateRequest),
                cancellationToken).ConfigureAwait(false);
            sw.Stop();

            if (response != null && response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                TryRecordThrottle(origin, response);
            }

            if (effectiveLimiter != null && response != null)
            {
                using var responseMessage = ToHttpResponseMessage(response);
                effectiveLimiter.RecordResponse(serviceName, endpoint, responseMessage);
            }

            try
            {
                // Structured provider-call log for dashboards/diagnostics
                var (prov, model) = ParseOrigin(origin);
                var status = response != null ? (int)response.StatusCode : -1;
                var latency = sw.ElapsedMilliseconds;
                var timeoutMs = (int)(perRequestTimeout ?? GetTimeoutOrDefault(templateRequest)).TotalMilliseconds;
                logger.InfoWithCorrelation($"provider_call provider={prov} model={model} host={ProviderMetricsHelper.SanitizeName(serviceName)} endpoint={ProviderMetricsHelper.SanitizeName(endpoint)} status={status} latency_ms={latency} retries={maxRetries} timeout_ms={timeoutMs} cap={maxConcurrencyPerHost}");
            }
            catch { }

            return response;
        }

        private static int EvaluateStatusCode(HttpResponse response, Func<HttpResponse, bool> shouldRetry)
        {
            if (!shouldRetry(response))
            {
                return (int)System.Net.HttpStatusCode.OK;
            }

            return (int)response.StatusCode;
        }

        private static bool DefaultShouldRetry(HttpResponse resp)
        {
            var code = resp.StatusCode;
            var numeric = (int)code;
            if (code == System.Net.HttpStatusCode.TooManyRequests || code == System.Net.HttpStatusCode.RequestTimeout)
            {
                return true;
            }

            return numeric >= 500 && numeric <= 504;
        }

        private static TimeSpan GetTimeoutOrDefault(HttpRequest request)
        {
            if (request.RequestTimeout <= TimeSpan.Zero)
            {
                return TimeSpan.FromSeconds(45);
            }

            return request.RequestTimeout;
        }

        private static Task<HttpRequest> CloneHttpRequestAsync(HttpRequest source)
        {
            return Task.FromResult(CloneHttpRequest(source));
        }

        private static HttpRequest CloneHttpRequest(HttpRequest source)
        {
            var clone = new HttpRequest(source.Url.FullUri)
            {
                Method = source.Method,
                ContentSummary = source.ContentSummary,
                Credentials = source.Credentials,
                SuppressHttpError = source.SuppressHttpError,
                SuppressHttpErrorStatusCodes = source.SuppressHttpErrorStatusCodes?.ToList(),
                UseSimplifiedUserAgent = source.UseSimplifiedUserAgent,
                AllowAutoRedirect = source.AllowAutoRedirect,
                ConnectionKeepAlive = source.ConnectionKeepAlive,
                LogResponseContent = source.LogResponseContent,
                LogHttpError = source.LogHttpError,
                StoreRequestCookie = source.StoreRequestCookie,
                StoreResponseCookie = source.StoreResponseCookie,
                RequestTimeout = source.RequestTimeout,
                RateLimit = source.RateLimit,
                RateLimitKey = source.RateLimitKey,
                ResponseStream = source.ResponseStream
            };

            if (source.ContentData != null)
            {
                clone.ContentData = (byte[])source.ContentData.Clone();
            }

            if (source.Headers != null)
            {
                foreach (var key in source.Headers.AllKeys)
                {
                    var values = source.Headers.GetValues(key);
                    if (values == null)
                    {
                        continue;
                    }

                    foreach (var value in values)
                    {
                        clone.Headers.Set(key, value);
                    }
                }
            }

            foreach (var kvp in source.Cookies)
            {
                clone.Cookies[kvp.Key] = kvp.Value;
            }

            return clone;
        }

        private static HttpResponseMessage ToHttpResponseMessage(HttpResponse response)
        {
            var message = new HttpResponseMessage(response.StatusCode);
            foreach (var header in response.Headers)
            {
                if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    message.Content ??= new ByteArrayContent(Array.Empty<byte>());
                    message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return message;
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

        private static void TryRecordThrottle(string origin, HttpResponse resp)
        {
            try
            {
                var (prov, model) = ParseOrigin(origin);
                Services.Resilience.MetricsCollector.IncrementCounter(ProviderMetricsHelper.BuildThrottleMetric(prov, model));
                var cap = prov is "ollama" or "lmstudio" ? 8 : 2;
                var ttl = GetRetryAfter(resp) ?? TimeSpan.FromSeconds(60);
                if (ttl < TimeSpan.FromSeconds(5)) ttl = TimeSpan.FromSeconds(5);
                if (ttl > TimeSpan.FromMinutes(5)) ttl = TimeSpan.FromMinutes(5);
                Services.Resilience.LimiterRegistry.RegisterThrottle($"{prov}:{model}", ttl, cap);
            }
            catch
            {
                // Best-effort metrics only.
            }
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
