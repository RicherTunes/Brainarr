using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Google
{
    /// <summary>
    /// Google Gemini response format for generateContent API
    /// </summary>
    public class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate> Candidates { get; set; } = new List<GeminiCandidate>();

        [JsonPropertyName("promptFeedback")]
        public GeminiPromptFeedback PromptFeedback { get; set; } = new GeminiPromptFeedback();

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata UsageMetadata { get; set; } = new GeminiUsageMetadata();

        /// <summary>
        /// Gets the primary response content from candidates
        /// </summary>
        public string GetContent()
        {
            if (Candidates == null || Candidates.Count == 0)
                return string.Empty;

            var candidate = Candidates[0];
            if (candidate.Content?.Parts == null || candidate.Content.Parts.Count == 0)
                return string.Empty;

            return string.Join("\n", candidate.Content.Parts.Select(p => p.Text));
        }

        /// <summary>
        /// Checks if the response was completed successfully
        /// </summary>
        public bool IsComplete()
        {
            return Candidates?.Count > 0 && 
                   (Candidates[0].FinishReason == "STOP" || 
                    Candidates[0].FinishReason == "MAX_TOKENS");
        }

        /// <summary>
        /// Checks if the prompt was blocked by safety filters
        /// </summary>
        public bool IsBlocked()
        {
            return PromptFeedback?.BlockReason != null;
        }
    }

    public class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent Content { get; set; } = new GeminiContent();

        [JsonPropertyName("finishReason")]
        public string FinishReason { get; set; } = string.Empty;

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("safetyRatings")]
        public List<GeminiSafetyRating> SafetyRatings { get; set; } = new List<GeminiSafetyRating>();

        [JsonPropertyName("citationMetadata")]
        public GeminiCitationMetadata CitationMetadata { get; set; }

        [JsonPropertyName("tokenCount")]
        public int TokenCount { get; set; }
    }

    public class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = new List<GeminiPart>();

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;
    }

    public class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    public class GeminiSafetyRating
    {
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("probability")]
        public string Probability { get; set; } = string.Empty;

        [JsonPropertyName("blocked")]
        public bool Blocked { get; set; }
    }

    public class GeminiPromptFeedback
    {
        [JsonPropertyName("safetyRatings")]
        public List<GeminiSafetyRating> SafetyRatings { get; set; } = new List<GeminiSafetyRating>();

        [JsonPropertyName("blockReason")]
        public string BlockReason { get; set; }
    }

    public class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }

        [JsonPropertyName("cachedContentTokenCount")]
        public int CachedContentTokenCount { get; set; }
    }

    public class GeminiCitationMetadata
    {
        [JsonPropertyName("citations")]
        public List<GeminiCitation> Citations { get; set; } = new List<GeminiCitation>();
    }

    public class GeminiCitation
    {
        [JsonPropertyName("startIndex")]
        public int StartIndex { get; set; }

        [JsonPropertyName("endIndex")]
        public int EndIndex { get; set; }

        [JsonPropertyName("uri")]
        public string Uri { get; set; } = string.Empty;
    }
}