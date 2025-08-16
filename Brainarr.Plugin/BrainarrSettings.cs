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
                RuleFor(c => c.OllamaUrl)
                    .NotEmpty()
                    .WithMessage("Ollama URL is required (default: http://localhost:11434)")
                    .Must(BeValidUrl)
                    .WithMessage("Please enter a valid URL like http://localhost:11434");
            });

            When(c => c.Provider == AIProvider.LMStudio, () =>
            {
                RuleFor(c => c.LMStudioUrl)
                    .NotEmpty()
                    .WithMessage("LM Studio URL is required (default: http://localhost:1234)")
                    .Must(BeValidUrl)
                    .WithMessage("Please enter a valid URL like http://localhost:1234");
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

            When(c => c.Provider == AIProvider.Claude || c.Provider == AIProvider.ClaudeMusic, () =>
            {
                RuleFor(c => c.ClaudeApiKey)
                    .NotEmpty()
                    .WithMessage("Claude API key is required (same as Anthropic key)");
            });
        }

        private bool BeValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return System.Uri.TryCreate(url, System.UriKind.Absolute, out var result)
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
        Anthropic = 4,    // Best reasoning
        Claude = 9,       // Claude via Anthropic API (standard)
        ClaudeMusic = 10  // Claude with music expertise
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

    public enum RecommendationType
    {
        Artists = 0,      // Recommend new artists to discover
        Albums = 1        // Recommend specific albums to add
    }

    public enum MusicMoodOptions
    {
        Any = 0,              // Use library analysis
        Energetic = 1,        // High energy, driving, upbeat
        Chill = 2,           // Relaxed, laid-back, mellow
        Dark = 3,            // Brooding, moody, atmospheric
        Emotional = 4,       // Moving, heartfelt, passionate
        Experimental = 5,    // Avant-garde, unconventional
        Danceable = 6,       // Rhythmic, groove-oriented
        Aggressive = 7,      // Intense, powerful, hard-hitting
        Peaceful = 8,        // Calm, serene, meditative
        Melancholic = 9,     // Sad, introspective, wistful
        Uplifting = 10,      // Inspiring, positive, joyful
        Mysterious = 11,     // Enigmatic, haunting, ethereal
        Playful = 12,        // Fun, whimsical, lighthearted
        Epic = 13,           // Grand, cinematic, dramatic
        Intimate = 14,       // Personal, close, tender
        Groovy = 15,         // Funky, rhythmic, infectious
        Nostalgic = 16       // Reminiscent, vintage, evocative
    }

    public enum MusicEraOptions
    {
        Any = 0,             // Use library analysis
        PreRock = 1,         // 1900s-1940s: Early jazz, blues
        EarlyRock = 2,       // 1950s: Birth of rock, early R&B
        SixtiesPop = 3,      // 1960s: British invasion, Motown
        SeventiesRock = 4,   // 1970s: Classic rock, progressive
        EightiesPop = 5,     // 1980s: New wave, synth-pop
        NinetiesAlt = 6,     // 1990s: Grunge, alternative
        MillenniumPop = 7,   // 2000s: Nu-metal, pop-punk
        TensSocial = 8,      // 2010s: Indie, streaming era
        Modern = 9,          // 2020s+: Current trends
        Vintage = 10,        // General vintage (50s-70s)
        Retro = 11,          // General retro (80s-90s)
        Contemporary = 12    // Last 3-5 years
    }


    public enum PerplexityModel
    {
        // Sonar models (with online search)
        Sonar_Large = 0,      // llama-3.1-sonar-large-128k-online - Best for online search
        Sonar_Small = 1,      // llama-3.1-sonar-small-128k-online - Faster, lower cost
        Sonar_Huge = 2,       // llama-3.1-sonar-huge-128k-online - Most powerful

        // Chat models (without online search)
        Llama_31_70B = 3,     // llama-3.1-70b-instruct - Large Llama model
        Llama_31_8B = 4,      // llama-3.1-8b-instruct - Smaller, faster Llama

        // Other supported models
        Claude_35_Sonnet = 5, // claude-3.5-sonnet - Anthropic's Claude
        Claude_35_Haiku = 6,  // claude-3.5-haiku - Faster Claude
        GPT4o_Mini = 7,       // gpt-4o-mini - OpenAI's efficient model
        Gemini_15_Pro = 8,    // gemini-1.5-pro-latest - Google's Gemini
        Gemini_15_Flash = 9,  // gemini-1.5-flash-latest - Faster Gemini
        Mistral_Large = 10    // mistral-large-latest - Mistral's best model
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

    public enum ClaudeModel
    {
        Claude35_Haiku = 0,   // claude-3-5-haiku-latest - Fast, efficient
        Claude35_Sonnet = 1,  // claude-3-5-sonnet-latest - Best balance
        Claude3_Opus = 2,     // claude-3-opus-20240229 - Most capable
        // Music-optimized variants (same models, different prompting)
        Music_Haiku = 3,      // Fast music discovery
        Music_Sonnet = 4,     // Deep music analysis
        Music_Opus = 5        // Ultimate music knowledge
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
            RecommendationType = RecommendationType.Albums; // Default to albums for specific additions
            TargetGenres = ""; // Empty by default - use library analysis
            MusicMood = new List<int>(); // Empty list by default - use library analysis
            MusicEra = new List<int>(); // Empty list by default - use library analysis
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

        [FieldDefinition(1, Label = "Recommendation Type", Type = FieldType.Select, SelectOptions = typeof(RecommendationType),
            HelpText = "Choose what to recommend:\n🎤 Artists - Discover new artists to explore their entire discography\n💿 Albums - Get specific album recommendations to add to your library")]
        public RecommendationType RecommendationType { get; set; }

        [FieldDefinition(2, Label = "Target Genres/Styles", Type = FieldType.Textbox,
            HelpText = "Optional: Specify genres or styles to explore (e.g. 'Jazz, Blues' or 'Electronic, Ambient')\nLeave blank to base on your current library")]
        public string TargetGenres { get; set; }

        [FieldDefinition(3, Label = "Music Moods", Type = FieldType.Select, SelectOptions = typeof(MusicMoodOptions),
            HelpText = "Select moods for music recommendations - Leave empty for library-based analysis")]
        public IEnumerable<int> MusicMood { get; set; }

        [FieldDefinition(4, Label = "Music Eras", Type = FieldType.Select, SelectOptions = typeof(MusicEraOptions),
            HelpText = "Select eras for music recommendations - Leave empty for library-based analysis")]
        public IEnumerable<int> MusicEra { get; set; }

        // ====== PROVIDER-SPECIFIC FIELDS ======
        // Each provider has its own fields that are shown/hidden based on selection

        // Ollama Settings (Provider == 0)
        private string _ollamaUrl;
        private string _ollamaModel;

        [FieldDefinition(15, Label = "[OLLAMA] Server URL", Type = FieldType.Textbox, HelpText = "Ollama API endpoint (default: http://localhost:11434)")]
        public string OllamaUrl
        {
            get => Provider == AIProvider.Ollama ? (string.IsNullOrEmpty(_ollamaUrl) ? BrainarrConstants.DefaultOllamaUrl : _ollamaUrl) : null;
            set => _ollamaUrl = value;
        }

        [FieldDefinition(11, Label = "[OLLAMA] Model", Type = FieldType.Select, SelectOptionsProviderAction = "getOllamaModels", HelpText = "Model name (e.g., llama3.1, mistral, qwen2.5) - Click 'Test' to auto-detect!")]
        public string OllamaModel
        {
            get => Provider == AIProvider.Ollama ? (string.IsNullOrEmpty(_ollamaModel) ? BrainarrConstants.DefaultOllamaModel : _ollamaModel) : null;
            set => _ollamaModel = value;
        }

        // LM Studio Settings (Provider == 1)
        private string _lmStudioUrl;
        private string _lmStudioModel;

        [FieldDefinition(20, Label = "[LM STUDIO] Server URL", Type = FieldType.Textbox, HelpText = "LM Studio server endpoint (default: http://localhost:1234)")]
        public string LMStudioUrl
        {
            get => Provider == AIProvider.LMStudio ? (string.IsNullOrEmpty(_lmStudioUrl) ? BrainarrConstants.DefaultLMStudioUrl : _lmStudioUrl) : null;
            set => _lmStudioUrl = value;
        }

        [FieldDefinition(21, Label = "[LM STUDIO] Model", Type = FieldType.Select, SelectOptionsProviderAction = "getLMStudioModels", HelpText = "Model identifier - Click 'Test' to auto-detect available models!")]
        public string LMStudioModel
        {
            get => Provider == AIProvider.LMStudio ? (string.IsNullOrEmpty(_lmStudioModel) ? BrainarrConstants.DefaultLMStudioModel : _lmStudioModel) : null;
            set => _lmStudioModel = value;
        }

        // Perplexity Settings (Provider == 2)
        private string _perplexityApiKey;
        private string _perplexityModel;

        [FieldDefinition(30, Label = "[PERPLEXITY] API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Perplexity API key from perplexity.ai/settings/api")]
        public string PerplexityApiKey
        {
            get => Provider == AIProvider.Perplexity ? _perplexityApiKey : null;
            set => _perplexityApiKey = value;
        }

        [FieldDefinition(31, Label = "[PERPLEXITY] Model", Type = FieldType.Select, SelectOptions = typeof(PerplexityModel), HelpText = "Choose model: Sonar Large (best), Small (faster), or Huge (most powerful)")]
        public string PerplexityModel
        {
            get => Provider == AIProvider.Perplexity ? _perplexityModel : null;
            set => _perplexityModel = value;
        }

        // OpenAI Settings (Provider == 3)
        private string _openAIApiKey;
        private string _openAIModel;

        [FieldDefinition(40, Label = "[OPENAI] API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your OpenAI API key from platform.openai.com/api-keys")]
        public string OpenAIApiKey
        {
            get => Provider == AIProvider.OpenAI ? _openAIApiKey : null;
            set => _openAIApiKey = value;
        }

        [FieldDefinition(41, Label = "[OPENAI] Model", Type = FieldType.Select, SelectOptions = typeof(OpenAIModel), HelpText = "GPT-4o Mini (cost-effective), GPT-4o (latest), or legacy models")]
        public string OpenAIModel
        {
            get => Provider == AIProvider.OpenAI ? _openAIModel : null;
            set => _openAIModel = value;
        }

        // Anthropic Settings (Provider == 4)
        private string _anthropicApiKey;
        private string _anthropicModel;

        [FieldDefinition(50, Label = "[ANTHROPIC] API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Anthropic API key from console.anthropic.com")]
        public string AnthropicApiKey
        {
            get => Provider == AIProvider.Anthropic ? _anthropicApiKey : null;
            set => _anthropicApiKey = value;
        }

        [FieldDefinition(51, Label = "[ANTHROPIC] Model", Type = FieldType.Select, SelectOptions = typeof(AnthropicModel), HelpText = "Claude 3.5 Haiku (fast), Sonnet (balanced), or Opus (most capable)")]
        public string AnthropicModel
        {
            get => Provider == AIProvider.Anthropic ? _anthropicModel : null;
            set => _anthropicModel = value;
        }

        // OpenRouter Settings (Provider == 5)
        private string _openRouterApiKey;
        private string _openRouterModel;

        [FieldDefinition(60, Label = "[OPENROUTER] API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your OpenRouter API key from openrouter.ai/keys - Access 200+ models!")]
        public string OpenRouterApiKey
        {
            get => Provider == AIProvider.OpenRouter ? _openRouterApiKey : null;
            set => _openRouterApiKey = value;
        }

        [FieldDefinition(61, Label = "[OPENROUTER] Model", Type = FieldType.Select, SelectOptions = typeof(OpenRouterModel), HelpText = "Choose from 200+ models - Claude, GPT, Llama, and more")]
        public string OpenRouterModel
        {
            get => Provider == AIProvider.OpenRouter ? _openRouterModel : null;
            set => _openRouterModel = value;
        }

        // DeepSeek Settings (Provider == 6)
        private string _deepSeekApiKey;
        private string _deepSeekModel;

        [FieldDefinition(70, Label = "[DEEPSEEK] API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your DeepSeek API key - 10-20x cheaper than GPT-4!")]
        public string DeepSeekApiKey
        {
            get => Provider == AIProvider.DeepSeek ? _deepSeekApiKey : null;
            set => _deepSeekApiKey = value;
        }

        [FieldDefinition(71, Label = "[DEEPSEEK] Model", Type = FieldType.Select, SelectOptions = typeof(DeepSeekModel), HelpText = "Chat (V3, best), Coder (for code), or Reasoner (R1 reasoning)")]
        public string DeepSeekModel
        {
            get => Provider == AIProvider.DeepSeek ? _deepSeekModel : null;
            set => _deepSeekModel = value;
        }

        // Gemini Settings (Provider == 7)
        private string _geminiApiKey;
        private string _geminiModel;

        [FieldDefinition(80, Label = "[GEMINI] API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Google Gemini API key - Free tier available!")]
        public string GeminiApiKey
        {
            get => Provider == AIProvider.Gemini ? _geminiApiKey : null;
            set => _geminiApiKey = value;
        }

        [FieldDefinition(81, Label = "[GEMINI] Model", Type = FieldType.Select, SelectOptions = typeof(GeminiModel), HelpText = "Flash (fast), Flash-8B (smaller), Pro (2M context), or 2.0 Flash")]
        public string GeminiModel
        {
            get => Provider == AIProvider.Gemini ? _geminiModel : null;
            set => _geminiModel = value;
        }

        // Groq Settings (Provider == 8)
        private string _groqApiKey;
        private string _groqModel;

        [FieldDefinition(90, Label = "[GROQ] API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Groq API key - Ultra-fast inference!")]
        public string GroqApiKey
        {
            get => Provider == AIProvider.Groq ? _groqApiKey : null;
            set => _groqApiKey = value;
        }

        [FieldDefinition(91, Label = "[GROQ] Model", Type = FieldType.Select, SelectOptions = typeof(GroqModel), HelpText = "Llama 3.3 70B (latest), Mixtral, or Gemma models")]
        public string GroqModel
        {
            get => Provider == AIProvider.Groq ? _groqModel : null;
            set => _groqModel = value;
        }

        // Claude Settings (Provider == 9 or 10)
        private string _claudeApiKey;
        private string _claudeModel;

        [FieldDefinition(100, Label = "[CLAUDE] API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Your Claude API key from console.anthropic.com - Same as Anthropic key")]
        public string ClaudeApiKey
        {
            get => (Provider == AIProvider.Claude || Provider == AIProvider.ClaudeMusic) ? _claudeApiKey : null;
            set => _claudeApiKey = value;
        }

        [FieldDefinition(101, Label = "[CLAUDE] Model", Type = FieldType.Select, SelectOptions = typeof(ClaudeModel),
            HelpText = "Choose model variant - Music versions have enhanced music knowledge")]
        public string ClaudeModel
        {
            get => (Provider == AIProvider.Claude || Provider == AIProvider.ClaudeMusic) ? _claudeModel : null;
            set => _claudeModel = value;
        }

        // ====== COMMON SETTINGS ======
        // Auto-detect model (show for all providers)
        [FieldDefinition(110, Label = "Auto-Detect Model", Type = FieldType.Checkbox, HelpText = "Automatically detect and select best available model")]
        public bool AutoDetectModel { get; set; }

        // Discovery Settings
        [FieldDefinition(120, Label = "Recommendations", Type = FieldType.Number,
            HelpText = "Number of albums per sync (1-50, default: 10)\n💡 Start with 5-10 and increase if you like the results")]
        public int MaxRecommendations { get; set; }

        [FieldDefinition(121, Label = "Discovery Mode", Type = FieldType.Select, SelectOptions = typeof(DiscoveryMode),
            HelpText = "How adventurous should recommendations be?\n• Similar: Stay close to current taste\n• Adjacent: Explore related genres\n• Exploratory: Discover new genres")]
        public DiscoveryMode DiscoveryMode { get; set; }

        [FieldDefinition(122, Label = "Library Sampling", Type = FieldType.Select, SelectOptions = typeof(SamplingStrategy),
            HelpText = "How much of your library to include in AI prompts\n• Minimal: Fast, less context (good for local models)\n• Balanced: Default, optimal balance\n• Comprehensive: Maximum context (best for GPT-4/Claude)")]
        public SamplingStrategy SamplingStrategy { get; set; }

        // ====== BACKWARD COMPATIBILITY ======
        // These properties maintain compatibility with existing configurations
        // Hidden fields that map to the provider-specific ones

        public string ConfigurationUrl
        {
            get => Provider switch
            {
                AIProvider.Ollama => OllamaUrl,
                AIProvider.LMStudio => LMStudioUrl,
                _ => "N/A - API Key based provider"
            };
            set
            {
                if (Provider == AIProvider.Ollama) OllamaUrl = value;
                else if (Provider == AIProvider.LMStudio) LMStudioUrl = value;
            }
        }

        public string ModelSelection
        {
            get => Provider switch
            {
                AIProvider.Ollama => _ollamaModel ?? BrainarrConstants.DefaultOllamaModel,
                AIProvider.LMStudio => _lmStudioModel ?? BrainarrConstants.DefaultLMStudioModel,
                AIProvider.Perplexity => _perplexityModel ?? "Sonar_Large",
                AIProvider.OpenAI => _openAIModel ?? "GPT4o_Mini",
                AIProvider.Anthropic => _anthropicModel ?? "Claude35_Haiku",
                AIProvider.OpenRouter => _openRouterModel ?? "Claude35_Haiku",
                AIProvider.DeepSeek => _deepSeekModel ?? "DeepSeek_Chat",
                AIProvider.Gemini => _geminiModel ?? "Gemini_15_Flash",
                AIProvider.Groq => _groqModel ?? "Llama33_70B",
                AIProvider.Claude => _claudeModel ?? "Claude35_Sonnet",
                AIProvider.ClaudeMusic => _claudeModel ?? "Music_Sonnet",
                _ => "Default"
            };
            set
            {
                switch (Provider)
                {
                    case AIProvider.Ollama: _ollamaModel = value; break;
                    case AIProvider.LMStudio: _lmStudioModel = value; break;
                    case AIProvider.Perplexity: _perplexityModel = value; break;
                    case AIProvider.OpenAI: _openAIModel = value; break;
                    case AIProvider.Anthropic: _anthropicModel = value; break;
                    case AIProvider.OpenRouter: _openRouterModel = value; break;
                    case AIProvider.DeepSeek: _deepSeekModel = value; break;
                    case AIProvider.Gemini: _geminiModel = value; break;
                    case AIProvider.Groq: _groqModel = value; break;
                    case AIProvider.Claude: _claudeModel = value; break;
                    case AIProvider.ClaudeMusic: _claudeModel = value; break;
                }
            }
        }

        public string ApiKey
        {
            get => Provider switch
            {
                AIProvider.Perplexity => _perplexityApiKey,
                AIProvider.OpenAI => _openAIApiKey,
                AIProvider.Anthropic => _anthropicApiKey,
                AIProvider.OpenRouter => _openRouterApiKey,
                AIProvider.DeepSeek => _deepSeekApiKey,
                AIProvider.Gemini => _geminiApiKey,
                AIProvider.Groq => _groqApiKey,
                AIProvider.Claude => _claudeApiKey,
                AIProvider.ClaudeMusic => _claudeApiKey,
                _ => null
            };
            set
            {
                switch (Provider)
                {
                    case AIProvider.Perplexity: _perplexityApiKey = value; break;
                    case AIProvider.OpenAI: _openAIApiKey = value; break;
                    case AIProvider.Anthropic: _anthropicApiKey = value; break;
                    case AIProvider.OpenRouter: _openRouterApiKey = value; break;
                    case AIProvider.DeepSeek: _deepSeekApiKey = value; break;
                    case AIProvider.Gemini: _geminiApiKey = value; break;
                    case AIProvider.Groq: _groqApiKey = value; break;
                    case AIProvider.Claude: _claudeApiKey = value; break;
                    case AIProvider.ClaudeMusic: _claudeApiKey = value; break;
                }
            }
        }

        // Lidarr Integration (Hidden from UI, set by Lidarr)
        public string BaseUrl
        {
            get => Provider == AIProvider.Ollama ? OllamaUrl : LMStudioUrl;
            set { /* Handled by provider-specific URLs */ }
        }

        // Model Detection Results (populated during test)
        public List<string> DetectedModels { get; set; } = new List<string>();

        // Provider Action Methods for Dynamic Dropdowns
        public List<FieldSelectOption> GetOllamaModels()
        {
            var options = new List<FieldSelectOption>
            {
                new FieldSelectOption { Name = "llama3.1", Value = 0, Order = 0 },
                new FieldSelectOption { Name = "llama3.1:8b", Value = 1, Order = 1 },
                new FieldSelectOption { Name = "mistral", Value = 2, Order = 2 },
                new FieldSelectOption { Name = "qwen2.5", Value = 3, Order = 3 },
                new FieldSelectOption { Name = "Custom Model", Value = 99, Order = 99 }
            };
            return options;
        }

        public List<FieldSelectOption> GetLMStudioModels()
        {
            var options = new List<FieldSelectOption>
            {
                new FieldSelectOption { Name = "TheBloke/Llama-2-7B-Chat-GGUF", Value = 0, Order = 0 },
                new FieldSelectOption { Name = "microsoft/DialoGPT-medium", Value = 1, Order = 1 },
                new FieldSelectOption { Name = "Auto-detect", Value = 98, Order = 98 },
                new FieldSelectOption { Name = "Custom Model", Value = 99, Order = 99 }
            };
            return options;
        }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}