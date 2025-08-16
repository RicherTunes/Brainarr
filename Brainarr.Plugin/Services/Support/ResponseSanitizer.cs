using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Sanitizes response content to remove sensitive information before logging.
    /// This prevents API keys, tokens, and other sensitive data from appearing in logs.
    /// </summary>
    public static class ResponseSanitizer
    {
        // Common API key patterns
        private static readonly List<Regex> SensitivePatterns = new List<Regex>
        {
            // API Keys and Tokens
            new Regex(@"(?i)(api[_\-]?key|apikey|api_token|access[_\-]?token|auth[_\-]?token|authentication[_\-]?token|bearer)\s*[:=]\s*['""]?([A-Za-z0-9\-_]{20,})['""]?", RegexOptions.Compiled),
            new Regex(@"(?i)(token|key|secret|password|passwd|pwd)\s*[:=]\s*['""]?([A-Za-z0-9\-_\./]{20,})['""]?", RegexOptions.Compiled),
            
            // Bearer tokens in headers
            new Regex(@"(?i)Bearer\s+[A-Za-z0-9\-_\./]{20,}", RegexOptions.Compiled),
            new Regex(@"(?i)Authorization\s*:\s*[^\s,]+", RegexOptions.Compiled),
            
            // API keys in various formats
            new Regex(@"sk-[A-Za-z0-9]{48,}", RegexOptions.Compiled), // OpenAI format
            new Regex(@"claude-[A-Za-z0-9\-]{36,}", RegexOptions.Compiled), // Anthropic format
            new Regex(@"AIza[A-Za-z0-9\-_]{35}", RegexOptions.Compiled), // Google format
            new Regex(@"gsk_[A-Za-z0-9]{32,}", RegexOptions.Compiled), // Groq format
            new Regex(@"pplx-[A-Za-z0-9]{48,}", RegexOptions.Compiled), // Perplexity format
            
            // URLs with embedded credentials
            new Regex(@"(?i)https?://[^:]+:[^@]+@[^\s]+", RegexOptions.Compiled),
            
            // JSON properties with sensitive names
            new Regex(@"""(api_key|apiKey|api_token|apiToken|access_token|accessToken|auth_token|authToken|secret|password|token|key)""\s*:\s*""[^""]+""", RegexOptions.Compiled),
            
            // Environment variable patterns
            new Regex(@"(?i)(OPENAI_API_KEY|ANTHROPIC_API_KEY|GOOGLE_API_KEY|GROQ_API_KEY|DEEPSEEK_API_KEY|PERPLEXITY_API_KEY|OPENROUTER_API_KEY)\s*=\s*[^\s]+", RegexOptions.Compiled),
            
            // Base64 encoded potential secrets (longer than 40 chars)
            new Regex(@"(?:[A-Za-z0-9+/]{40,}={0,2})(?![A-Za-z0-9+/=])", RegexOptions.Compiled),
            
            // Email addresses (to protect privacy)
            new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled),
            
            // Credit card numbers
            new Regex(@"\b(?:\d{4}[\s\-]?){3}\d{4}\b", RegexOptions.Compiled),
            
            // SSH keys
            new Regex(@"-----BEGIN [A-Z ]+ KEY-----[\s\S]+?-----END [A-Z ]+ KEY-----", RegexOptions.Compiled)
        };

        /// <summary>
        /// Sanitizes response content by removing or masking sensitive information.
        /// </summary>
        /// <param name="content">The content to sanitize</param>
        /// <param name="maxLength">Maximum length of content to return (default: 500)</param>
        /// <returns>Sanitized content safe for logging</returns>
        public static string SanitizeResponse(string content, int maxLength = 500)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            // Truncate to reasonable length first
            var workingContent = content.Length > maxLength * 2 
                ? content.Substring(0, maxLength * 2) 
                : content;

            // Apply all sanitization patterns
            foreach (var pattern in SensitivePatterns)
            {
                workingContent = pattern.Replace(workingContent, match =>
                {
                    // Preserve the key name but mask the value
                    var groups = match.Groups;
                    if (groups.Count > 1)
                    {
                        // If we captured a key name, preserve it
                        var keyPart = match.Value.Substring(0, Math.Min(match.Value.IndexOf('=') + 1, match.Value.IndexOf(':') + 1));
                        if (keyPart.Length > 0)
                        {
                            return keyPart + "[REDACTED]";
                        }
                    }
                    
                    // For patterns without clear key/value separation, replace entirely
                    return "[REDACTED]";
                });
            }

            // Final truncation with ellipsis if needed
            if (workingContent.Length > maxLength)
            {
                workingContent = workingContent.Substring(0, maxLength) + "... [truncated]";
            }

            return workingContent;
        }

        /// <summary>
        /// Sanitizes exception messages and stack traces.
        /// </summary>
        public static string SanitizeException(Exception ex, bool includeStackTrace = false)
        {
            if (ex == null)
            {
                return null;
            }

            var message = SanitizeResponse(ex.Message, 1000);
            
            if (!includeStackTrace)
            {
                return message;
            }

            var stackTrace = ex.StackTrace;
            if (!string.IsNullOrEmpty(stackTrace))
            {
                // Remove file paths that might contain usernames
                stackTrace = Regex.Replace(stackTrace, @"in [A-Z]:\\[^\s]+", "in [PATH REDACTED]", RegexOptions.IgnoreCase);
                stackTrace = Regex.Replace(stackTrace, @"in /[^\s]+", "in [PATH REDACTED]", RegexOptions.IgnoreCase);
            }

            return $"{message}\nStack Trace:\n{stackTrace}";
        }

        /// <summary>
        /// Checks if content potentially contains sensitive information.
        /// </summary>
        public static bool ContainsSensitiveData(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            foreach (var pattern in SensitivePatterns)
            {
                if (pattern.IsMatch(content))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Sanitizes a URL by removing any embedded credentials.
        /// </summary>
        public static string SanitizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            // Remove credentials from URLs (http://user:pass@host -> http://host)
            var urlPattern = new Regex(@"(https?://)([^:]+):([^@]+)@(.+)", RegexOptions.IgnoreCase);
            return urlPattern.Replace(url, "$1[REDACTED]@$4");
        }
    }
}