using System.Collections.Generic;
using System.Text.Json.Serialization;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenRouter
{
    /// <summary>
    /// OpenRouter response format - aggregates multiple AI providers
    /// </summary>
    public class OpenRouterResponse : OpenAIResponse
    {
        // OpenRouter implements OpenAI-compatible API format
        // All base fields inherited from OpenAIResponse

        /// <summary>
        /// OpenRouter-specific metadata about routing and provider
        /// </summary>
        [JsonPropertyName("x_openrouter")]
        public OpenRouterMetadata XOpenRouter { get; set; }
    }

    /// <summary>
    /// OpenRouter specific routing and billing metadata
    /// </summary>
    public class OpenRouterMetadata
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("usage")]
        public OpenRouterUsage Usage { get; set; }

        [JsonPropertyName("routing")]
        public OpenRouterRouting Routing { get; set; }
    }

    public class OpenRouterUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }

        [JsonPropertyName("cost")]
        public double Cost { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "USD";
    }

    public class OpenRouterRouting
    {
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("model_id")]
        public string ModelId { get; set; } = string.Empty;

        [JsonPropertyName("fallback_models")]
        public List<string> FallbackModels { get; set; } = new List<string>();

        [JsonPropertyName("routing_time_ms")]
        public int RoutingTimeMs { get; set; }

        [JsonPropertyName("queue_depth")]
        public int QueueDepth { get; set; }
    }

    /// <summary>
    /// Response for listing available models on OpenRouter
    /// </summary>
    public class OpenRouterModelsResponse
    {
        [JsonPropertyName("data")]
        public List<OpenRouterModel> Data { get; set; } = new List<OpenRouterModel>();
    }

    public class OpenRouterModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("context_length")]
        public int ContextLength { get; set; }

        [JsonPropertyName("pricing")]
        public OpenRouterPricing Pricing { get; set; }

        [JsonPropertyName("top_provider")]
        public OpenRouterProvider TopProvider { get; set; }

        [JsonPropertyName("per_request_limits")]
        public OpenRouterLimits PerRequestLimits { get; set; }
    }

    public class OpenRouterPricing
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("completion")]
        public string Completion { get; set; } = string.Empty;

        [JsonPropertyName("request")]
        public string Request { get; set; } = string.Empty;

        [JsonPropertyName("image")]
        public string Image { get; set; } = string.Empty;
    }

    public class OpenRouterProvider
    {
        [JsonPropertyName("max_completion_tokens")]
        public int? MaxCompletionTokens { get; set; }

        [JsonPropertyName("is_moderated")]
        public bool IsModerated { get; set; }
    }

    public class OpenRouterLimits
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }
}
