using System;
using System.Text.RegularExpressions;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public static class SecureLogger
    {
        // Patterns to detect sensitive information
        private static readonly Regex ApiKeyPattern = new Regex(
            @"(api[_-]?key|apikey|key|token|secret|password|auth|bearer|credentials?)[\""\s:=]*([A-Za-z0-9\-_]{10,})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex BearerTokenPattern = new Regex(
            @"(Bearer\s+)([A-Za-z0-9\-_\.]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex SpecificKeyPatterns = new Regex(
            @"(sk-[A-Za-z0-9]{48,}|sk-ant-[A-Za-z0-9]{100,}|AIza[A-Za-z0-9\-_]{35}|gsk_[A-Za-z0-9]{50,})",
            RegexOptions.Compiled);

        /// <summary>
        /// Sanitizes a message by removing or masking sensitive information
        /// </summary>
        public static string SanitizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            // Replace API keys and tokens
            message = ApiKeyPattern.Replace(message, m => 
            {
                var keyName = m.Groups[1].Value;
                var keyValue = m.Groups[2].Value;
                if (keyValue.Length > 4)
                {
                    return $"{keyName}=[REDACTED-{keyValue.Substring(0, 4)}...]";
                }
                return $"{keyName}=[REDACTED]";
            });

            // Replace Bearer tokens
            message = BearerTokenPattern.Replace(message, "$1[REDACTED]");
            
            // Replace specific key patterns (OpenAI, Anthropic, Google, etc.)
            message = SpecificKeyPatterns.Replace(message, m =>
            {
                var key = m.Value;
                if (key.Length > 8)
                {
                    return $"{key.Substring(0, 8)}...[REDACTED]";
                }
                return "[REDACTED]";
            });

            return message;
        }

        /// <summary>
        /// Logs an error with sanitized content
        /// </summary>
        public static void LogError(Logger logger, string message, Exception ex = null)
        {
            var sanitized = SanitizeMessage(message);
            
            if (ex != null)
            {
                var sanitizedException = SanitizeMessage(ex.ToString());
                logger.Error($"{sanitized} - Exception: {sanitizedException}");
            }
            else
            {
                logger.Error(sanitized);
            }
        }

        /// <summary>
        /// Logs a warning with sanitized content
        /// </summary>
        public static void LogWarn(Logger logger, string message)
        {
            logger.Warn(SanitizeMessage(message));
        }

        /// <summary>
        /// Logs debug information with sanitized content
        /// </summary>
        public static void LogDebug(Logger logger, string message)
        {
            logger.Debug(SanitizeMessage(message));
        }

        /// <summary>
        /// Logs information with sanitized content
        /// </summary>
        public static void LogInfo(Logger logger, string message)
        {
            logger.Info(SanitizeMessage(message));
        }

        /// <summary>
        /// Sanitizes HTTP response content for logging
        /// </summary>
        public static string SanitizeHttpResponse(int statusCode, string content)
        {
            // Limit content length to prevent log flooding
            const int maxContentLength = 500;
            
            if (string.IsNullOrEmpty(content))
            {
                return $"Status: {statusCode}, Content: [empty]";
            }

            var sanitized = SanitizeMessage(content);
            
            if (sanitized.Length > maxContentLength)
            {
                sanitized = sanitized.Substring(0, maxContentLength) + "...[truncated]";
            }

            return $"Status: {statusCode}, Content: {sanitized}";
        }
    }
}