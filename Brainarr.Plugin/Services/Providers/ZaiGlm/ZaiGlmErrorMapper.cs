using System;
using System.Text.RegularExpressions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Core;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.ZaiGlm
{
    /// <summary>
    /// Maps Z.AI GLM-specific error codes and responses to normalized ProviderError instances.
    /// Z.AI uses business codes in the 1000-1309 range for specific error conditions.
    /// </summary>
    public static class ZaiGlmErrorMapper
    {
        // Documentation link for Z.AI troubleshooting
        private const string ZaiDocsUrl = "https://open.bigmodel.cn/dev/api/error-code/list";

        #region Business Code Ranges

        // Authentication errors: 1000-1004
        private const int AuthErrorMin = 1000;
        private const int AuthErrorMax = 1004;

        // Account issues: 1110-1121
        private const int AccountErrorMin = 1110;
        private const int AccountErrorMax = 1121;
        private const int InsufficientBalanceCode = 1113;

        // API errors: 1210-1234
        private const int ApiErrorMin = 1210;
        private const int ApiErrorMax = 1234;
        private const int ModelNotFoundCode = 1211;
        private const int InvalidParametersCode = 1214;

        // Rate limiting: 1300-1309
        private const int RateLimitMin = 1300;
        private const int RateLimitMax = 1309;

        #endregion

        /// <summary>
        /// Determines if a response should be retried based on status code and body content.
        /// </summary>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="body">Response body content.</param>
        /// <returns>True if the request should be retried.</returns>
        public static bool ShouldRetry(int statusCode, string? body)
        {
            // Never retry auth errors
            if (statusCode == 401 || statusCode == 403)
                return false;

            // Don't retry content filtered responses
            if (body?.Contains("\"sensitive\"") == true)
                return false;

            // Check Z.AI business codes
            var businessCode = ParseBusinessCode(body);
            if (businessCode.HasValue)
            {
                // Don't retry auth errors (1000-1004) or account issues (1110-1121)
                if (businessCode >= AuthErrorMin && businessCode <= AuthErrorMax)
                    return false;
                if (businessCode >= AccountErrorMin && businessCode <= AccountErrorMax)
                    return false;
            }

            // Retry rate limits, timeouts, and server errors
            return statusCode == 429 || statusCode == 408 || (statusCode >= 500 && statusCode <= 504);
        }

        /// <summary>
        /// Checks if the response body contains a Z.AI error structure.
        /// </summary>
        /// <param name="body">Response body content.</param>
        /// <returns>True if an error is present in the body.</returns>
        public static bool HasErrorInBody(string? body)
        {
            if (string.IsNullOrEmpty(body))
                return false;
            return body.Contains("\"error\"") && body.Contains("\"code\"");
        }

        /// <summary>
        /// Maps a Z.AI response to a normalized ProviderError.
        /// </summary>
        /// <param name="httpCode">HTTP status code.</param>
        /// <param name="body">Response body content.</param>
        /// <returns>A normalized ProviderError instance.</returns>
        public static ProviderError MapError(int httpCode, string? body)
        {
            var businessCode = ParseBusinessCode(body);
            var message = ParseErrorMessage(body);

            // Authentication errors (1000-1004)
            if (businessCode >= AuthErrorMin && businessCode <= AuthErrorMax)
            {
                return new ProviderError
                {
                    HttpCode = httpCode,
                    BusinessCode = businessCode,
                    Category = ProviderErrorCategory.Authentication,
                    ShouldRetry = false,
                    RawMessage = message,
                    UserMessage = $"Z.AI authentication error: {message ?? "Invalid API key or token expired"}",
                    DocsUrl = ZaiDocsUrl
                };
            }

            // Account issues (1110-1121)
            if (businessCode >= AccountErrorMin && businessCode <= AccountErrorMax)
            {
                var category = businessCode == InsufficientBalanceCode
                    ? ProviderErrorCategory.InsufficientQuota
                    : ProviderErrorCategory.Forbidden;

                var userMsg = businessCode == InsufficientBalanceCode
                    ? "Z.AI account has insufficient balance (quota exceeded)"
                    : $"Z.AI account issue: {message ?? "Check Z.AI dashboard"}";

                return new ProviderError
                {
                    HttpCode = httpCode,
                    BusinessCode = businessCode,
                    Category = category,
                    ShouldRetry = false,
                    RawMessage = message,
                    UserMessage = userMsg,
                    DocsUrl = ZaiDocsUrl
                };
            }

            // Rate limiting (1300-1309)
            if (businessCode >= RateLimitMin && businessCode <= RateLimitMax)
            {
                return new ProviderError
                {
                    HttpCode = httpCode,
                    BusinessCode = businessCode,
                    Category = ProviderErrorCategory.RateLimit,
                    ShouldRetry = true,
                    RawMessage = message,
                    UserMessage = "Z.AI rate limit exceeded - too many requests",
                    DocsUrl = ZaiDocsUrl
                };
            }

            // API errors (1210-1234)
            if (businessCode >= ApiErrorMin && businessCode <= ApiErrorMax)
            {
                var userMsg = businessCode switch
                {
                    ModelNotFoundCode => "Z.AI model not found",
                    InvalidParametersCode => "Z.AI invalid request parameters",
                    _ => $"Z.AI API error: {message ?? "Unknown error"}"
                };

                return new ProviderError
                {
                    HttpCode = httpCode,
                    BusinessCode = businessCode,
                    Category = ProviderErrorCategory.BadRequest,
                    ShouldRetry = false,
                    RawMessage = message,
                    UserMessage = userMsg,
                    DocsUrl = ZaiDocsUrl
                };
            }

            // Fall back to HTTP status code mapping
            return httpCode switch
            {
                401 => new ProviderError
                {
                    HttpCode = httpCode,
                    Category = ProviderErrorCategory.Authentication,
                    ShouldRetry = false,
                    RawMessage = body,
                    UserMessage = "Z.AI authentication failed - check API key"
                },
                403 => new ProviderError
                {
                    HttpCode = httpCode,
                    Category = ProviderErrorCategory.Forbidden,
                    ShouldRetry = false,
                    RawMessage = body,
                    UserMessage = "Z.AI access forbidden - check permissions"
                },
                429 => new ProviderError
                {
                    HttpCode = httpCode,
                    Category = ProviderErrorCategory.RateLimit,
                    ShouldRetry = true,
                    RawMessage = body,
                    UserMessage = "Z.AI rate limit exceeded"
                },
                >= 500 and <= 504 => new ProviderError
                {
                    HttpCode = httpCode,
                    Category = ProviderErrorCategory.ServerError,
                    ShouldRetry = true,
                    RawMessage = body,
                    UserMessage = $"Z.AI server error ({httpCode})"
                },
                _ => new ProviderError
                {
                    HttpCode = httpCode,
                    Category = ProviderErrorCategory.Unknown,
                    ShouldRetry = false,
                    RawMessage = body,
                    UserMessage = $"Z.AI API error: HTTP {httpCode} - {message ?? body}"
                }
            };
        }

        /// <summary>
        /// Creates an exception from a Z.AI error response.
        /// </summary>
        /// <param name="httpCode">HTTP status code.</param>
        /// <param name="body">Response body content.</param>
        /// <returns>An exception describing the error.</returns>
        public static Exception ToException(int httpCode, string? body)
        {
            var error = MapError(httpCode, body);
            return new InvalidOperationException(error.UserMessage);
        }

        /// <summary>
        /// Parses the business error code from a Z.AI error response body.
        /// Z.AI returns: {"error": {"code": "1234", "message": "..."}}
        /// </summary>
        private static int? ParseBusinessCode(string? body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            var match = Regex.Match(body, @"""code""\s*:\s*""?(\d+)""?");
            return match.Success && int.TryParse(match.Groups[1].Value, out var code) ? code : null;
        }

        /// <summary>
        /// Parses the error message from a Z.AI error response body.
        /// </summary>
        private static string? ParseErrorMessage(string? body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            var match = Regex.Match(body, @"""message""\s*:\s*""([^""]+)""");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
