using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Brainarr.Plugin.Models.Responses.Base;

namespace Brainarr.Plugin.Models.Responses.OpenAI
{
    /// <summary>
    /// OpenAI/GPT response format
    /// </summary>
    public class OpenAIResponse : IProviderResponse
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

        [JsonPropertyName("function_call")]
        public OpenAIFunctionCall FunctionCall { get; set; }
    }

    public class OpenAIFunctionCall
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("arguments")]
        public string Arguments { get; set; }
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
    /// OpenAI request format
    /// </summary>
    public class OpenAIRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("messages")]
        public List<OpenAIMessage> Messages { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("response_format")]
        public ResponseFormat ResponseFormat { get; set; }

        public class ResponseFormat
        {
            [JsonPropertyName("type")]
            public string Type { get; set; }
        }
    }
}