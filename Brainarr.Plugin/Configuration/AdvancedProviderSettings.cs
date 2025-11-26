using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    /// <summary>
    /// Advanced provider settings for fine-tuning AI behavior (v1.1 feature).
    /// Activated through feature flag in main settings.
    /// </summary>
    public class AdvancedProviderSettings
    {
        /// <summary>
        /// Temperature controls randomness (0.0 = deterministic, 1.0 = creative)
        /// </summary>
        public double Temperature { get; set; } = 0.7;

        /// <summary>
        /// Top-P nucleus sampling (0.0-1.0, controls diversity)
        /// </summary>
        public double TopP { get; set; } = 0.9;

        /// <summary>
        /// Maximum response tokens (affects response length)
        /// </summary>
        public int MaxTokens { get; set; } = 2000;

        /// <summary>
        /// Request timeout in seconds
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Custom headers for provider requests
        /// </summary>
        public Dictionary<string, string> CustomHeaders { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Whether to use streaming responses (when supported)
        /// </summary>
        public bool UseStreaming { get; set; } = false;

        /// <summary>
        /// Gets advanced settings as a dictionary for provider integration.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["temperature"] = Temperature,
                ["topP"] = TopP,
                ["maxTokens"] = MaxTokens,
                ["timeout"] = TimeoutSeconds,
                ["useStreaming"] = UseStreaming
            };

            // Add custom headers if present
            if (CustomHeaders?.Count > 0)
            {
                dict["customHeaders"] = CustomHeaders;
            }

            return dict;
        }

        /// <summary>
        /// Gets provider-specific defaults for optimal performance.
        /// </summary>
        public static AdvancedProviderSettings GetDefaults(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => new AdvancedProviderSettings
                {
                    Temperature = 0.7,
                    TopP = 0.9,
                    MaxTokens = 2000,
                    TimeoutSeconds = 60, // Longer timeout for local models
                    UseStreaming = false
                },
                AIProvider.LMStudio => new AdvancedProviderSettings
                {
                    Temperature = 0.7,
                    TopP = 0.9,
                    MaxTokens = 2000,
                    TimeoutSeconds = 60,
                    UseStreaming = false
                },
                AIProvider.OpenAI => new AdvancedProviderSettings
                {
                    Temperature = 0.7,
                    TopP = 0.9,
                    MaxTokens = 1500, // OpenAI models are efficient
                    TimeoutSeconds = 30,
                    UseStreaming = false
                },
                AIProvider.Anthropic => new AdvancedProviderSettings
                {
                    Temperature = 0.7,
                    TopP = 0.9,
                    MaxTokens = 1500,
                    TimeoutSeconds = 30,
                    UseStreaming = false
                },
                AIProvider.Perplexity => new AdvancedProviderSettings
                {
                    Temperature = 0.6, // Perplexity works well with lower temperature
                    TopP = 0.8,
                    MaxTokens = 1000,
                    TimeoutSeconds = 25,
                    UseStreaming = false
                },
                AIProvider.Groq => new AdvancedProviderSettings
                {
                    Temperature = 0.7,
                    TopP = 0.9,
                    MaxTokens = 2000, // Groq is fast, can handle more tokens
                    TimeoutSeconds = 20, // Very fast inference
                    UseStreaming = false
                },
                AIProvider.DeepSeek => new AdvancedProviderSettings
                {
                    Temperature = 0.7,
                    TopP = 0.9,
                    MaxTokens = 1500,
                    TimeoutSeconds = 30,
                    UseStreaming = false
                },
                AIProvider.Gemini => new AdvancedProviderSettings
                {
                    Temperature = 0.7,
                    TopP = 0.9,
                    MaxTokens = 1500,
                    TimeoutSeconds = 30,
                    UseStreaming = false
                },
                AIProvider.OpenRouter => new AdvancedProviderSettings
                {
                    Temperature = 0.7,
                    TopP = 0.9,
                    MaxTokens = 1500,
                    TimeoutSeconds = 45, // Gateway may be slower
                    UseStreaming = false
                },
                _ => new AdvancedProviderSettings()
            };
        }

        /// <summary>
        /// Validates the advanced settings for the given provider.
        /// </summary>
        public List<string> Validate(AIProvider provider)
        {
            var errors = new List<string>();

            if (Temperature < 0.0 || Temperature > 2.0)
                errors.Add("Temperature must be between 0.0 and 2.0");

            if (TopP < 0.0 || TopP > 1.0)
                errors.Add("TopP must be between 0.0 and 1.0");

            if (MaxTokens < 100 || MaxTokens > 10000)
                errors.Add("MaxTokens must be between 100 and 10000");

            if (TimeoutSeconds < BrainarrConstants.MinAITimeout || TimeoutSeconds > BrainarrConstants.MaxAITimeout)
                errors.Add($"Timeout must be between {BrainarrConstants.MinAITimeout} and {BrainarrConstants.MaxAITimeout} seconds");

            // Provider-specific validations
            switch (provider)
            {
                case AIProvider.Ollama:
                case AIProvider.LMStudio:
                    if (TimeoutSeconds < 30)
                        errors.Add($"Local models typically need at least 30 seconds timeout");
                    break;

                case AIProvider.Groq:
                    if (TimeoutSeconds > 30)
                        errors.Add("Groq is very fast - timeout over 30 seconds is unnecessary");
                    break;
            }

            return errors;
        }
    }
}
