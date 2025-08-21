using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Brainarr.Plugin.Models.Responses.Base;

namespace Brainarr.Plugin.Models.Responses.Groq
{
    /// <summary>
    /// Groq AI response format (OpenAI-compatible)
    /// </summary>
    public class GroqResponse : IProviderResponse
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
        public List<GroqChoice> Choices { get; set; }

        [JsonPropertyName("usage")]
        public GroqUsage Usage { get; set; }

        [JsonPropertyName("x_groq")]
        public GroqMetadata XGroq { get; set; }

        public string GetContent()
        {
            return Choices?.Count > 0 ? Choices[0].Message?.Content : null;
        }

        public bool IsSuccessful()
        {
            return Choices?.Count > 0 && !string.IsNullOrEmpty(GetContent());
        }

        public int? GetTokenUsage()
        {
            return Usage?.TotalTokens;
        }
    }

    public class GroqChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public GroqMessage Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; }

        [JsonPropertyName("logprobs")]
        public object Logprobs { get; set; }
    }

    public class GroqMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
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

    public class GroqMetadata
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    /// <summary>
    /// Groq request format
    /// </summary>
    public class GroqRequest
    {
        [JsonPropertyName("messages")]
        public List<GroqMessage> Messages { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("response_format")]
        public ResponseFormat ResponseFormat { get; set; }

        public class ResponseFormat
        {
            [JsonPropertyName("type")]
            public string Type { get; set; }
        }
    }
}