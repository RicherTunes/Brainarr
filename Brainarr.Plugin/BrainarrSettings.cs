using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public class BrainarrSettingsValidator : AbstractValidator<BrainarrSettings>
    {
        public BrainarrSettingsValidator()
        {
            RuleFor(c => c.MaxRecommendations)
                .InclusiveBetween(BrainarrConstants.MinRecommendations, BrainarrConstants.MaxRecommendations)
                .WithMessage($"Recommendations must be between {BrainarrConstants.MinRecommendations} and {BrainarrConstants.MaxRecommendations}");

            When(c => c.Provider == AIProvider.Ollama, () =>
            {
                RuleFor(c => c.OllamaUrlRaw)
                    .Must(url => !string.IsNullOrEmpty(url) && BeValidUrl(url))
                    .WithMessage("Please enter a valid URL like http://localhost:11434")
                    .OverridePropertyName("OllamaUrl"); 
            });

            When(c => c.Provider == AIProvider.LMStudio, () =>
            {
                RuleFor(c => c.LMStudioUrlRaw)
                    .Must(url => !string.IsNullOrEmpty(url) && BeValidUrl(url))
                    .WithMessage("Please enter a valid URL like http://localhost:1234")
                    .OverridePropertyName("LMStudioUrl");
            });

            When(c => c.Provider == AIProvider.Perplexity, () =>
            {
                RuleFor(c => c.PerplexityApiKey)
                    .NotEmpty()
                    .WithMessage("Perplexity API key is required");
            });

            When(c => c.Provider == AIProvider.OpenAI, () =>
            {
                RuleFor(c => c.OpenAIApiKey)
                    .NotEmpty()
                    .WithMessage("OpenAI API key is required");
            });

            When(c => c.Provider == AIProvider.Anthropic, () =>
            {
                RuleFor(c => c.AnthropicApiKey)
                    .NotEmpty()
                    .WithMessage("Anthropic API key is required");
            });

            When(c => c.Provider == AIProvider.OpenRouter, () =>
            {
                RuleFor(c => c.OpenRouterApiKey)
                    .NotEmpty()
                    .WithMessage("OpenRouter API key is required");
            });

            When(c => c.Provider == AIProvider.DeepSeek, () =>
            {
                RuleFor(c => c.DeepSeekApiKey)
                    .NotEmpty()
                    .WithMessage("DeepSeek API key is required");
            });

            When(c => c.Provider == AIProvider.Gemini, () =>
            {
                RuleFor(c => c.GeminiApiKey)
                    .NotEmpty()
                    .WithMessage("Google Gemini API key is required");
            });

            When(c => c.Provider == AIProvider.Groq, () =>
            {
                RuleFor(c => c.GroqApiKey)
                    .NotEmpty()
                    .WithMessage("Groq API key is required");
            });
        }

        private bool BeValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            
            // Reject dangerous schemes upfront
            var lowerUrl = url.ToLowerInvariant();
            if (lowerUrl.StartsWith("javascript:") || 
                lowerUrl.StartsWith("file:") || 
                lowerUrl.StartsWith("ftp:") ||
                lowerUrl.StartsWith("data:") ||
                lowerUrl.StartsWith("vbscript:"))
            {
                return false;
            }
            
            // If no scheme provided, assume http:// and validate
            string urlToValidate = url;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                // Basic check for valid format before adding http://
                if (url.Contains(' ') || url.StartsWith('.') || url.EndsWith('.'))
                    return false;
                    
                urlToValidate = "http://" + url;
            }
            
            return System.Uri.TryCreate(urlToValidate, System.UriKind.Absolute, out var result) 
                && (result.Scheme == System.Uri.UriSchemeHttp || result.Scheme == System.Uri.UriSchemeHttps);
        }
    }

    public enum AIProvider
    {
        // Local providers first (privacy-focused)
        Ollama = 0,       // Local, 100% private
        LMStudio = 1,     // Local with GUI
        
        // Gateway for flexibility
        OpenRouter = 5,   // Access 200+ models
        
        // Cost-effective options
        DeepSeek = 6,     // 10-20x cheaper
        Gemini = 7,       // Free tier available
        Groq = 8,         // Ultra-fast inference
        
        // Premium cloud options
        Perplexity = 2,   // Web-enhanced
        OpenAI = 3,       // GPT-4 quality
        Anthropic = 4     // Best reasoning
    }

    public enum DiscoveryMode
    {
        Similar = 0,      // Very similar to existing library
        Adjacent = 1,     // Related genres
        Exploratory = 2   // New genres to explore
    }

    public enum SamplingStrategy
    {
        Minimal = 0,      // Small sample for fast responses (local models)
        Balanced = 1,     // Default - good balance of context and speed
        Comprehensive = 2 // Large sample for best quality (premium providers)
    }

    public enum PerplexityModel
    {
        Sonar_Large = 0,  // llama-3.1-sonar-large-128k-online - Best for online search
        Sonar_Small = 1,  // llama-3.1-sonar-small-128k-online - Faster, lower cost
        Sonar_Huge = 2    // llama-3.1-sonar-huge-128k-online - Most powerful
    }

    public enum OpenAIModel
    {
        GPT4o_Mini = 0,   // gpt-4o-mini - Most cost-effective
        GPT4o = 1,        // gpt-4o - Latest multimodal model
        GPT4_Turbo = 2,   // gpt-4-turbo - Previous generation
        GPT35_Turbo = 3   // gpt-3.5-turbo - Legacy, lowest cost
    }

    public enum AnthropicModel
    {
        Claude35_Haiku = 0,  // claude-3-5-haiku-latest - Fast and cost-effective
        Claude35_Sonnet = 1, // claude-3-5-sonnet-latest - Balanced performance
        Claude3_Opus = 2     // claude-3-opus-20240229 - Most capable
    }

    public enum OpenRouterModel
    {
        // Best value models
        Claude35_Haiku = 0,      // anthropic/claude-3.5-haiku - Fast & cheap
        DeepSeekV3 = 1,           // deepseek/deepseek-chat - Very cost-effective
        Gemini_Flash = 2,         // google/gemini-flash-1.5 - Fast Google model
        
        // Balanced performance
        Claude35_Sonnet = 3,      // anthropic/claude-3.5-sonnet - Best overall
        GPT4o_Mini = 4,           // openai/gpt-4o-mini - OpenAI efficient
        Llama3_70B = 5,           // meta-llama/llama-3-70b-instruct - Open source
        
        // Premium models
        GPT4o = 6,                // openai/gpt-4o - Latest OpenAI
        Claude3_Opus = 7,         // anthropic/claude-3-opus - Most capable
        Gemini_Pro = 8,           // google/gemini-pro-1.5 - Large context
        
        // Specialized
        Mistral_Large = 9,        // mistral/mistral-large - European
        Qwen_72B = 10             // qwen/qwen-72b-chat - Multilingual
    }

    public enum DeepSeekModel
    {
        DeepSeek_Chat = 0,        // deepseek-chat - Latest V3, best overall
        DeepSeek_Coder = 1,       // deepseek-coder - Optimized for code
        DeepSeek_Reasoner = 2     // deepseek-reasoner - R1 reasoning model
    }

    public enum GeminiModel
    {
        Gemini_15_Flash = 0,      // gemini-1.5-flash - Fast, 1M context
        Gemini_15_Flash_8B = 1,   // gemini-1.5-flash-8b - Smaller, faster
        Gemini_15_Pro = 2,        // gemini-1.5-pro - Most capable, 2M context
        Gemini_20_Flash = 3       // gemini-2.0-flash-exp - Latest experimental
    }

    public enum GroqModel
    {
        Llama33_70B = 0,          // llama-3.3-70b-versatile - Latest, most capable
        Llama32_90B_Vision = 1,   // llama-3.2-90b-vision-preview - Multimodal
        Llama31_70B = 2,          // llama-3.1-70b-versatile - Previous gen
        Mixtral_8x7B = 3,         // mixtral-8x7b-32768 - Fast MoE model
        Gemma2_9B = 4             // gemma2-9b-it - Google's efficient model
    }

    public class BrainarrSettings : IImportListSettings
    {
        private static readonly BrainarrSettingsValidator Validator = new BrainarrSettingsValidator();

        public BrainarrSettings()
        {
            // Sensible defaults that actually work
            Provider = AIProvider.Ollama;
            _ollamaUrl = BrainarrConstants.DefaultOllamaUrl;
            _ollamaModel = BrainarrConstants.DefaultOllamaModel;
            _lmStudioUrl = BrainarrConstants.DefaultLMStudioUrl;
            _lmStudioModel = BrainarrConstants.DefaultLMStudioModel;
            MaxRecommendations = BrainarrConstants.DefaultRecommendations;
            DiscoveryMode = DiscoveryMode.Adjacent;
            SamplingStrategy = SamplingStrategy.Balanced;
            AutoDetectModel = true;
        }

        // ====== QUICK START GUIDE ======
        [FieldDefinition(0, Label = "AI Provider", Type = FieldType.Select, SelectOptions = typeof(AIProvider), 
            HelpText = "Choose your AI provider:\n🏠 LOCAL (Private): Ollama, LM Studio - Your data stays private\n🌐 GATEWAY: OpenRouter - Access 200+ models with one key\n💰 BUDGET: DeepSeek, Gemini - Low cost or free\n⚡ FAST: Groq - Ultra-fast responses\n🤖 PREMIUM: OpenAI, Anthropic - Best quality\n\n⚠️ After selecting, click 'Test' to verify connection!")]
        public AIProvider Provider { get; set; }

        // Ollama Settings
        private string _ollamaUrl;
        private string _ollamaModel;
        private string _lmStudioUrl;
        private string _lmStudioModel;

        [FieldDefinition(1, Label = "Configuration URL", Type = FieldType.Textbox,
            HelpText = "Provider-specific URL will be auto-configured based on your selection above")]
        public string ConfigurationUrl 
        { 
            get => Provider switch
            {
                AIProvider.Ollama => string.IsNullOrEmpty(_ollamaUrl) ? BrainarrConstants.DefaultOllamaUrl : _ollamaUrl,
                AIProvider.LMStudio => string.IsNullOrEmpty(_lmStudioUrl) ? BrainarrConstants.DefaultLMStudioUrl : _lmStudioUrl,
                _ => "N/A - API Key based provider"
            };
            set 
            {
                if (Provider == AIProvider.Ollama) _ollamaUrl = value;
                else if (Provider == AIProvider.LMStudio) _lmStudioUrl = value;
            }
        }

        [FieldDefinition(2, Label = "Model Selection", Type = FieldType.Select, SelectOptionsProviderAction = "getModelOptions",
            HelpText = "⚠️ IMPORTANT: Click 'Test' first to auto-detect available models!")]
        public string ModelSelection 
        { 
            get => Provider switch
            {
                AIProvider.Ollama => string.IsNullOrEmpty(_ollamaModel) ? BrainarrConstants.DefaultOllamaModel : _ollamaModel,
                AIProvider.LMStudio => string.IsNullOrEmpty(_lmStudioModel) ? BrainarrConstants.DefaultLMStudioModel : _lmStudioModel,
                AIProvider.Perplexity => PerplexityModel ?? "Sonar_Large",
                AIProvider.OpenAI => OpenAIModel ?? "GPT4o_Mini", 
                AIProvider.Anthropic => AnthropicModel ?? "Claude35_Haiku",
                AIProvider.OpenRouter => OpenRouterModel ?? "Claude35_Haiku",
                AIProvider.DeepSeek => DeepSeekModel ?? "DeepSeek_Chat",
                AIProvider.Gemini => GeminiModel ?? "Gemini_15_Flash",
                AIProvider.Groq => GroqModel ?? "Llama33_70B",
                _ => "Default"
            };
            set 
            {
                switch (Provider)
                {
                    case AIProvider.Ollama: _ollamaModel = value; break;
                    case AIProvider.LMStudio: _lmStudioModel = value; break;
                    case AIProvider.Perplexity: PerplexityModel = value; break;
                    case AIProvider.OpenAI: OpenAIModel = value; break;
                    case AIProvider.Anthropic: AnthropicModel = value; break;
                    case AIProvider.OpenRouter: OpenRouterModel = value; break;
                    case AIProvider.DeepSeek: DeepSeekModel = value; break;
                    case AIProvider.Gemini: GeminiModel = value; break;
                    case AIProvider.Groq: GroqModel = value; break;
                }
            }
        }

        [FieldDefinition(3, Label = "API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Enter your API key for the selected provider. Not needed for local providers (Ollama/LM Studio)")]
        public string ApiKey 
        { 
            get => Provider switch
            {
                AIProvider.Perplexity => PerplexityApiKey,
                AIProvider.OpenAI => OpenAIApiKey,
                AIProvider.Anthropic => AnthropicApiKey,
                AIProvider.OpenRouter => OpenRouterApiKey,
                AIProvider.DeepSeek => DeepSeekApiKey,
                AIProvider.Gemini => GeminiApiKey,
                AIProvider.Groq => GroqApiKey,
                _ => null
            };
            set
            {
                switch (Provider)
                {
                    case AIProvider.Perplexity: PerplexityApiKey = value; break;
                    case AIProvider.OpenAI: OpenAIApiKey = value; break;
                    case AIProvider.Anthropic: AnthropicApiKey = value; break;
                    case AIProvider.OpenRouter: OpenRouterApiKey = value; break;
                    case AIProvider.DeepSeek: DeepSeekApiKey = value; break;
                    case AIProvider.Gemini: GeminiApiKey = value; break;
                    case AIProvider.Groq: GroqApiKey = value; break;
                }
            }
        }

        // Hidden backing fields for all providers
        public string OllamaUrl 
        { 
            get => string.IsNullOrEmpty(_ollamaUrl) ? BrainarrConstants.DefaultOllamaUrl : _ollamaUrl;
            set => _ollamaUrl = value;
        }
        
        // Internal property for validation - returns actual value without defaults
        internal string OllamaUrlRaw => _ollamaUrl;

        public string OllamaModel 
        { 
            get => string.IsNullOrEmpty(_ollamaModel) ? BrainarrConstants.DefaultOllamaModel : _ollamaModel;
            set => _ollamaModel = value;
        }

        public string LMStudioUrl 
        { 
            get => string.IsNullOrEmpty(_lmStudioUrl) ? BrainarrConstants.DefaultLMStudioUrl : _lmStudioUrl;
            set => _lmStudioUrl = value;
        }
        
        // Internal property for validation - returns actual value without defaults
        internal string LMStudioUrlRaw => _lmStudioUrl;

        public string LMStudioModel 
        { 
            get => string.IsNullOrEmpty(_lmStudioModel) ? BrainarrConstants.DefaultLMStudioModel : _lmStudioModel;
            set => _lmStudioModel = value;
        }

        // Hidden backing properties for all API-based providers
        public string PerplexityApiKey { get; set; }
        public string PerplexityModel { get; set; }
        public string OpenAIApiKey { get; set; }
        public string OpenAIModel { get; set; }
        public string AnthropicApiKey { get; set; }
        public string AnthropicModel { get; set; }
        public string OpenRouterApiKey { get; set; }
        public string OpenRouterModel { get; set; }
        public string DeepSeekApiKey { get; set; }
        public string DeepSeekModel { get; set; }
        public string GeminiApiKey { get; set; }
        public string GeminiModel { get; set; }
        public string GroqApiKey { get; set; }
        public string GroqModel { get; set; }

        // Auto-detect model (show for all providers)
        [FieldDefinition(4, Label = "Auto-Detect Model", Type = FieldType.Checkbox, HelpText = "Automatically detect and select best available model")]
        public bool AutoDetectModel { get; set; }

        // Discovery Settings
        [FieldDefinition(5, Label = "Recommendations", Type = FieldType.Number, 
            HelpText = "Number of albums per sync (1-50, default: 10)\n💡 Start with 5-10 and increase if you like the results")]
        public int MaxRecommendations { get; set; }

        [FieldDefinition(6, Label = "Discovery Mode", Type = FieldType.Select, SelectOptions = typeof(DiscoveryMode), 
            HelpText = "How adventurous should recommendations be?\n• Similar: Stay close to current taste\n• Adjacent: Explore related genres\n• Exploratory: Discover new genres")]
        public DiscoveryMode DiscoveryMode { get; set; }

        [FieldDefinition(7, Label = "Library Sampling", Type = FieldType.Select, SelectOptions = typeof(SamplingStrategy),
            HelpText = "How much of your library to include in AI prompts\n• Minimal: Fast, less context (good for local models)\n• Balanced: Default, optimal balance\n• Comprehensive: Maximum context (best for GPT-4/Claude)")]
        public SamplingStrategy SamplingStrategy { get; set; }

        // Lidarr Integration (Hidden from UI, set by Lidarr)
        public string BaseUrl 
        { 
            get => Provider == AIProvider.Ollama ? OllamaUrl : LMStudioUrl;
            set { /* Handled by provider-specific URLs */ }
        }

        // Model Detection Results (populated during test)
        public List<string> DetectedModels { get; set; } = new List<string>();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}