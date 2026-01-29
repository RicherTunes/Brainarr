using System;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Logging
{
    /// <summary>
    /// Structured logging extensions for Brainarr AI providers.
    /// Provides consistent, parseable log output with correlation IDs.
    /// </summary>
    public static class BrainarrLoggerExtensions
    {
        private const string PluginName = "Brainarr";

        /// <summary>
        /// Log request start with correlation tracking.
        /// </summary>
        public static void LogRequestStart(
            this Logger logger,
            string provider,
            string operation,
            string correlationId,
            string? model = null,
            int attempt = 1)
        {
            logger.Info(
                "[{Plugin}] {Provider} {Operation} started | CorrelationId={CorrelationId} Model={Model} Attempt={Attempt}",
                PluginName, provider, operation, correlationId, model ?? "default", attempt);
        }

        /// <summary>
        /// Log successful request completion with timing.
        /// </summary>
        public static void LogRequestComplete(
            this Logger logger,
            string provider,
            string operation,
            string correlationId,
            long elapsedMs,
            int? inputTokens = null,
            int? outputTokens = null)
        {
            logger.Info(
                "[{Plugin}] {Provider} {Operation} completed | CorrelationId={CorrelationId} ElapsedMs={ElapsedMs} InputTokens={InputTokens} OutputTokens={OutputTokens}",
                PluginName, provider, operation, correlationId, elapsedMs, inputTokens ?? 0, outputTokens ?? 0);
        }

        /// <summary>
        /// Log request error with error details.
        /// </summary>
        public static void LogRequestError(
            this Logger logger,
            string provider,
            string operation,
            string correlationId,
            string errorCode,
            string errorMessage,
            Exception? exception = null)
        {
            if (exception != null)
            {
                logger.Error(
                    exception,
                    "[{Plugin}] {Provider} {Operation} error | CorrelationId={CorrelationId} ErrorCode={ErrorCode} Error={Error}",
                    PluginName, provider, operation, correlationId, errorCode, RedactSensitive(errorMessage));
            }
            else
            {
                logger.Error(
                    "[{Plugin}] {Provider} {Operation} error | CorrelationId={CorrelationId} ErrorCode={ErrorCode} Error={Error}",
                    PluginName, provider, operation, correlationId, errorCode, RedactSensitive(errorMessage));
            }
        }

        /// <summary>
        /// Log rate limit event.
        /// </summary>
        public static void LogRateLimited(
            this Logger logger,
            string provider,
            string correlationId,
            TimeSpan? retryAfter = null)
        {
            logger.Warn(
                "[{Plugin}] {Provider} rate limited | CorrelationId={CorrelationId} RetryAfterMs={RetryAfterMs}",
                PluginName, provider, correlationId, retryAfter?.TotalMilliseconds ?? -1);
        }

        /// <summary>
        /// Log rate limit recovery.
        /// </summary>
        public static void LogRateLimitRecovered(
            this Logger logger,
            string provider,
            string correlationId,
            int totalAttempts)
        {
            logger.Info(
                "[{Plugin}] {Provider} rate limit recovered | CorrelationId={CorrelationId} TotalAttempts={TotalAttempts}",
                PluginName, provider, correlationId, totalAttempts);
        }

        /// <summary>
        /// Log authentication failure.
        /// </summary>
        public static void LogAuthFail(
            this Logger logger,
            string provider,
            string correlationId,
            string reason)
        {
            logger.Warn(
                "[{Plugin}] {Provider} authentication failed | CorrelationId={CorrelationId} Reason={Reason}",
                PluginName, provider, correlationId, RedactSensitive(reason));
        }

        /// <summary>
        /// Log health check pass.
        /// </summary>
        public static void LogHealthCheckPass(
            this Logger logger,
            string provider,
            long elapsedMs)
        {
            logger.Info(
                "[{Plugin}] {Provider} health check passed | ElapsedMs={ElapsedMs}",
                PluginName, provider, elapsedMs);
        }

        /// <summary>
        /// Log health check failure.
        /// </summary>
        public static void LogHealthCheckFail(
            this Logger logger,
            string provider,
            string reason)
        {
            logger.Warn(
                "[{Plugin}] {Provider} health check failed | Reason={Reason}",
                PluginName, provider, RedactSensitive(reason));
        }

        /// <summary>
        /// Log info with correlation ID.
        /// </summary>
        public static void InfoWithCorrelation(
            this Logger logger,
            string message,
            string? correlationId = null)
        {
            if (correlationId != null)
            {
                logger.Info("{Message} | CorrelationId={CorrelationId}", message, correlationId);
            }
            else
            {
                logger.Info(message);
            }
        }

        /// <summary>
        /// Log debug with correlation ID.
        /// </summary>
        public static void DebugWithCorrelation(
            this Logger logger,
            string message,
            string? correlationId = null)
        {
            if (correlationId != null)
            {
                logger.Debug("{Message} | CorrelationId={CorrelationId}", message, correlationId);
            }
            else
            {
                logger.Debug(message);
            }
        }

        /// <summary>
        /// Redact potentially sensitive information from log messages.
        /// </summary>
        private static string RedactSensitive(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            // Simple redaction for common API key patterns
            if (value.Contains("sk-") || value.Contains("Bearer ") || value.Length > 50)
            {
                // Truncate long strings that might be keys/tokens
                if (value.Length > 100)
                {
                    return value.Substring(0, 50) + "...[REDACTED]";
                }
            }

            return value;
        }
    }
}
