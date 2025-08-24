using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Providers.OpenAI
{
    /// <summary>
    /// Represents a single completion choice from OpenAI
    /// </summary>
    public class OpenAIChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public OpenAIMessage Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; }

        /// <summary>
        /// Checks if the completion finished normally
        /// </summary>
        public bool IsComplete()
        {
            return FinishReason == "stop" || FinishReason == "length";
        }
    }
}