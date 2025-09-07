using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Anthropic
{
    /// <summary>
    /// Anthropic Claude response format for messages API
    /// </summary>
    public class AnthropicResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public List<AnthropicContent> Content { get; set; } = new List<AnthropicContent>();

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("stop_reason")]
        public string StopReason { get; set; } = string.Empty;

        [JsonPropertyName("stop_sequence")]
        public string StopSequence { get; set; } = string.Empty;

        [JsonPropertyName("usage")]
        public AnthropicUsage Usage { get; set; } = new AnthropicUsage();

        /// <summary>
        /// Gets the combined text content from all content blocks
        /// </summary>
        public string GetContent()
        {
            if (Content == null || Content.Count == 0)
                return string.Empty;

            return string.Join("\n", Content
                .Where(c => c.Type == "text")
                .Select(c => c.Text));
        }

        /// <summary>
        /// Checks if the response was completed successfully
        /// </summary>
        public bool IsComplete()
        {
            return StopReason == "end_turn" || StopReason == "stop_sequence";
        }
    }

    public class AnthropicContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    public class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }

        /// <summary>
        /// Calculate total tokens for consistency with other providers
        /// </summary>
        public int GetTotalTokens()
        {
            return InputTokens + OutputTokens;
        }
    }

    /// <summary>
    /// Error response from Anthropic API
    /// </summary>
    public class AnthropicError
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}
