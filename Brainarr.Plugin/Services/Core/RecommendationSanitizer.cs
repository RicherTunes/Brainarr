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
        
        // Patterns that indicate potential security issues
        private static readonly Regex SqlInjectionPattern = new Regex(
            @"(\b(DELETE|DROP|EXEC(UTE)?|INSERT|SELECT|UNION|UPDATE)\b)|(--)|(/\*)|(\*/)|(')",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex XssPattern = new Regex(
            @"<[^>]*(script|iframe|object|embed|form|input|button|img|svg|on\w+\s*=)[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex PathTraversalPattern = new Regex(
            @"(\.\./|\.\.\\|%2e%2e|%252e%252e)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex NullBytePattern = new Regex(
            @"(\x00|%00|\\0)",
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
                if (IsValidRecommendation(rec))
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
            
            // Clean up quotes and special characters using StringBuilder for efficiency
            var sb = new System.Text.StringBuilder(sanitized.Length);
            foreach (char c in sanitized)
            {
                switch (c)
                {
                    case '"':
                        // Remove double quotes
                        break;
                    case '\'':
                        sb.Append('''); // Replace with proper apostrophe
                        break;
                    case '<':
                    case '>':
                        // Remove angle brackets
                        break;
                    case '&':
                        sb.Append("&amp;");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            sanitized = sb.ToString().Trim();
            
            // Remove any control characters
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
                   NullBytePattern.IsMatch(input);
        }
    }
}