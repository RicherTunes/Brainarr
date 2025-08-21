using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Brainarr.Plugin.Models.Responses.Base;

namespace Brainarr.Plugin.Models.Responses.Anthropic
{
    /// <summary>
    /// Anthropic Claude response format
    /// </summary>
    public class AnthropicResponse : IProviderResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public List<AnthropicContent> Content { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("stop_reason")]
        public string StopReason { get; set; }

        [JsonPropertyName("stop_sequence")]
        public string StopSequence { get; set; }

        [JsonPropertyName("usage")]
        public AnthropicUsage Usage { get; set; }

        public string GetContent()
        {
            if (Content?.Count > 0)
            {
                foreach (var item in Content)
                {
                    if (item.Type == "text")
                        return item.Text;
                }
            }
            return null;
        }

        public bool IsSuccessful()
        {
            return Content?.Count > 0 && !string.IsNullOrEmpty(GetContent());
        }

        public int? GetTokenUsage()
        {
            return Usage?.InputTokens + Usage?.OutputTokens;
        }
    }

    public class AnthropicContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }

    /// <summary>
    /// Anthropic request format
    /// </summary>
    public class AnthropicRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("messages")]
        public List<AnthropicMessage> Messages { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("system")]
        public string System { get; set; }
    }

    public class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
    }
}