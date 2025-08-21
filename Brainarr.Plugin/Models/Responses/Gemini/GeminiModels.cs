using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Brainarr.Plugin.Models.Responses.Base;

namespace Brainarr.Plugin.Models.Responses.Gemini
{
    /// <summary>
    /// Google Gemini response format
    /// </summary>
    public class GeminiResponse : IProviderResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate> Candidates { get; set; }

        [JsonPropertyName("promptFeedback")]
        public GeminiPromptFeedback PromptFeedback { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata UsageMetadata { get; set; }

        public string GetContent()
        {
            if (Candidates?.Count > 0)
            {
                var content = Candidates[0].Content;
                if (content?.Parts?.Count > 0)
                {
                    return content.Parts[0].Text;
                }
            }
            return null;
        }

        public bool IsSuccessful()
        {
            return Candidates?.Count > 0 && !string.IsNullOrEmpty(GetContent());
        }

        public int? GetTokenUsage()
        {
            return UsageMetadata?.TotalTokenCount;
        }
    }

    public class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent Content { get; set; }

        [JsonPropertyName("finishReason")]
        public string FinishReason { get; set; }

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("safetyRatings")]
        public List<GeminiSafetyRating> SafetyRatings { get; set; }
    }

    public class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; }

        [JsonPropertyName("role")]
        public string Role { get; set; }
    }

    public class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class GeminiSafetyRating
    {
        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("probability")]
        public string Probability { get; set; }
    }

    public class GeminiPromptFeedback
    {
        [JsonPropertyName("safetyRatings")]
        public List<GeminiSafetyRating> SafetyRatings { get; set; }
    }

    public class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }

    /// <summary>
    /// Gemini request format
    /// </summary>
    public class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; }

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig GenerationConfig { get; set; }

        [JsonPropertyName("safetySettings")]
        public List<GeminiSafetySettings> SafetySettings { get; set; }
    }

    public class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("topK")]
        public int TopK { get; set; }

        [JsonPropertyName("topP")]
        public double TopP { get; set; }

        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }

        [JsonPropertyName("stopSequences")]
        public List<string> StopSequences { get; set; }
    }

    public class GeminiSafetySettings
    {
        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("threshold")]
        public string Threshold { get; set; }
    }
}