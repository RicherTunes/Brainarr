using System;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    public static class HttpRequestExtensions
    {
        /// <summary>
        /// Sets the appropriate timeout for AI provider requests
        /// </summary>
        public static HttpRequest WithAITimeout(this HttpRequest request, int? customTimeoutSeconds = null)
        {
            var timeout = customTimeoutSeconds ?? BrainarrConstants.DefaultAITimeout;
            request.RequestTimeout = TimeSpan.FromSeconds(Math.Min(timeout, BrainarrConstants.MaxAITimeout));
            return request;
        }

        /// <summary>
        /// Sets timeout for model detection requests
        /// </summary>
        public static HttpRequest WithModelDetectionTimeout(this HttpRequest request)
        {
            request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.ModelDetectionTimeout);
            return request;
        }

        /// <summary>
        /// Sets timeout for health check requests
        /// </summary>
        public static HttpRequest WithHealthCheckTimeout(this HttpRequest request)
        {
            request.RequestTimeout = TimeSpan.FromMilliseconds(BrainarrConstants.HealthCheckTimeoutMs);
            return request;
        }

        /// <summary>
        /// Sets timeout for connection test requests
        /// </summary>
        public static HttpRequest WithTestConnectionTimeout(this HttpRequest request)
        {
            request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);
            return request;
        }
    }
}