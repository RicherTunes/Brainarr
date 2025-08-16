using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Standardized error handling for AI providers
    /// </summary>
    public static class ErrorHandling
    {
        /// <summary>
        /// Standard error response for all providers
        /// </summary>
        public static List<Recommendation> HandleProviderError(
            string providerName,
            Exception ex,
            Logger logger,
            bool throwOnCritical = false)
        {
            // Categorize the error
            var errorCategory = CategorizeError(ex);

            switch (errorCategory)
            {
                case ErrorCategory.RateLimit:
                    logger.Warn($"{providerName}: Rate limit exceeded. Will retry later.");
                    break;

                case ErrorCategory.Authentication:
                    logger.Error($"{providerName}: Authentication failed. Check API key.");
                    if (throwOnCritical)
                        throw new InvalidOperationException($"{providerName} authentication failed", ex);
                    break;

                case ErrorCategory.Network:
                    logger.Warn($"{providerName}: Network error - {ex.Message}");
                    break;

                case ErrorCategory.Timeout:
                    logger.Warn($"{providerName}: Request timed out");
                    break;

                case ErrorCategory.InvalidResponse:
                    logger.Debug($"{providerName}: Invalid response format - {ex.Message}");
                    break;

                case ErrorCategory.ServiceError:
                    logger.Error(ex, $"{providerName}: Service error");
                    break;

                default:
                    logger.Error(ex, $"{providerName}: Unexpected error");
                    break;
            }

            // Always return empty list for graceful degradation
            return new List<Recommendation>();
        }

        /// <summary>
        /// Handle HTTP response errors consistently
        /// </summary>
        public static void HandleHttpError(
            string providerName,
            HttpStatusCode statusCode,
            string responseContent,
            Logger logger)
        {
            switch (statusCode)
            {
                case HttpStatusCode.Unauthorized:
                    logger.Error($"{providerName}: 401 Unauthorized - Invalid API key");
                    break;

                case HttpStatusCode.Forbidden:
                    logger.Error($"{providerName}: 403 Forbidden - Access denied");
                    break;

                case HttpStatusCode.NotFound:
                    logger.Error($"{providerName}: 404 Not Found - Check API endpoint");
                    break;

                case HttpStatusCode.TooManyRequests:
                    logger.Warn($"{providerName}: 429 Too Many Requests - Rate limited");
                    break;

                case HttpStatusCode.InternalServerError:
                case HttpStatusCode.BadGateway:
                case HttpStatusCode.ServiceUnavailable:
                case HttpStatusCode.GatewayTimeout:
                    logger.Warn($"{providerName}: {(int)statusCode} {statusCode} - Service temporarily unavailable");
                    break;

                default:
                    logger.Error($"{providerName}: {(int)statusCode} {statusCode} - {responseContent?.Substring(0, Math.Min(200, responseContent.Length))}");
                    break;
            }
        }

        /// <summary>
        /// Categorize exceptions for appropriate handling
        /// </summary>
        private static ErrorCategory CategorizeError(Exception ex)
        {
            if (ex is HttpRequestException httpEx)
            {
                if (httpEx.Message.Contains("401") || httpEx.Message.Contains("Unauthorized"))
                    return ErrorCategory.Authentication;
                if (httpEx.Message.Contains("429") || httpEx.Message.Contains("Too Many"))
                    return ErrorCategory.RateLimit;
                return ErrorCategory.Network;
            }

            if (ex is TaskCanceledException || ex is TimeoutException)
                return ErrorCategory.Timeout;

            if (ex is JsonException || ex is InvalidOperationException)
                return ErrorCategory.InvalidResponse;

            if (ex is WebException)
                return ErrorCategory.Network;

            return ErrorCategory.Unknown;
        }

        private enum ErrorCategory
        {
            Authentication,
            RateLimit,
            Network,
            Timeout,
            InvalidResponse,
            ServiceError,
            Unknown
        }
    }

    /// <summary>
    /// Custom exceptions for AI providers
    /// </summary>
    public class AIProviderException : Exception
    {
        public string Provider { get; }
        public string ErrorCode { get; }

        public AIProviderException(string provider, string message, string errorCode = null)
            : base(message)
        {
            Provider = provider;
            ErrorCode = errorCode;
        }

        public AIProviderException(string provider, string message, Exception innerException)
            : base(message, innerException)
        {
            Provider = provider;
        }
    }

    public class RateLimitException : AIProviderException
    {
        public TimeSpan? RetryAfter { get; }

        public RateLimitException(string provider, TimeSpan? retryAfter = null)
            : base(provider, "Rate limit exceeded")
        {
            RetryAfter = retryAfter;
        }
    }

    public class AuthenticationException : AIProviderException
    {
        public AuthenticationException(string provider)
            : base(provider, "Authentication failed - check API key", "AUTH_FAILED")
        {
        }
    }
}