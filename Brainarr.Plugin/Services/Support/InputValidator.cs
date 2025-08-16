using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Provides comprehensive input validation and sanitization for user inputs
    /// to prevent injection attacks and ensure data integrity.
    /// </summary>
    public static class InputValidator
    {
        // Maximum lengths for various input types
        private const int MaxGenreLength = 50;
        private const int MaxPromptLength = 2000;
        private const int MaxUrlLength = 2048;
        private const int MaxModelNameLength = 100;
        private const int MaxApiKeyLength = 256;

        // Dangerous patterns that could indicate injection attempts
        private static readonly Regex SqlInjectionPattern = new Regex(
            @"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE|UNION|FROM|WHERE|JOIN|ORDER BY|GROUP BY|HAVING)\b|--|;|'|""|/\*|\*/|xp_|sp_|0x)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ScriptInjectionPattern = new Regex(
            @"(<script|<iframe|javascript:|onerror=|onload=|<img|<body|<html|document\.|window\.|eval\(|setTimeout|setInterval|<svg)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CommandInjectionPattern = new Regex(
            @"(;|\||&|`|\$\(|<\(|>\(|\n|\r|&&|\|\||>|<)",
            RegexOptions.Compiled);

        private static readonly Regex PathTraversalPattern = new Regex(
            @"(\.\.\/|\.\.\\|\.\.%2F|\.\.%5C|%252E%252E|\.\.%252F|\.\.%255C)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Valid character patterns
        private static readonly Regex ValidGenrePattern = new Regex(
            @"^[a-zA-Z0-9\s\-_&/,.'()]+$",
            RegexOptions.Compiled);

        private static readonly Regex ValidModelNamePattern = new Regex(
            @"^[a-zA-Z0-9\-_.:/@]+$",
            RegexOptions.Compiled);

        private static readonly Regex ValidUrlPattern = new Regex(
            @"^https?://[a-zA-Z0-9\-._~:/?#[\]@!$&'()*+,;=%]+$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Validates and sanitizes genre input.
        /// </summary>
        public static string ValidateGenre(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
            {
                return null;
            }

            // Trim and check length
            genre = genre.Trim();
            if (genre.Length > MaxGenreLength)
            {
                throw new ArgumentException($"Genre exceeds maximum length of {MaxGenreLength} characters");
            }

            // Check for injection patterns
            if (ContainsInjectionPattern(genre))
            {
                throw new ArgumentException("Genre contains invalid characters or potential injection pattern");
            }

            // Validate against allowed pattern
            if (!ValidGenrePattern.IsMatch(genre))
            {
                // Remove invalid characters instead of throwing
                genre = Regex.Replace(genre, @"[^a-zA-Z0-9\s\-_&/,.'()]+", "");
            }

            return genre;
        }

        /// <summary>
        /// Validates and sanitizes multiple genres.
        /// </summary>
        public static List<string> ValidateGenres(IEnumerable<string> genres)
        {
            if (genres == null)
            {
                return new List<string>();
            }

            var validatedGenres = new List<string>();
            foreach (var genre in genres)
            {
                try
                {
                    var validated = ValidateGenre(genre);
                    if (!string.IsNullOrWhiteSpace(validated))
                    {
                        validatedGenres.Add(validated);
                    }
                }
                catch (ArgumentException)
                {
                    // Skip invalid genres
                    continue;
                }
            }

            return validatedGenres;
        }

        /// <summary>
        /// Validates and sanitizes user prompt input.
        /// </summary>
        public static string ValidatePrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return null;
            }

            // Trim and check length
            prompt = prompt.Trim();
            if (prompt.Length > MaxPromptLength)
            {
                prompt = prompt.Substring(0, MaxPromptLength);
            }

            // Check for injection patterns
            if (ContainsInjectionPattern(prompt))
            {
                // Remove dangerous patterns instead of throwing
                prompt = RemoveInjectionPatterns(prompt);
            }

            // Escape special characters that might be interpreted by AI models
            prompt = EscapeSpecialCharacters(prompt);

            return prompt;
        }

        /// <summary>
        /// Validates URL input.
        /// </summary>
        public static string ValidateUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL cannot be empty");
            }

            url = url.Trim();

            // Check length
            if (url.Length > MaxUrlLength)
            {
                throw new ArgumentException($"URL exceeds maximum length of {MaxUrlLength} characters");
            }

            // Validate URL format
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("Invalid URL format");
            }

            // Ensure HTTP or HTTPS only
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("Only HTTP and HTTPS URLs are allowed");
            }

            // Check for local/private IP addresses
            if (IsLocalOrPrivateUrl(uri))
            {
                throw new ArgumentException("Local or private network URLs are not allowed");
            }

            return url;
        }

        /// <summary>
        /// Validates model name input.
        /// </summary>
        public static string ValidateModelName(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                throw new ArgumentException("Model name cannot be empty");
            }

            modelName = modelName.Trim();

            // Check length
            if (modelName.Length > MaxModelNameLength)
            {
                throw new ArgumentException($"Model name exceeds maximum length of {MaxModelNameLength} characters");
            }

            // Validate against allowed pattern
            if (!ValidModelNamePattern.IsMatch(modelName))
            {
                throw new ArgumentException("Model name contains invalid characters");
            }

            // Check for path traversal
            if (PathTraversalPattern.IsMatch(modelName))
            {
                throw new ArgumentException("Model name contains invalid path characters");
            }

            return modelName;
        }

        /// <summary>
        /// Validates API key format (basic validation only).
        /// </summary>
        public static string ValidateApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key cannot be empty");
            }

            apiKey = apiKey.Trim();

            // Check length
            if (apiKey.Length > MaxApiKeyLength)
            {
                throw new ArgumentException($"API key exceeds maximum length of {MaxApiKeyLength} characters");
            }

            // Basic character validation (alphanumeric, hyphens, underscores)
            if (!Regex.IsMatch(apiKey, @"^[a-zA-Z0-9\-_]+$"))
            {
                throw new ArgumentException("API key contains invalid characters");
            }

            return apiKey;
        }

        /// <summary>
        /// Validates integer input within a range.
        /// </summary>
        public static int ValidateIntegerRange(int value, int min, int max, string paramName)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(paramName, 
                    $"{paramName} must be between {min} and {max}");
            }
            return value;
        }

        /// <summary>
        /// Checks if input contains any injection patterns.
        /// </summary>
        private static bool ContainsInjectionPattern(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            return SqlInjectionPattern.IsMatch(input) ||
                   ScriptInjectionPattern.IsMatch(input) ||
                   CommandInjectionPattern.IsMatch(input) ||
                   PathTraversalPattern.IsMatch(input);
        }

        /// <summary>
        /// Removes injection patterns from input.
        /// </summary>
        private static string RemoveInjectionPatterns(string input)
        {
            input = SqlInjectionPattern.Replace(input, "");
            input = ScriptInjectionPattern.Replace(input, "");
            input = CommandInjectionPattern.Replace(input, "");
            input = PathTraversalPattern.Replace(input, "");
            return input;
        }

        /// <summary>
        /// Escapes special characters that might be interpreted by AI models.
        /// </summary>
        private static string EscapeSpecialCharacters(string input)
        {
            // Escape characters that might trigger special behavior in AI models
            input = input.Replace("\\", "\\\\");
            input = input.Replace("\"", "\\\"");
            input = input.Replace("\n", " ");
            input = input.Replace("\r", " ");
            input = input.Replace("\t", " ");
            
            // Remove any control characters
            input = Regex.Replace(input, @"[\x00-\x1F\x7F]", "");
            
            return input;
        }

        /// <summary>
        /// Checks if URL points to local or private network.
        /// </summary>
        private static bool IsLocalOrPrivateUrl(Uri uri)
        {
            var host = uri.Host.ToLower();
            
            // Check for localhost
            if (host == "localhost" || host == "127.0.0.1" || host == "::1")
            {
                return true;
            }

            // Check for private IP ranges
            if (Regex.IsMatch(host, @"^10\..*") ||
                Regex.IsMatch(host, @"^172\.(1[6-9]|2[0-9]|3[0-1])\..*") ||
                Regex.IsMatch(host, @"^192\.168\..*") ||
                Regex.IsMatch(host, @"^169\.254\..*"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sanitizes a string for safe inclusion in JSON.
        /// </summary>
        public static string SanitizeForJson(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            // Escape JSON special characters
            input = input.Replace("\\", "\\\\");
            input = input.Replace("\"", "\\\"");
            input = input.Replace("/", "\\/");
            input = input.Replace("\b", "\\b");
            input = input.Replace("\f", "\\f");
            input = input.Replace("\n", "\\n");
            input = input.Replace("\r", "\\r");
            input = input.Replace("\t", "\\t");

            return input;
        }
    }
}