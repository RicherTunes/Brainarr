using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service interface for sanitizing and validating AI recommendations.
    /// Prevents security issues like SQL injection and XSS attacks.
    /// </summary>
    public interface IRecommendationSanitizer
    {
        /// <summary>
        /// Sanitizes a list of recommendations to remove potentially malicious content.
        /// </summary>
        /// <param name="recommendations">Raw recommendations from AI provider</param>
        /// <returns>Sanitized recommendations safe for storage and display</returns>
        List<Recommendation> SanitizeRecommendations(List<Recommendation> recommendations);

        /// <summary>
        /// Validates a single recommendation for safety and correctness.
        /// </summary>
        /// <param name="recommendation">Recommendation to validate</param>
        /// <returns>True if the recommendation is valid and safe</returns>
        bool IsValidRecommendation(Recommendation recommendation);

        /// <summary>
        /// Sanitizes a single string value to remove dangerous content.
        /// </summary>
        /// <param name="input">Input string to sanitize</param>
        /// <returns>Sanitized string safe for use</returns>
        string SanitizeString(string input);
    }
}