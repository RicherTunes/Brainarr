using System.Text.Json.Serialization;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Groq
{
    /// <summary>
    /// Groq response format - uses OpenAI-compatible API with extensions
    /// </summary>
    public class GroqResponse : OpenAIResponse
    {
        // Groq implements OpenAI-compatible API format
        // All base fields inherited from OpenAIResponse

        /// <summary>
        /// Groq-specific performance metrics
        /// </summary>
        [JsonPropertyName("x_groq")]
        public GroqMetadata XGroq { get; set; }
    }

    /// <summary>
    /// Groq-specific performance and routing metadata
    /// </summary>
    public class GroqMetadata
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("usage")]
        public GroqUsageMetadata Usage { get; set; }

        [JsonPropertyName("performance")]
        public GroqPerformance Performance { get; set; }
    }

    public class GroqUsageMetadata
    {
        [JsonPropertyName("queue_time")]
        public double QueueTime { get; set; }

        [JsonPropertyName("prompt_time")]
        public double PromptTime { get; set; }

        [JsonPropertyName("completion_time")]
        public double CompletionTime { get; set; }

        [JsonPropertyName("total_time")]
        public double TotalTime { get; set; }
    }

    public class GroqPerformance
    {
        [JsonPropertyName("tokens_per_second")]
        public double TokensPerSecond { get; set; }

        [JsonPropertyName("time_to_first_token")]
        public double TimeToFirstToken { get; set; }

        [JsonPropertyName("hardware_utilized")]
        public string HardwareUtilized { get; set; } = string.Empty;
    }
}
