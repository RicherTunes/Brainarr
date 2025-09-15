using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing
{
    /// <summary>
    /// Defines the contract for parsing AI responses into recommendations.
    /// </summary>
    public interface IRecommendationParser
    {
        /// <summary>
        /// Parses an AI response string into a list of recommendations.
        /// </summary>
        /// <param name="response">The raw response from the AI provider.</param>
        /// <returns>A list of parsed and validated recommendations.</returns>
        List<Recommendation> ParseRecommendations(string response);

        /// <summary>
        /// Attempts to parse JSON formatted recommendations.
        /// </summary>
        /// <param name="jsonString">The JSON string to parse.</param>
        /// <param name="recommendations">The parsed recommendations if successful.</param>
        /// <returns>True if parsing was successful; otherwise, false.</returns>
        bool TryParseJson(string jsonString, out List<Recommendation> recommendations);

        /// <summary>
        /// Parses text-based recommendations using pattern matching.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <returns>A list of parsed recommendations.</returns>
        List<Recommendation> ParseTextFallback(string text);
    }
}
