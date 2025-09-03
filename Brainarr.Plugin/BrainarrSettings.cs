using System;
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
                    .Must(url => string.IsNullOrEmpty(url) || BeValidUrl(url))
                    .WithMessage("Please enter a valid URL like http://localhost:11434")
                    .OverridePropertyName("OllamaUrl"); 
            });

            When(c => c.Provider == AIProvider.LMStudio, () =>
            {
                RuleFor(c => c.LMStudioUrlRaw)
                    .Must(url => string.IsNullOrEmpty(url) || BeValidUrl(url))
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
            if (string.IsNullOrWhiteSpace(url)) return true;
            return TryNormalizeHttpUrl(ref url);
        }

        private static bool TryNormalizeHttpUrl(ref string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;

            string raw;
            try { raw = Uri.UnescapeDataString(url.Trim()); }
            catch { raw = url.Trim(); }

            var lowerRaw = raw.ToLowerInvariant();
            if (lowerRaw.StartsWith("javascript:") || lowerRaw.StartsWith("file:") ||
                lowerRaw.StartsWith("ftp:") || lowerRaw.StartsWith("data:") ||
                lowerRaw.StartsWith("vbscript:"))
            {
                return false;
            }

            if (raw.Contains("://") && !raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var candidate = (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                ? raw
                : $"http://{raw}";

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var u)) return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;

            url = u.ToString();
            return true;
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

    public enum RecommendationMode
    {
        SpecificAlbums = 0,  // Recommend specific albums to import
        Artists = 1          // Recommend artists (Lidarr imports all their albums)
    }

    public enum PerplexityModelKind
    {
        Sonar_Large = 0,  // llama-3.1-sonar-large-128k-online - Best for online search
        Sonar_Small = 1,  // llama-3.1-sonar-small-128k-online - Faster, lower cost
        Sonar_Huge = 2    // llama-3.1-sonar-huge-128k-online - Most powerful
    }

    public enum OpenAIModelKind
    {
        GPT4o_Mini = 0,   // gpt-4o-mini - Most cost-effective
        GPT4o = 1,        // gpt-4o - Latest multimodal model
        GPT4_Turbo = 2,   // gpt-4-turbo - Previous generation
        GPT35_Turbo = 3   // gpt-3.5-turbo - Legacy, lowest cost
    }

    public enum AnthropicModelKind
    {
        Claude35_Haiku = 0,  // claude-3-5-haiku-latest - Fast and cost-effective
        Claude35_Sonnet = 1, // claude-3-5-sonnet-latest - Balanced performance
        Claude3_Opus = 2     // claude-3-opus-20240229 - Most capable
    }

    public enum OpenRouterModelKind
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

    public enum DeepSeekModelKind
    {
        DeepSeek_Chat = 0,        // deepseek-chat - Latest V3, best overall
        DeepSeek_Coder = 1,       // deepseek-coder - Optimized for code
        DeepSeek_Reasoner = 2     // deepseek-reasoner - R1 reasoning model
    }

    public enum GeminiModelKind
    {
        Gemini_15_Flash = 0,      // gemini-1.5-flash - Fast, 1M context
        Gemini_15_Flash_8B = 1,   // gemini-1.5-flash-8b - Smaller, faster
        Gemini_15_Pro = 2,        // gemini-1.5-pro - Most capable, 2M context
        Gemini_20_Flash = 3       // gemini-2.0-flash-exp - Latest experimental
    }

    public enum GroqModelKind
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
        private AIProvider _provider;
        private AIProvider? _previousProvider;

        public BrainarrSettings()
        {
            // Sensible defaults that actually work
            _provider = AIProvider.Ollama;
            _ollamaUrl = BrainarrConstants.DefaultOllamaUrl;
            _ollamaModel = BrainarrConstants.DefaultOllamaModel;
            _lmStudioUrl = BrainarrConstants.DefaultLMStudioUrl;
            _lmStudioModel = BrainarrConstants.DefaultLMStudioModel;
            MaxRecommendations = BrainarrConstants.DefaultRecommendations;
            DiscoveryMode = DiscoveryMode.Adjacent;
            SamplingStrategy = SamplingStrategy.Balanced;
            RecommendationMode = RecommendationMode.SpecificAlbums;
            AutoDetectModel = true;
            // Default iterative refinement on for local default provider
            EnableIterativeRefinement = true;
        }

        // ====== QUICK START GUIDE ======
        [FieldDefinition(0, Label = "AI Provider", Type = FieldType.Select, SelectOptions = typeof(AIProvider), 
            HelpText = "Choose your AI provider:\n🏠 LOCAL (Private): Ollama, LM Studio - Your data stays private\n🌐 GATEWAY: OpenRouter - Access 200+ models with one key\n💰 BUDGET: DeepSeek, Gemini - Low cost or free\n⚡ FAST: Groq - Ultra-fast responses\n🤖 PREMIUM: OpenAI, Anthropic - Best quality\n\n⚠️ After selecting, click 'Test' to verify connection!")]
        public AIProvider Provider 
        { 
            get => _provider;
            set
            {
                if (_provider != value)
                {
                    _previousProvider = _provider;
                    _provider = value;
                    // Don't clear any models - preserve settings for each provider
                    // Auto-enable iterative refinement for local providers for better fill behavior
                    if (_provider == AIProvider.Ollama || _provider == AIProvider.LMStudio)
                    {
                        EnableIterativeRefinement = true;
                    }
                }
                else
                {
                    // Same provider - treat as reset operation  
                    ClearCurrentProviderModel();
                }
            }
        }

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
            get
            {
                return Provider switch
                {
                    AIProvider.Ollama => string.IsNullOrEmpty(_ollamaModel) ? BrainarrConstants.DefaultOllamaModel : _ollamaModel,
                    AIProvider.LMStudio => string.IsNullOrEmpty(_lmStudioModel) ? BrainarrConstants.DefaultLMStudioModel : _lmStudioModel,
                    AIProvider.Perplexity => string.IsNullOrEmpty(PerplexityModelId) ? "Sonar_Large" : PerplexityModelId,
                    AIProvider.OpenAI => string.IsNullOrEmpty(OpenAIModelId) ? "GPT4o_Mini" : OpenAIModelId, 
                    AIProvider.Anthropic => string.IsNullOrEmpty(AnthropicModelId) ? "Claude35_Haiku" : AnthropicModelId,
                    AIProvider.OpenRouter => string.IsNullOrEmpty(OpenRouterModelId) ? "Claude35_Haiku" : OpenRouterModelId,
                    AIProvider.DeepSeek => string.IsNullOrEmpty(DeepSeekModelId) ? "DeepSeek_Chat" : DeepSeekModelId,
                    AIProvider.Gemini => string.IsNullOrEmpty(GeminiModelId) ? "Gemini_15_Flash" : GeminiModelId,
                    AIProvider.Groq => string.IsNullOrEmpty(GroqModelId) ? "Llama33_70B" : GroqModelId,
                    _ => "Default"
                };
            }
            set 
            {
                switch (Provider)
                {
                    case AIProvider.Ollama: _ollamaModel = value; break;
                    case AIProvider.LMStudio: _lmStudioModel = value; break;
                    case AIProvider.Perplexity: PerplexityModelId = value; break;
                    case AIProvider.OpenAI: OpenAIModelId = value; break;
                    case AIProvider.Anthropic: AnthropicModelId = value; break;
                    case AIProvider.OpenRouter: OpenRouterModelId = value; break;
                    case AIProvider.DeepSeek: DeepSeekModelId = value; break;
                    case AIProvider.Gemini: GeminiModelId = value; break;
                    case AIProvider.Groq: GroqModelId = value; break;
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
            set => _ollamaUrl = NormalizeHttpUrlOrOriginal(value);
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
            set => _lmStudioUrl = NormalizeHttpUrlOrOriginal(value);
        }
        
        // Internal property for validation - returns actual value without defaults
        internal string LMStudioUrlRaw => _lmStudioUrl;

        public string LMStudioModel 
        { 
            get => string.IsNullOrEmpty(_lmStudioModel) ? BrainarrConstants.DefaultLMStudioModel : _lmStudioModel;
            set => _lmStudioModel = value;
        }

        // Hidden backing properties for all API-based providers
        // SECURITY: API keys are stored as strings and only marked as Password in UI fields.
        // Do not log these values; consider external secret storage if needed.
        private string? _perplexityApiKey;
        private string? _openAIApiKey;
        private string? _anthropicApiKey;
        private string? _openRouterApiKey;
        private string? _deepSeekApiKey;
        private string? _geminiApiKey;
        private string? _groqApiKey;
        
        public string PerplexityApiKey 
        { 
            get => _perplexityApiKey; 
            set => _perplexityApiKey = SanitizeApiKey(value); 
        }
        // New canonical model id properties per provider
        public string? PerplexityModelId { get; set; }
        // Backward-compat aliases for tests and legacy code
        public string? PerplexityModel { get => PerplexityModelId; set => PerplexityModelId = value; }
        public string? OpenAIApiKey 
        { 
            get => _openAIApiKey; 
            set => _openAIApiKey = SanitizeApiKey(value); 
        }
        public string? OpenAIModelId { get; set; }
        public string? OpenAIModel { get => OpenAIModelId; set => OpenAIModelId = value; }
        public string? AnthropicApiKey 
        { 
            get => _anthropicApiKey; 
            set => _anthropicApiKey = SanitizeApiKey(value); 
        }
        public string? AnthropicModelId { get; set; }
        public string? AnthropicModel { get => AnthropicModelId; set => AnthropicModelId = value; }
        public string? OpenRouterApiKey 
        { 
            get => _openRouterApiKey; 
            set => _openRouterApiKey = SanitizeApiKey(value); 
        }
        public string? OpenRouterModelId { get; set; }
        public string? OpenRouterModel { get => OpenRouterModelId; set => OpenRouterModelId = value; }
        public string? DeepSeekApiKey 
        { 
            get => _deepSeekApiKey; 
            set => _deepSeekApiKey = SanitizeApiKey(value); 
        }
        public string? DeepSeekModelId { get; set; }
        public string? DeepSeekModel { get => DeepSeekModelId; set => DeepSeekModelId = value; }
        public string? GeminiApiKey 
        { 
            get => _geminiApiKey; 
            set => _geminiApiKey = SanitizeApiKey(value); 
        }
        public string? GeminiModelId { get; set; }
        public string? GeminiModel { get => GeminiModelId; set => GeminiModelId = value; }
        public string? GroqApiKey 
        { 
            get => _groqApiKey; 
            set => _groqApiKey = SanitizeApiKey(value); 
        }
        public string? GroqModelId { get; set; }
        public string? GroqModel { get => GroqModelId; set => GroqModelId = value; }

        // No backward-compat properties; canonical fields are *ModelId

        // Auto-detect model (show for all providers)
        [FieldDefinition(4, Label = "Auto-Detect Model", Type = FieldType.Checkbox, HelpText = "Automatically detect and select best available model")]
        public bool AutoDetectModel { get; set; }

        // Discovery Settings
        [FieldDefinition(5, Label = "Recommendations", Type = FieldType.Number, 
            HelpText = "Number of albums per sync (1-50, default: 10)\n💡 Start with 5-10 and increase if you like the results")]
        public int MaxRecommendations { get; set; }

        [FieldDefinition(6, Label = "Discovery Mode", Type = FieldType.Select, SelectOptions = typeof(DiscoveryMode), 
            HelpText = "How adventurous should recommendations be?\n• Similar: Stay close to current taste\n• Adjacent: Explore related genres\n• Exploratory: Discover new genres", 
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#discovery-mode")]
        public DiscoveryMode DiscoveryMode { get; set; }

        [FieldDefinition(7, Label = "Library Sampling", Type = FieldType.Select, SelectOptions = typeof(SamplingStrategy),
            HelpText = "How much of your library to include in AI prompts\n• Minimal: Fast, less context (good for local models)\n• Balanced: Default, optimal balance\n• Comprehensive: Maximum context (best for GPT-4/Claude)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#library-sampling")]
        public SamplingStrategy SamplingStrategy { get; set; }

        [FieldDefinition(8, Label = "Recommendation Type", Type = FieldType.Select, SelectOptions = typeof(RecommendationMode),
            HelpText = "Control what gets recommended:\n• Specific Albums: Recommend individual albums to import\n• Artists: Recommend artists (Lidarr will import ALL their albums)\n\n💡 Choose 'Artists' for comprehensive library building, 'Specific Albums' for targeted additions",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#recommendation-type")]
        public RecommendationMode RecommendationMode { get; set; }

        // Lidarr Integration (Hidden from UI, set by Lidarr)
        public string BaseUrl 
        { 
            get => Provider == AIProvider.Ollama ? OllamaUrl : LMStudioUrl;
            set { /* Handled by provider-specific URLs */ }
        }

        // Model Detection Results (populated during test)
        public List<string> DetectedModels { get; set; } = new List<string>();

        // Auto-detection enabled flag
        public bool EnableAutoDetection { get; set; } = true;

        // Additional missing properties
        public bool EnableFallbackModel { get; set; } = true;
        public string FallbackModel { get; set; } = "qwen2.5:latest";
        public bool EnableLibraryAnalysis { get; set; } = true;
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(BrainarrConstants.MinRefreshIntervalHours);
        [FieldDefinition(17, Label = "Iterative Top-Up", Type = FieldType.Checkbox, Advanced = true,
            HelpText = "If under target, request additional recommendations with feedback to fill the gap.\nFor local providers (Ollama/LM Studio) this runs by default.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#iterative-top-up")]
        public bool EnableIterativeRefinement { get; set; } = false;

        // Iteration Hysteresis (Advanced)
        [FieldDefinition(18, Label = "Top-Up Max Iterations", Type = FieldType.Number, Advanced = true,
            HelpText = "Maximum top-up iterations before stopping (default: 3)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeMaxIterations { get; set; } = 3;

        [FieldDefinition(19, Label = "Top-Up Zero-Success Stop", Type = FieldType.Number, Advanced = true,
            HelpText = "Stop top-up after this many zero-unique iterations (default: 1)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeZeroSuccessStopThreshold { get; set; } = 1;

        [FieldDefinition(20, Label = "Top-Up Low-Success Stop", Type = FieldType.Number, Advanced = true,
            HelpText = "Stop top-up after this many low-success iterations (<70%) (default: 2)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeLowSuccessStopThreshold { get; set; } = 2;

        [FieldDefinition(21, Label = "Top-Up Cooldown (ms)", Type = FieldType.Number, Advanced = true,
            HelpText = "Cooldown (milliseconds) on early stop to reduce churn (local providers). Default: 1000ms",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeCooldownMs { get; set; } = 1000;

        // Advanced Validation Settings
        [FieldDefinition(9, Label = "Custom Filter Patterns", Type = FieldType.Textbox, Advanced = true,
            HelpText = "Additional patterns to filter out AI hallucinations (comma-separated)\nExample: '(alternate take), (radio mix), (demo version)'\n⚠️ Be careful not to filter legitimate albums!")]
        public string CustomFilterPatterns { get; set; } = string.Empty;

        [FieldDefinition(10, Label = "Enable Strict Validation", Type = FieldType.Checkbox, Advanced = true,
            HelpText = "Apply stricter validation rules to reduce false positives\n✅ Filters more aggressively\n❌ May block some legitimate albums")]
        public bool EnableStrictValidation { get; set; }

        [FieldDefinition(11, Label = "Enable Debug Logging", Type = FieldType.Checkbox, Advanced = true,
            HelpText = "Enable detailed logging for troubleshooting\n⚠️ Creates verbose logs")]
        public bool EnableDebugLogging { get; set; }

        // Safety Gates
        [FieldDefinition(12, Label = "Minimum Confidence", Type = FieldType.Number, Advanced = true,
            HelpText = "Drop or queue items below this confidence (0.0–1.0)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#safety-gates")]
        public double MinConfidence { get; set; } = 0.7;

        [FieldDefinition(13, Label = "Require MusicBrainz IDs", Type = FieldType.Checkbox, Advanced = true,
            HelpText = "Require MBIDs before adding. Items without MBIDs are sent to Review Queue",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#safety-gates")]
        public bool RequireMbids { get; set; } = true;

        [FieldDefinition(14, Label = "Queue Borderline Items", Type = FieldType.Checkbox, Advanced = true,
            HelpText = "Send low-confidence or missing-MBID items to the Review Queue instead of dropping them",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#safety-gates")]
        public bool QueueBorderlineItems { get; set; } = true;

        // Review Queue UI integration
        [FieldDefinition(15, Label = "Approve Suggestions", Type = FieldType.Tag, 
            HelpText = "Select pending review items to approve; Save settings to apply.", 
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Review-Queue",
            SelectOptionsProviderAction = "review/getOptions")]
        public IEnumerable<string> ReviewApproveKeys { get; set; } = Array.Empty<string>();

        [FieldDefinition(16, Label = "Review Summary", Type = FieldType.Tag,
            HelpText = "Read-only overview of your Review Queue (counts)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Review-Queue",
            SelectOptionsProviderAction = "review/getSummaryOptions")]
        public IEnumerable<string> ReviewSummary { get; set; } = Array.Empty<string>();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
        
        private string? SanitizeApiKey(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return apiKey;
                
            // Remove any potential whitespace or control characters
            apiKey = apiKey?.Trim();
            
            // Basic validation to prevent injection
            if (apiKey != null && apiKey.Length > 500)
            {
                throw new ArgumentException("API key exceeds maximum allowed length");
            }
            
            return apiKey;
        }

        /// <summary>
        /// Gets provider-specific settings for configuration.
        /// </summary>
        public Dictionary<string, object> GetProviderSettings(AIProvider provider)
        {
            var settings = new Dictionary<string, object>();

            switch (provider)
            {
                case AIProvider.Ollama:
                    settings["url"] = OllamaUrl;
                    settings["model"] = OllamaModel;
                    break;
                case AIProvider.LMStudio:
                    settings["url"] = LMStudioUrl;
                    settings["model"] = LMStudioModel;
                    break;
                case AIProvider.OpenAI:
                    settings["apiKey"] = OpenAIApiKey;
                    settings["model"] = OpenAIModelId;
                    break;
                case AIProvider.Anthropic:
                    settings["apiKey"] = AnthropicApiKey;
                    settings["model"] = AnthropicModelId;
                    break;
                case AIProvider.Perplexity:
                    settings["apiKey"] = PerplexityApiKey;
                    settings["model"] = PerplexityModelId;
                    break;
                case AIProvider.OpenRouter:
                    settings["apiKey"] = OpenRouterApiKey;
                    settings["model"] = OpenRouterModelId;
                    break;
                case AIProvider.DeepSeek:
                    settings["apiKey"] = DeepSeekApiKey;
                    settings["model"] = DeepSeekModelId;
                    break;
                case AIProvider.Gemini:
                    settings["apiKey"] = GeminiApiKey;
                    settings["model"] = GeminiModelId;
                    break;
                case AIProvider.Groq:
                    settings["apiKey"] = GroqApiKey;
                    settings["model"] = GroqModelId;
                    break;
            }

            return settings;
        }


        /// <summary>
        /// Gets the default model for a specific provider.
        /// </summary>
        private string? GetCurrentProviderModel()
        {
            return Provider switch
            {
                AIProvider.Ollama => _ollamaModel,
                AIProvider.LMStudio => _lmStudioModel,
                AIProvider.Perplexity => PerplexityModelId,
                AIProvider.OpenAI => OpenAIModelId, 
                AIProvider.Anthropic => AnthropicModelId,
                AIProvider.OpenRouter => OpenRouterModelId,
                AIProvider.DeepSeek => DeepSeekModelId,
                AIProvider.Gemini => GeminiModelId,
                AIProvider.Groq => GroqModelId,
                _ => null
            };
        }

        private void ClearCurrentProviderModel()
        {
            // Clear the model for the current provider to reset to default
            switch (_provider)
            {
                case AIProvider.Ollama:
                    _ollamaModel = null;
                    break;
                case AIProvider.LMStudio:
                    _lmStudioModel = null;
                    break;
                case AIProvider.Perplexity:
                    PerplexityModelId = null;
                    break;
                case AIProvider.OpenAI:
                    OpenAIModelId = null;
                    break;
                case AIProvider.Anthropic:
                    AnthropicModelId = null;
                    break;
                case AIProvider.OpenRouter:
                    OpenRouterModelId = null;
                    break;
                case AIProvider.DeepSeek:
                    DeepSeekModelId = null;
                    break;
                case AIProvider.Gemini:
                    GeminiModelId = null;
                    break;
                case AIProvider.Groq:
                    GroqModelId = null;
                    break;
            }
        }

        private void ClearPreviousProviderModel()
        {
            // Clear the model for the previous provider when switching away
            if (_previousProvider.HasValue)
            {
                switch (_previousProvider.Value)
                {
                    case AIProvider.Ollama:
                        _ollamaModel = null;
                        break;
                    case AIProvider.LMStudio:
                        _lmStudioModel = null;
                        break;
                    case AIProvider.Perplexity:
                        PerplexityModelId = null;
                        break;
                    case AIProvider.OpenAI:
                        OpenAIModelId = null;
                        break;
                    case AIProvider.Anthropic:
                        AnthropicModelId = null;
                        break;
                    case AIProvider.OpenRouter:
                        OpenRouterModelId = null;
                        break;
                    case AIProvider.DeepSeek:
                        DeepSeekModelId = null;
                        break;
                    case AIProvider.Gemini:
                        GeminiModelId = null;
                        break;
                    case AIProvider.Groq:
                        GroqModelId = null;
                        break;
                }
            }
        }

        private string GetDefaultModelForProvider(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => BrainarrConstants.DefaultOllamaModel,
                AIProvider.LMStudio => BrainarrConstants.DefaultLMStudioModel,
                AIProvider.Perplexity => "Sonar_Large",
                AIProvider.OpenAI => "GPT4o_Mini",
                AIProvider.Anthropic => "Claude35_Haiku",
                AIProvider.OpenRouter => "Claude35_Haiku",
                AIProvider.DeepSeek => "DeepSeek_Chat",
                AIProvider.Gemini => "Gemini_15_Flash",
                AIProvider.Groq => "Llama33_70B",
                _ => "Default"
            };
        }

        // Polymorphic methods to get/set provider-specific configuration
        public string? GetModelForProvider()
        {
            return Provider switch
            {
                AIProvider.Ollama => OllamaModel,
                AIProvider.LMStudio => LMStudioModel,
                AIProvider.OpenAI => OpenAIModelId,
                AIProvider.Anthropic => AnthropicModelId,
                AIProvider.Perplexity => PerplexityModelId,
                AIProvider.OpenRouter => OpenRouterModelId,
                AIProvider.DeepSeek => DeepSeekModelId,
                AIProvider.Gemini => GeminiModelId,
                AIProvider.Groq => GroqModelId,
                _ => null
            };
        }

        public void SetModelForProvider(string? model)
        {
            switch (Provider)
            {
                case AIProvider.Ollama:
                    OllamaModel = model;
                    break;
                case AIProvider.LMStudio:
                    LMStudioModel = model;
                    break;
                case AIProvider.OpenAI:
                    OpenAIModelId = model;
                    break;
                case AIProvider.Anthropic:
                    AnthropicModelId = model;
                    break;
                case AIProvider.Perplexity:
                    PerplexityModelId = model;
                    break;
                case AIProvider.OpenRouter:
                    OpenRouterModelId = model;
                    break;
                case AIProvider.DeepSeek:
                    DeepSeekModelId = model;
                    break;
                case AIProvider.Gemini:
                    GeminiModelId = model;
                    break;
                case AIProvider.Groq:
                    GroqModelId = model;
                    break;
            }
        }

        public string? GetApiKeyForProvider()
        {
            return Provider switch
            {
                AIProvider.OpenAI => OpenAIApiKey,
                AIProvider.Anthropic => AnthropicApiKey,
                AIProvider.Perplexity => PerplexityApiKey,
                AIProvider.OpenRouter => OpenRouterApiKey,
                AIProvider.DeepSeek => DeepSeekApiKey,
                AIProvider.Gemini => GeminiApiKey,
                AIProvider.Groq => GroqApiKey,
                _ => null
            };
        }

        public string? GetBaseUrlForProvider()
        {
            return Provider switch
            {
                AIProvider.Ollama => OllamaUrl,
                AIProvider.LMStudio => LMStudioUrl,
                _ => null
            };
        }

        private static string NormalizeHttpUrlOrOriginal(string value)
        {
            try
            {
                var v = value;
                if (string.IsNullOrWhiteSpace(v)) return v;
                // Accept http/https only
                if (!v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !v.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    v = $"http://{v}";
                }
                if (Uri.TryCreate(v.Trim(), UriKind.Absolute, out var u))
                {
                    if (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                    {
                        return u.ToString();
                    }
                }
                return value;
            }
            catch
            {
                return value;
            }
        }
    }
}
