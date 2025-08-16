using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Provides input sanitization to prevent injection attacks and data corruption
    /// </summary>
    public static class InputSanitizer
    {
        private static readonly Regex ControlCharPattern = new Regex(@"[\x00-\x1F\x7F]", RegexOptions.Compiled);
        private static readonly Regex PromptInjectionPattern = new Regex(
            @"(system|assistant|user|human|ai):\s*", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SqlInjectionPattern = new Regex(
            @"(\b(union|select|insert|update|delete|drop|create|alter|exec|execute|script|javascript|eval)\b)|(-{2}|/\*|\*/|;)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PathTraversalPattern = new Regex(
            @"(\.\./|\.\.\\|%2e%2e|%252e%252e)", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Sanitizes input for use in AI prompts
        /// </summary>
        public static string SanitizeForPrompt(string input, int maxLength = 1000)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // Remove control characters
            input = ControlCharPattern.Replace(input, " ");

            // Remove potential prompt injection patterns
            input = PromptInjectionPattern.Replace(input, "");

            // Escape markdown code blocks
            input = input.Replace("```", "'''");

            // Remove multiple consecutive spaces
            input = Regex.Replace(input, @"\s+", " ");

            // Trim and limit length
            input = input.Trim();
            if (input.Length > maxLength)
            {
                input = input.Substring(0, maxLength - 3) + "...";
            }

            return input;
        }

        /// <summary>
        /// Sanitizes artist or album names for storage
        /// </summary>
        public static string SanitizeForStorage(string input, int maxLength = 255)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            // Remove control characters
            input = ControlCharPattern.Replace(input, "");

            // Normalize quotes
            input = input.Replace("\"", "")
                        .Replace("'", "'")
                        .Replace("`", "'")
                        .Replace(""", "\"")
                        .Replace(""", "\"")
                        .Replace("'", "'")
                        .Replace("'", "'");

            // Remove potential SQL injection attempts
            if (SqlInjectionPattern.IsMatch(input))
            {
                input = SqlInjectionPattern.Replace(input, "");
            }

            // Trim whitespace
            input = input.Trim();

            // Limit length
            if (input.Length > maxLength)
            {
                input = input.Substring(0, maxLength);
            }

            return string.IsNullOrWhiteSpace(input) ? null : input;
        }

        /// <summary>
        /// Sanitizes file paths to prevent directory traversal
        /// </summary>
        public static string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            // Remove path traversal attempts
            path = PathTraversalPattern.Replace(path, "");

            // Remove control characters
            path = ControlCharPattern.Replace(path, "");

            // Normalize slashes
            path = path.Replace('\\', '/');

            // Remove double slashes
            while (path.Contains("//"))
            {
                path = path.Replace("//", "/");
            }

            return path;
        }

        /// <summary>
        /// Validates and sanitizes URLs
        /// </summary>
        public static bool TryValidateUrl(string url, out Uri validatedUri)
        {
            validatedUri = null;

            if (string.IsNullOrWhiteSpace(url))
                return false;

            // Basic length check
            if (url.Length > 2048)
                return false;

            try
            {
                validatedUri = new Uri(url);

                // Only allow HTTP and HTTPS
                if (validatedUri.Scheme != Uri.UriSchemeHttp && 
                    validatedUri.Scheme != Uri.UriSchemeHttps)
                {
                    validatedUri = null;
                    return false;
                }

                // Check for localhost/private IPs (configurable based on requirements)
                var host = validatedUri.Host.ToLower();
                if (IsPrivateHost(host))
                {
                    // Log this as it might be SSRF attempt
                    validatedUri = null;
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a host is private/internal
        /// </summary>
        private static bool IsPrivateHost(string host)
        {
            var privatePatterns = new[]
            {
                "localhost",
                "127.0.0.1",
                "::1",
                "0.0.0.0",
                "169.254", // Link-local
                "10.",      // Private Class A
                "172.16",   // Private Class B start
                "172.17", "172.18", "172.19",
                "172.20", "172.21", "172.22", "172.23",
                "172.24", "172.25", "172.26", "172.27",
                "172.28", "172.29", "172.30", "172.31", // Private Class B end
                "192.168"   // Private Class C
            };

            return privatePatterns.Any(pattern => host.StartsWith(pattern));
        }

        /// <summary>
        /// Generates a safe filename from user input
        /// </summary>
        public static string GenerateSafeFilename(string input, int maxLength = 255)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "unnamed";

            // Remove invalid filename characters
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            var result = new StringBuilder();

            foreach (char c in input)
            {
                if (!invalidChars.Contains(c))
                {
                    result.Append(c);
                }
                else
                {
                    result.Append('_');
                }
            }

            var filename = result.ToString();

            // Remove leading/trailing dots and spaces
            filename = filename.Trim('. ');

            // Limit length
            if (filename.Length > maxLength)
            {
                var extension = System.IO.Path.GetExtension(filename);
                var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(filename);
                var maxNameLength = maxLength - extension.Length;
                
                if (maxNameLength > 0)
                {
                    filename = nameWithoutExt.Substring(0, maxNameLength) + extension;
                }
                else
                {
                    filename = filename.Substring(0, maxLength);
                }
            }

            return string.IsNullOrWhiteSpace(filename) ? "unnamed" : filename;
        }

        /// <summary>
        /// Validates numeric input within bounds
        /// </summary>
        public static int ValidateIntegerInput(string input, int min, int max, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;

            if (int.TryParse(input, out int value))
            {
                if (value < min) return min;
                if (value > max) return max;
                return value;
            }

            return defaultValue;
        }
    }
}