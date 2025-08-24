using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Providers.OpenAI
{
    /// <summary>
    /// OpenAI API response structure for chat completions
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

        /// <summary>
        /// Checks if the response contains valid completion data
        /// </summary>
        public bool HasValidContent()
        {
            return Choices?.Count > 0 && 
                   !string.IsNullOrWhiteSpace(Choices[0]?.Message?.Content);
        }

        /// <summary>
        /// Gets the first completion content or null
        /// </summary>
        public string GetContent()
        {
            return Choices?.FirstOrDefault()?.Message?.Content;
        }
    }
}