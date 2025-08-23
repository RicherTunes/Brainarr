using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Providers
{
    /// <summary>
    /// Groq response format models (OpenAI-compatible with extensions)
    /// </summary>
    public class GroqResponse : OpenAIResponse
    {
        [JsonPropertyName("x_groq")]
        public GroqMetadata XGroq { get; set; }
    }

    public class GroqMetadata
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("usage")]
        public GroqUsage Usage { get; set; }
    }

    public class GroqUsage
    {
        [JsonPropertyName("queue_time")]
        public double QueueTime { get; set; }

        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("prompt_time")]
        public double PromptTime { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("completion_time")]
        public double CompletionTime { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }

        [JsonPropertyName("total_time")]
        public double TotalTime { get; set; }
    }
}