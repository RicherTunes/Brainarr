using System;
using System.Text.RegularExpressions;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Thread-safe logger wrapper that sanitizes sensitive information from logs
    /// </summary>
    public static class SafeLogger
    {
        private static readonly Regex SensitiveDataPattern = new Regex(
            @"(api[_-]?key|password|token|secret|bearer|authorization|credential)[:\s=]*['""]?([^'""}\s,]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex UrlCredentialPattern = new Regex(
            @"(https?://)([^:]+):([^@]+)@",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Logs an error with sanitized exception details
        /// </summary>
        public static void LogError(Logger logger, Exception ex, string message, params object[] args)
        {
            if (logger == null) return;

            var sanitizedException = SanitizeException(ex);
            var sanitizedMessage = SanitizeMessage(string.Format(message, args));
            
            logger.Error(sanitizedException, sanitizedMessage);
        }

        /// <summary>
        /// Logs a warning with sanitized content
        /// </summary>
        public static void LogWarn(Logger logger, string message, params object[] args)
        {
            if (logger == null) return;

            var sanitizedMessage = SanitizeMessage(string.Format(message, args));
            logger.Warn(sanitizedMessage);
        }

        /// <summary>
        /// Logs info with sanitized content
        /// </summary>
        public static void LogInfo(Logger logger, string message, params object[] args)
        {
            if (logger == null) return;

            var sanitizedMessage = SanitizeMessage(string.Format(message, args));
            logger.Info(sanitizedMessage);
        }

        /// <summary>
        /// Logs debug info with sanitized content
        /// </summary>
        public static void LogDebug(Logger logger, string message, params object[] args)
        {
            if (logger == null) return;

            var sanitizedMessage = SanitizeMessage(string.Format(message, args));
            logger.Debug(sanitizedMessage);
        }

        /// <summary>
        /// Sanitizes a message by removing sensitive data
        /// </summary>
        private static string SanitizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            // Replace sensitive data patterns
            message = SensitiveDataPattern.Replace(message, "$1=***REDACTED***");
            
            // Replace credentials in URLs
            message = UrlCredentialPattern.Replace(message, "$1***:***@");

            // Remove any base64 encoded strings that might be keys
            message = Regex.Replace(message, @"\b[A-Za-z0-9+/]{20,}={0,2}\b", "***BASE64_REDACTED***");

            return message;
        }

        /// <summary>
        /// Recursively sanitizes exception messages and stack traces
        /// </summary>
        private static Exception SanitizeException(Exception ex)
        {
            if (ex == null)
                return null;

            var sanitizedMessage = SanitizeMessage(ex.Message);
            var sanitizedStackTrace = SanitizeMessage(ex.StackTrace);

            var sanitizedException = new Exception(sanitizedMessage, SanitizeException(ex.InnerException))
            {
                Source = ex.Source,
                HelpLink = ex.HelpLink
            };

            // Copy safe data from original exception
            foreach (var key in ex.Data.Keys)
            {
                var value = ex.Data[key];
                sanitizedException.Data[key] = value is string str ? SanitizeMessage(str) : value;
            }

            return sanitizedException;
        }

        /// <summary>
        /// Masks an API key for safe display (shows last 4 chars only)
        /// </summary>
        public static string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return "***EMPTY***";

            if (apiKey.Length <= 4)
                return "***";

            return $"***{apiKey.Substring(apiKey.Length - 4)}";
        }

        /// <summary>
        /// Creates a safe representation of a URL (removes credentials)
        /// </summary>
        public static string SanitizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            try
            {
                var uri = new Uri(url);
                var builder = new UriBuilder(uri)
                {
                    UserName = string.IsNullOrEmpty(uri.UserInfo) ? "" : "***",
                    Password = ""
                };
                return builder.ToString();
            }
            catch
            {
                // If URL parsing fails, apply regex sanitization
                return UrlCredentialPattern.Replace(url, "$1***:***@");
            }
        }
    }
}