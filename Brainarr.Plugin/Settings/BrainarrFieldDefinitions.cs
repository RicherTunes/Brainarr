using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Settings
{
    /// <summary>
    /// UI field definitions for Brainarr settings.
    /// Manages field metadata, visibility conditions, and UI presentation.
    /// </summary>
    public static class BrainarrFieldDefinitions
    {
        /// <summary>
        /// Applies UI field definitions to the settings instance
        /// </summary>
        public static void ApplyFieldDefinitions(BrainarrSettings settings)
        {
            // This would typically be handled by attributes on the properties
            // but we're separating it for better modularity
        }
        
        /// <summary>
        /// Gets field definition for provider selection
        /// </summary>
        public static FieldDefinition GetProviderField()
        {
            return new FieldDefinition
            {
                Name = "Provider",
                Label = "AI Provider",
                HelpText = "Select your AI provider. Local providers (Ollama, LM Studio) keep your data private.",
                Type = FieldType.Select,
                SelectOptions = GetProviderOptions(),
                Order = 1
            };
        }
        
        /// <summary>
        /// Gets field definition for max recommendations
        /// </summary>
        public static FieldDefinition GetMaxRecommendationsField()
        {
            return new FieldDefinition
            {
                Name = "MaxRecommendations",
                Label = "Max Recommendations",
                HelpText = $"Maximum number of recommendations to generate ({BrainarrConstants.MinRecommendations}-{BrainarrConstants.MaxRecommendations})",
                Type = FieldType.Number,
                Order = 2
            };
        }
        
        /// <summary>
        /// Gets field definition for recommendation mode
        /// </summary>
        public static FieldDefinition GetRecommendationModeField()
        {
            return new FieldDefinition
            {
                Name = "RecommendationMode",
                Label = "Recommendation Mode",
                HelpText = "Choose between recommending specific albums or all albums by recommended artists",
                Type = FieldType.Select,
                SelectOptions = new List<SelectOption>
                {
                    new SelectOption { Value = (int)RecommendationMode.Albums, Name = "Specific Albums" },
                    new SelectOption { Value = (int)RecommendationMode.ArtistsOnly, Name = "All Albums by Artist" }
                },
                Order = 3
            };
        }
        
        /// <summary>
        /// Gets provider-specific URL fields
        /// </summary>
        public static Dictionary<string, FieldDefinition> GetProviderUrlFields()
        {
            return new Dictionary<string, FieldDefinition>
            {
                ["OllamaUrl"] = new FieldDefinition
                {
                    Name = "OllamaUrlRaw",
                    Label = "Ollama URL",
                    HelpText = "URL of your Ollama instance (e.g., http://localhost:11434)",
                    Type = FieldType.Url,
                    Hidden = HiddenType.HiddenIfNotSet,
                    Order = 10
                },
                ["LMStudioUrl"] = new FieldDefinition
                {
                    Name = "LMStudioUrlRaw",
                    Label = "LM Studio URL",
                    HelpText = "URL of your LM Studio server (e.g., http://localhost:1234)",
                    Type = FieldType.Url,
                    Hidden = HiddenType.HiddenIfNotSet,
                    Order = 11
                }
            };
        }
        
        /// <summary>
        /// Gets provider-specific API key fields
        /// </summary>
        public static Dictionary<string, FieldDefinition> GetProviderApiKeyFields()
        {
            return new Dictionary<string, FieldDefinition>
            {
                ["OpenAIApiKey"] = CreateApiKeyField("OpenAIApiKey", "OpenAI API Key", 20),
                ["AnthropicApiKey"] = CreateApiKeyField("AnthropicApiKey", "Anthropic API Key", 21),
                ["GeminiApiKey"] = CreateApiKeyField("GeminiApiKey", "Gemini API Key", 22),
                ["GroqApiKey"] = CreateApiKeyField("GroqApiKey", "Groq API Key", 23),
                ["PerplexityApiKey"] = CreateApiKeyField("PerplexityApiKey", "Perplexity API Key", 24),
                ["DeepSeekApiKey"] = CreateApiKeyField("DeepSeekApiKey", "DeepSeek API Key", 25),
                ["OpenRouterApiKey"] = CreateApiKeyField("OpenRouterApiKey", "OpenRouter API Key", 26)
            };
        }
        
        /// <summary>
        /// Gets provider-specific model selection fields
        /// </summary>
        public static Dictionary<string, FieldDefinition> GetProviderModelFields()
        {
            return new Dictionary<string, FieldDefinition>
            {
                ["OllamaModel"] = CreateModelField("OllamaModel", "Ollama Model", GetOllamaModels(), 30),
                ["LMStudioModel"] = CreateModelField("LMStudioModel", "LM Studio Model", null, 31),
                ["OpenAIModel"] = CreateModelField("OpenAIModel", "OpenAI Model", GetOpenAIModels(), 32),
                ["AnthropicModel"] = CreateModelField("AnthropicModel", "Anthropic Model", GetAnthropicModels(), 33),
                ["GeminiModel"] = CreateModelField("GeminiModel", "Gemini Model", GetGeminiModels(), 34),
                ["GroqModel"] = CreateModelField("GroqModel", "Groq Model", GetGroqModels(), 35),
                ["PerplexityModel"] = CreateModelField("PerplexityModel", "Perplexity Model", GetPerplexityModels(), 36),
                ["DeepSeekModel"] = CreateModelField("DeepSeekModel", "DeepSeek Model", GetDeepSeekModels(), 37),
                ["OpenRouterModel"] = CreateModelField("OpenRouterModel", "OpenRouter Model", null, 38)
            };
        }
        
        /// <summary>
        /// Gets advanced settings fields
        /// </summary>
        public static Dictionary<string, FieldDefinition> GetAdvancedFields()
        {
            return new Dictionary<string, FieldDefinition>
            {
                ["EnableLibraryAnalysis"] = new FieldDefinition
                {
                    Name = "EnableLibraryAnalysis",
                    Label = "Enable Library Analysis",
                    HelpText = "Analyze your library to generate personalized recommendations",
                    Type = FieldType.Checkbox,
                    Section = "Advanced",
                    Order = 40
                },
                ["LibrarySampleSize"] = new FieldDefinition
                {
                    Name = "LibrarySampleSize",
                    Label = "Library Sample Size",
                    HelpText = "Number of artists to analyze for recommendations (10-500)",
                    Type = FieldType.Number,
                    Section = "Advanced",
                    Hidden = HiddenType.HiddenIfNotSet,
                    Order = 41
                },
                ["EnableIterativeRefinement"] = new FieldDefinition
                {
                    Name = "EnableIterativeRefinement",
                    Label = "Enable Iterative Refinement",
                    HelpText = "Use multiple AI iterations to improve recommendation quality",
                    Type = FieldType.Checkbox,
                    Section = "Advanced",
                    Order = 42
                },
                ["MaxIterations"] = new FieldDefinition
                {
                    Name = "MaxIterations",
                    Label = "Max Iterations",
                    HelpText = "Maximum refinement iterations (1-5)",
                    Type = FieldType.Number,
                    Section = "Advanced",
                    Hidden = HiddenType.HiddenIfNotSet,
                    Order = 43
                },
                ["EnableCaching"] = new FieldDefinition
                {
                    Name = "EnableCaching",
                    Label = "Enable Caching",
                    HelpText = "Cache recommendations to reduce API calls",
                    Type = FieldType.Checkbox,
                    Section = "Advanced",
                    Order = 44
                },
                ["CacheExpirationHours"] = new FieldDefinition
                {
                    Name = "CacheExpirationHours",
                    Label = "Cache Expiration (hours)",
                    HelpText = "How long to cache recommendations (1-168 hours)",
                    Type = FieldType.Number,
                    Section = "Advanced",
                    Hidden = HiddenType.HiddenIfNotSet,
                    Order = 45
                },
                ["EnableHallucinationDetection"] = new FieldDefinition
                {
                    Name = "EnableHallucinationDetection",
                    Label = "Enable Hallucination Detection",
                    HelpText = "Detect and filter out AI-generated fake recommendations",
                    Type = FieldType.Checkbox,
                    Section = "Advanced",
                    Order = 46
                },
                ["HallucinationThreshold"] = new FieldDefinition
                {
                    Name = "HallucinationThreshold",
                    Label = "Hallucination Threshold",
                    HelpText = "Confidence threshold for hallucination detection (0.1-1.0)",
                    Type = FieldType.Number,
                    Section = "Advanced",
                    Hidden = HiddenType.HiddenIfNotSet,
                    Order = 47
                },
                ["RequestTimeoutSeconds"] = new FieldDefinition
                {
                    Name = "RequestTimeoutSeconds",
                    Label = "Request Timeout (seconds)",
                    HelpText = "Timeout for AI provider requests (10-300 seconds)",
                    Type = FieldType.Number,
                    Section = "Advanced",
                    Order = 48
                },
                ["MaxRetryAttempts"] = new FieldDefinition
                {
                    Name = "MaxRetryAttempts",
                    Label = "Max Retry Attempts",
                    HelpText = "Maximum retries for failed requests (0-10)",
                    Type = FieldType.Number,
                    Section = "Advanced",
                    Order = 49
                }
            };
        }
        
        // Helper methods
        private static FieldDefinition CreateApiKeyField(string name, string label, int order)
        {
            return new FieldDefinition
            {
                Name = name,
                Label = label,
                HelpText = $"Your {label} for authentication",
                Type = FieldType.Password,
                Hidden = HiddenType.HiddenIfNotSet,
                Order = order
            };
        }
        
        private static FieldDefinition CreateModelField(string name, string label, List<SelectOption> options, int order)
        {
            var field = new FieldDefinition
            {
                Name = name,
                Label = label,
                HelpText = $"Select the {label} to use",
                Hidden = HiddenType.HiddenIfNotSet,
                Order = order
            };
            
            if (options != null)
            {
                field.Type = FieldType.Select;
                field.SelectOptions = options;
            }
            else
            {
                field.Type = FieldType.Textbox;
            }
            
            return field;
        }
        
        private static List<SelectOption> GetProviderOptions()
        {
            return System.Enum.GetValues<AIProvider>()
                .Select(p => new SelectOption
                {
                    Value = (int)p,
                    Name = GetProviderDisplayName(p)
                })
                .ToList();
        }
        
        private static string GetProviderDisplayName(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => "Ollama (Local)",
                AIProvider.LMStudio => "LM Studio (Local)",
                AIProvider.OpenAI => "OpenAI",
                AIProvider.Anthropic => "Anthropic Claude",
                AIProvider.Gemini => "Google Gemini",
                AIProvider.Groq => "Groq",
                AIProvider.Perplexity => "Perplexity",
                AIProvider.DeepSeek => "DeepSeek",
                AIProvider.OpenRouter => "OpenRouter",
                _ => provider.ToString()
            };
        }
        
        private static List<SelectOption> GetOllamaModels()
        {
            return new List<SelectOption>
            {
                new SelectOption { Value = "llama2", Name = "Llama 2" },
                new SelectOption { Value = "mistral", Name = "Mistral" },
                new SelectOption { Value = "mixtral", Name = "Mixtral" },
                new SelectOption { Value = "codellama", Name = "Code Llama" }
            };
        }
        
        private static List<SelectOption> GetOpenAIModels()
        {
            return new List<SelectOption>
            {
                new SelectOption { Value = "gpt-4", Name = "GPT-4" },
                new SelectOption { Value = "gpt-4-turbo", Name = "GPT-4 Turbo" },
                new SelectOption { Value = "gpt-3.5-turbo", Name = "GPT-3.5 Turbo" },
                new SelectOption { Value = "gpt-3.5-turbo-16k", Name = "GPT-3.5 Turbo 16K" }
            };
        }
        
        private static List<SelectOption> GetAnthropicModels()
        {
            return new List<SelectOption>
            {
                new SelectOption { Value = "claude-3-opus-20240229", Name = "Claude 3 Opus" },
                new SelectOption { Value = "claude-3-sonnet-20240229", Name = "Claude 3 Sonnet" },
                new SelectOption { Value = "claude-3-haiku-20240307", Name = "Claude 3 Haiku" },
                new SelectOption { Value = "claude-2.1", Name = "Claude 2.1" }
            };
        }
        
        private static List<SelectOption> GetGeminiModels()
        {
            return new List<SelectOption>
            {
                new SelectOption { Value = "gemini-pro", Name = "Gemini Pro" },
                new SelectOption { Value = "gemini-pro-vision", Name = "Gemini Pro Vision" }
            };
        }
        
        private static List<SelectOption> GetGroqModels()
        {
            return new List<SelectOption>
            {
                new SelectOption { Value = "mixtral-8x7b-32768", Name = "Mixtral 8x7B" },
                new SelectOption { Value = "llama2-70b-4096", Name = "Llama 2 70B" }
            };
        }
        
        private static List<SelectOption> GetPerplexityModels()
        {
            return new List<SelectOption>
            {
                new SelectOption { Value = "mixtral-8x7b-instruct", Name = "Mixtral 8x7B Instruct" },
                new SelectOption { Value = "sonar-small-chat", Name = "Sonar Small" },
                new SelectOption { Value = "sonar-medium-chat", Name = "Sonar Medium" }
            };
        }
        
        private static List<SelectOption> GetDeepSeekModels()
        {
            return new List<SelectOption>
            {
                new SelectOption { Value = "deepseek-chat", Name = "DeepSeek Chat" },
                new SelectOption { Value = "deepseek-coder", Name = "DeepSeek Coder" }
            };
        }
    }
}