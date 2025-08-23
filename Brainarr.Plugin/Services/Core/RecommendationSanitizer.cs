using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
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
        
        // Patterns that indicate potential security issues
        private static readonly Regex SqlInjectionPattern = new Regex(
            @"(\b(DELETE|DROP|EXEC(UTE)?|INSERT|SELECT|UNION|UPDATE)\b)|(--)|(/\*)|(\*/)|(\';)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex XssPattern = new Regex(
            @"<(script|iframe|object|embed|form|input|button)[^>]*>.*?</\1>|<[^>]*(img|svg|on\w+\s*=)[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
        
        private static readonly Regex PathTraversalPattern = new Regex(
            @"(\.\./|\.\.\\|%2e%2e|%252e%252e)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex DangerousPathPattern = new Regex(
            @"(etc/passwd|System32|Windows\\System32|config)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex NullBytePattern = new Regex(
            @"(\x00|%00|\\0)",
            RegexOptions.Compiled);
        
        private static readonly Regex HtmlTagPattern = new Regex(
            @"<[^>]*>",
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

            var sanitized = new List<Recommendation>();
            
            foreach (var rec in recommendations)
            {
                // Check basic validity (not including confidence range)
                if (IsBasicallyValid(rec))
                {
                    var sanitizedRec = new Recommendation
                    {
                        Artist = SanitizeString(rec.Artist),
                        Album = SanitizeString(rec.Album),
                        Genre = SanitizeString(rec.Genre),
                        Confidence = Math.Max(0.0, Math.Min(1.0, rec.Confidence)), // Clamp to valid range
                        Reason = SanitizeString(rec.Reason)
                    };
                    
                    sanitized.Add(sanitizedRec);
                }
                else
                {
                    _logger.Warn($"Filtered potentially malicious recommendation: {rec.Artist} - {rec.Album}");
                }
            }

            return sanitized;
        }

        /// <summary>
        /// Validates a single recommendation for safety and correctness.
        /// </summary>
        public bool IsValidRecommendation(Recommendation recommendation)
        {
            if (recommendation == null)
                return false;

            // Check for required fields - artist is always required
            // Album can be empty for artist-only recommendations
            if (string.IsNullOrWhiteSpace(recommendation.Artist))
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
                _logger.Debug($"Invalid confidence value: {recommendation.Confidence}");
                return false;
            }

            // Check for reasonable string lengths
            if (recommendation.Artist.Length > 500 || 
                recommendation.Album.Length > 500 ||
                (recommendation.Genre?.Length ?? 0) > 100 ||
                (recommendation.Reason?.Length ?? 0) > 1000)
            {
                _logger.Debug("Recommendation field exceeds maximum length");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates a recommendation for basic safety without strict confidence validation.
        /// Used for sanitization where confidence will be clamped.
        /// </summary>
        private bool IsBasicallyValid(Recommendation recommendation)
        {
            if (recommendation == null)
                return false;

            // Check for required fields - artist is always required
            // Album can be empty for artist-only recommendations
            if (string.IsNullOrWhiteSpace(recommendation.Artist))
                return false;

            // Check for malicious patterns
            if (ContainsMaliciousPattern(recommendation.Artist) ||
                ContainsMaliciousPattern(recommendation.Album) ||
                ContainsMaliciousPattern(recommendation.Genre) ||
                ContainsMaliciousPattern(recommendation.Reason))
            {
                return false;
            }

            // Check for reasonable string lengths
            if (recommendation.Artist.Length > 500 || 
                recommendation.Album.Length > 500 ||
                (recommendation.Genre?.Length ?? 0) > 100 ||
                (recommendation.Reason?.Length ?? 0) > 1000)
            {
                _logger.Debug("Recommendation field exceeds maximum length");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Sanitizes a single string value to remove dangerous content.
        /// </summary>
        public string SanitizeString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Remove null bytes
            var sanitized = NullBytePattern.Replace(input, string.Empty);
            
            // Remove potential SQL injection patterns
            if (SqlInjectionPattern.IsMatch(sanitized))
            {
                sanitized = SqlInjectionPattern.Replace(sanitized, string.Empty);
            }
            
            // Remove potential XSS patterns (malicious scripts)
            if (XssPattern.IsMatch(sanitized))
            {
                sanitized = XssPattern.Replace(sanitized, string.Empty);
            }
            
            // Remove path traversal attempts
            if (PathTraversalPattern.IsMatch(sanitized))
            {
                sanitized = PathTraversalPattern.Replace(sanitized, string.Empty);
            }
            
            // Remove dangerous path components
            if (DangerousPathPattern.IsMatch(sanitized))
            {
                sanitized = DangerousPathPattern.Replace(sanitized, string.Empty);
            }
            
            // Remove HTML tags but preserve content inside them
            sanitized = HtmlTagPattern.Replace(sanitized, string.Empty);
            
            // Clean up quotes and special characters
            sanitized = sanitized
                .Replace("\"", "") // Remove double quotes
                .Replace("&", "&amp;") // Encode ampersands
                .Trim();
            
            // Remove any control characters but preserve normal apostrophes
            sanitized = Regex.Replace(sanitized, @"[\x00-\x1F\x7F]", string.Empty);
            
            return sanitized;
        }

        private bool ContainsMaliciousPattern(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            return SqlInjectionPattern.IsMatch(input) ||
                   XssPattern.IsMatch(input) ||
                   PathTraversalPattern.IsMatch(input) ||
                   DangerousPathPattern.IsMatch(input) ||
                   NullBytePattern.IsMatch(input);
        }
    }
}