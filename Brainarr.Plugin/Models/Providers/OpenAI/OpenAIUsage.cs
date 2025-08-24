using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Providers.OpenAI
{
    /// <summary>
    /// Token usage statistics from OpenAI API
    /// </summary>
    public class OpenAIUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }

        /// <summary>
        /// Estimates cost based on model pricing (GPT-4 rates)
        /// </summary>
        public decimal EstimatedCost(string model = "gpt-4")
        {
            // Simplified cost calculation - should be externalized to config
            var promptCost = model.Contains("gpt-4") ? 0.03m : 0.002m;
            var completionCost = model.Contains("gpt-4") ? 0.06m : 0.002m;
            
            return (PromptTokens * promptCost / 1000) + 
                   (CompletionTokens * completionCost / 1000);
        }
    }
}