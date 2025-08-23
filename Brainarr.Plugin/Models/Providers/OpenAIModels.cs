using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Providers
{
    /// <summary>
    /// OpenAI/GPT response format models
    /// </summary>
    public class OpenAIResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("object")]
        public string Object { get; set; }

        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("choices")]
        public List<OpenAIChoice> Choices { get; set; }

        [JsonPropertyName("usage")]
        public OpenAIUsage Usage { get; set; }
    }

    public class OpenAIChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public OpenAIMessage Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; }
    }

    public class OpenAIMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
    }

    public class OpenAIUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    /// <summary>
    /// LM Studio response format (OpenAI-compatible)
    /// </summary>
    public class LMStudioResponse : OpenAIResponse
    {
        // LM Studio uses OpenAI-compatible format
    }

    /// <summary>
    /// Azure OpenAI response format
    /// </summary>
    public class AzureOpenAIResponse : OpenAIResponse
    {
        [JsonPropertyName("system_fingerprint")]
        public string SystemFingerprint { get; set; }
    }
}