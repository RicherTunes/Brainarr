using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service for sanitizing and validating AI recommendations.
    /// Implements security best practices to prevent injection attacks.
    /// </summary>
    public class RecommendationSanitizer : IRecommendationSanitizer
    {
        private readonly Logger _logger;
        
        // Security-focused length limits (more restrictive)
        private const int MaxArtistLength = 100;
        private const int MaxAlbumLength = 150;
        private const int MaxGenreLength = 50;
        private const int MaxReasonLength = 300;
        private const int MaxRecommendations = 100;
        
        // Enhanced patterns for security detection
        private static readonly Regex SqlInjectionPattern = new Regex(
            @"(\b(DELETE|DROP|EXEC(UTE)?|INSERT|SELECT|UNION|UPDATE|ALTER|CREATE|TRUNCATE)\b)|(--)|(/\*)|(\*/)|(')|(\bOR\b\s+\d+\s*=\s*\d+)|(\bAND\b\s+\d+\s*=\s*\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex XssPattern = new Regex(
            @"<[^>]*(script|iframe|object|embed|form|input|button|img|svg|on\w+\s*=)[^>]*>|javascript:|data:text/html",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex PathTraversalPattern = new Regex(
            @"(\.\./|\.\.\\|%2e%2e|%252e%252e|%c0%ae|%c1%9c)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex NullBytePattern = new Regex(
            @"(\x00|%00|\\0|\\x00)",
            RegexOptions.Compiled);
        
        private static readonly Regex CommandInjectionPattern = new Regex(
            @"[;&|`$()]|\b(wget|curl|nc|netcat|bash|sh|cmd|powershell)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex LdapInjectionPattern = new Regex(
            @"[*()\\|&=]|(\|\|)|(&&)",
            RegexOptions.Compiled);

        public RecommendationSanitizer(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Sanitizes a list of recommendations to remove potentially malicious content.
        /// </summary>
        public List<Recommendation> SanitizeRecommendations(List<Recommendation> recommendations)
        {
            if (recommendations == null)
                return new List<Recommendation>();

            // Limit the number of recommendations to process
            if (recommendations.Count > MaxRecommendations)
            {
                _logger.Warn($"Received {recommendations.Count} recommendations, limiting to {MaxRecommendations}");
                recommendations = recommendations.Take(MaxRecommendations).ToList();
            }

            var sanitized = new List<Recommendation>();
            
            foreach (var rec in recommendations)
            {
                if (ValidateRecommendation(rec))
                {
                    var sanitizedRec = new Recommendation
                    {
                        Artist = TruncateAndSanitize(rec.Artist, MaxArtistLength),
                        Album = TruncateAndSanitize(rec.Album, MaxAlbumLength),
                        Genre = TruncateAndSanitize(rec.Genre, MaxGenreLength),
                        Confidence = Math.Max(0.0, Math.Min(1.0, rec.Confidence)), // Clamp to valid range
                        Reason = TruncateAndSanitize(rec.Reason, MaxReasonLength)
                    };
                    
                    sanitized.Add(sanitizedRec);
                }
                else
                {
                    _logger.Warn($"Filtered potentially malicious recommendation: {rec?.Artist} - {rec?.Album}");
                }
            }

            return sanitized;
        }

        /// <summary>
        /// Validates a single recommendation for safety and correctness.
        /// </summary>
        public bool IsValidRecommendation(Recommendation recommendation)
        {
            return ValidateRecommendation(recommendation);
        }

        /// <summary>
        /// Static method to validate a recommendation (can be used without instance)
        /// </summary>
        public static bool ValidateRecommendation(Recommendation recommendation)
        {
            if (recommendation == null)
                return false;

            // Check for required fields
            if (string.IsNullOrWhiteSpace(recommendation.Artist) || 
                string.IsNullOrWhiteSpace(recommendation.Album))
                return false;

            // Check for malicious patterns
            if (ContainsMaliciousPattern(recommendation.Artist) ||
                ContainsMaliciousPattern(recommendation.Album) ||
                ContainsMaliciousPattern(recommendation.Genre) ||
                ContainsMaliciousPattern(recommendation.Reason))
            {
                return false;
            }

            // Validate confidence range
            if (recommendation.Confidence < 0.0 || recommendation.Confidence > 1.0)
            {
                return false;
            }

            // Check for reasonable string lengths (using stricter limits)
            if (recommendation.Artist.Length > MaxArtistLength || 
                recommendation.Album.Length > MaxAlbumLength ||
                (recommendation.Genre?.Length ?? 0) > MaxGenreLength ||
                (recommendation.Reason?.Length ?? 0) > MaxReasonLength)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Truncates and sanitizes a string to a maximum length
        /// </summary>
        private string TruncateAndSanitize(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // First sanitize
            var sanitized = SanitizeString(input);
            
            // Then truncate if needed
            if (sanitized.Length > maxLength)
            {
                _logger.Debug($"Truncating string from {sanitized.Length} to {maxLength} characters");
                sanitized = sanitized.Substring(0, maxLength);
            }
            
            return sanitized;
        }

        /// <summary>
        /// Sanitizes a single string value to remove dangerous content.
        /// </summary>
        public string SanitizeString(string input)
        {
            return SanitizeInput(input);
        }

        /// <summary>
        /// Static method to sanitize input (can be used without instance)
        /// </summary>
        public static string SanitizeInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Remove null bytes first
            var sanitized = NullBytePattern.Replace(input, string.Empty);
            
            // Remove control characters
            sanitized = Regex.Replace(sanitized, @"[\x00-\x1F\x7F]", string.Empty);
            
            // Remove potential SQL injection patterns
            if (SqlInjectionPattern.IsMatch(sanitized))
            {
                // Instead of removing, replace with safe alternatives
                sanitized = Regex.Replace(sanitized, @"\b(DELETE|DROP|EXEC(UTE)?|INSERT|SELECT|UNION|UPDATE)\b", 
                    m => m.Value.ToLower(), RegexOptions.IgnoreCase);
                sanitized = sanitized.Replace("--", "").Replace("/*", "").Replace("*/", "");
            }
            
            // Remove potential XSS patterns
            if (XssPattern.IsMatch(sanitized))
            {
                sanitized = XssPattern.Replace(sanitized, string.Empty);
            }
            
            // Remove path traversal attempts
            if (PathTraversalPattern.IsMatch(sanitized))
            {
                sanitized = PathTraversalPattern.Replace(sanitized, string.Empty);
            }
            
            // Remove command injection patterns
            if (CommandInjectionPattern.IsMatch(sanitized))
            {
                sanitized = CommandInjectionPattern.Replace(sanitized, string.Empty);
            }
            
            // Clean up quotes and special characters
            sanitized = sanitized
                .Replace("\"", "&#34;")
                .Replace("'", "&#39;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("&", "&amp;")
                .Replace("\r", "")
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Trim();
            
            // Normalize whitespace
            sanitized = Regex.Replace(sanitized, @"\s+", " ");
            
            return sanitized;
        }

        /// <summary>
        /// Checks if a string contains SQL injection patterns
        /// </summary>
        public static bool ContainsSqlInjection(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;
            
            return SqlInjectionPattern.IsMatch(input);
        }

        private static bool ContainsMaliciousPattern(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            return SqlInjectionPattern.IsMatch(input) ||
                   XssPattern.IsMatch(input) ||
                   PathTraversalPattern.IsMatch(input) ||
                   NullBytePattern.IsMatch(input) ||
                   CommandInjectionPattern.IsMatch(input) ||
                   LdapInjectionPattern.IsMatch(input);
        }
    }
}