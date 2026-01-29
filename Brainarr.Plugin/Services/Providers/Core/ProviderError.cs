using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Core
{
    /// <summary>
    /// Represents a normalized provider error with retry and user-facing information.
    /// </summary>
    public sealed class ProviderError
    {
        public int HttpCode { get; init; }
        public int? BusinessCode { get; init; }
        public string? RawMessage { get; init; }
        public bool ShouldRetry { get; init; }
        public string? UserMessage { get; init; }
        public string? DocsUrl { get; init; }
        public ProviderErrorCategory Category { get; init; }

        public static ProviderError FromHttpCode(int httpCode, string? body = null)
        {
            var category = CategorizeHttpCode(httpCode, body);
            return new ProviderError
            {
                HttpCode = httpCode,
                RawMessage = body,
                ShouldRetry = IsRetryable(category),
                Category = category
            };
        }

        public static ProviderError Timeout()
        {
            return new ProviderError
            {
                HttpCode = 408,
                Category = ProviderErrorCategory.Timeout,
                ShouldRetry = true,
                UserMessage = "Request timed out. The provider may be slow or unreachable."
            };
        }

        public static ProviderError Cancelled()
        {
            return new ProviderError
            {
                HttpCode = 0,
                Category = ProviderErrorCategory.Cancelled,
                ShouldRetry = false,
                UserMessage = "Request was cancelled."
            };
        }

        public static ProviderError NetworkError(Exception ex)
        {
            return new ProviderError
            {
                HttpCode = 0,
                Category = ProviderErrorCategory.NetworkError,
                ShouldRetry = true,
                RawMessage = ex.Message,
                UserMessage = "Network error. Check your connection and provider URL."
            };
        }

        private static ProviderErrorCategory CategorizeHttpCode(int code, string? body)
        {
            return code switch
            {
                401 => ProviderErrorCategory.Authentication,
                403 => ProviderErrorCategory.Forbidden,
                429 => ProviderErrorCategory.RateLimit,
                400 or 422 => ProviderErrorCategory.BadRequest,
                408 => ProviderErrorCategory.Timeout,
                >= 500 and <= 504 => ProviderErrorCategory.ServerError,
                _ => ProviderErrorCategory.Unknown
            };
        }

        private static bool IsRetryable(ProviderErrorCategory category)
        {
            return category switch
            {
                ProviderErrorCategory.RateLimit => true,
                ProviderErrorCategory.Timeout => true,
                ProviderErrorCategory.ServerError => true,
                ProviderErrorCategory.NetworkError => true,
                _ => false
            };
        }
    }

    public enum ProviderErrorCategory
    {
        Unknown,
        Authentication,
        Forbidden,
        RateLimit,
        BadRequest,
        Timeout,
        ServerError,
        NetworkError,
        ContentFiltered,
        InsufficientQuota,
        Cancelled
    }
}
