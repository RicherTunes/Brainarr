using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI
{
    /// <summary>
    /// OpenAI/GPT response format for chat completions API
    /// </summary>
    public class OpenAIResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("object")]
        public string Object { get; set; } = string.Empty;

        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("choices")]
        public List<OpenAIChoice> Choices { get; set; } = new List<OpenAIChoice>();

        [JsonPropertyName("usage")]
        public OpenAIUsage Usage { get; set; } = new OpenAIUsage();

        [JsonPropertyName("system_fingerprint")]
        public string SystemFingerprint { get; set; } = string.Empty;

        /// <summary>
        /// Gets the primary response content from choices
        /// </summary>
        public string GetContent()
        {
            return Choices?.Count > 0 ? Choices[0].Message?.Content ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// Checks if the response was completed successfully
        /// </summary>
        public bool IsComplete()
        {
            return Choices?.Count > 0 &&
                   Choices[0].FinishReason == "stop";
        }
    }

    public class OpenAIChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public OpenAIMessage Message { get; set; } = new OpenAIMessage();

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; } = string.Empty;

        [JsonPropertyName("logprobs")]
        public object Logprobs { get; set; }
    }

    public class OpenAIMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("refusal")]
        public string Refusal { get; set; } = string.Empty;
    }

    public class OpenAIUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }

        [JsonPropertyName("prompt_tokens_details")]
        public OpenAITokenDetails PromptTokensDetails { get; set; }

        [JsonPropertyName("completion_tokens_details")]
        public OpenAITokenDetails CompletionTokensDetails { get; set; }
    }

    public class OpenAITokenDetails
    {
        [JsonPropertyName("cached_tokens")]
        public int CachedTokens { get; set; }

        [JsonPropertyName("reasoning_tokens")]
        public int ReasoningTokens { get; set; }
    }
}
