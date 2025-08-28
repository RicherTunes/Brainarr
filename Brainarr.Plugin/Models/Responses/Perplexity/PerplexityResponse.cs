using System.Collections.Generic;
using System.Text.Json.Serialization;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Perplexity
{
    /// <summary>
    /// Perplexity response format - uses OpenAI-compatible API with search extensions
    /// </summary>
    public class PerplexityResponse : OpenAIResponse
    {
        // Perplexity implements OpenAI-compatible API format
        // All base fields inherited from OpenAIResponse

        /// <summary>
        /// Perplexity-specific search citations and sources
        /// </summary>
        [JsonPropertyName("citations")]
        public List<PerplexityCitation> Citations { get; set; } = new List<PerplexityCitation>();

        [JsonPropertyName("search_results")]
        public List<PerplexitySearchResult> SearchResults { get; set; } = new List<PerplexitySearchResult>();
    }

    /// <summary>
    /// Citation information for web sources used by Perplexity
    /// </summary>
    public class PerplexityCitation
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("snippet")]
        public string Snippet { get; set; } = string.Empty;

        [JsonPropertyName("index")]
        public int Index { get; set; }
    }

    /// <summary>
    /// Web search results used to generate the response
    /// </summary>
    public class PerplexitySearchResult
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("published_date")]
        public string PublishedDate { get; set; } = string.Empty;
    }
}