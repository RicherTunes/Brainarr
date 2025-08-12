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
        Anthropic = 4     // Best reasoning
    }

    public enum DiscoveryMode
    {
        Similar = 0,      // Very similar to existing library
        Adjacent = 1,     // Related genres
        Exploratory = 2   // New genres to explore
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

        [FieldDefinition(1, Label = "Ollama URL", Type = FieldType.Textbox, 
            HelpText = "URL of your Ollama instance (default: http://localhost:11434)\n📝 Install: curl -fsSL https://ollama.com/install.sh | sh\nThen run: ollama pull llama3")]
        public string OllamaUrl 
        { 
            get => string.IsNullOrEmpty(_ollamaUrl) ? BrainarrConstants.DefaultOllamaUrl : _ollamaUrl;
            set => _ollamaUrl = value;
        }

        [FieldDefinition(2, Label = "Ollama Model", Type = FieldType.Select, SelectOptionsProviderAction = "getOllamaOptions", 
            HelpText = "⚠️ IMPORTANT: Click 'Test' first to populate models!\nRecommended: llama3 (best), mistral (fast), mixtral (quality)")]
        public string OllamaModel 
        { 
            get => string.IsNullOrEmpty(_ollamaModel) ? BrainarrConstants.DefaultOllamaModel : _ollamaModel;
            set => _ollamaModel = value;
        }

        // LM Studio Settings  
        [FieldDefinition(3, Label = "LM Studio URL", Type = FieldType.Textbox, 
            HelpText = "URL of LM Studio server (default: http://localhost:1234)\n📝 Setup: Download from lmstudio.ai, load model, start server")]
        public string LMStudioUrl 
        { 
            get => string.IsNullOrEmpty(_lmStudioUrl) ? BrainarrConstants.DefaultLMStudioUrl : _lmStudioUrl;
            set => _lmStudioUrl = value;
        }

        [FieldDefinition(4, Label = "LM Studio Model", Type = FieldType.Select, SelectOptionsProviderAction = "getLMStudioOptions", 
            HelpText = "⚠️ IMPORTANT: Click 'Test' first to populate models!\nMake sure model is loaded in LM Studio")]
        public string LMStudioModel 
        { 
            get => string.IsNullOrEmpty(_lmStudioModel) ? BrainarrConstants.DefaultLMStudioModel : _lmStudioModel;
            set => _lmStudioModel = value;
        }

        // Perplexity Settings
        [FieldDefinition(5, Label = "Perplexity API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Perplexity API key")]
        public string PerplexityApiKey { get; set; }
        
        [FieldDefinition(6, Label = "Perplexity Model", Type = FieldType.Select, SelectOptions = typeof(PerplexityModel), HelpText = "Select Perplexity model")]
        public string PerplexityModel { get; set; }

        // OpenAI Settings
        [FieldDefinition(7, Label = "OpenAI API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your OpenAI API key")]
        public string OpenAIApiKey { get; set; }
        
        [FieldDefinition(8, Label = "OpenAI Model", Type = FieldType.Select, SelectOptions = typeof(OpenAIModel), HelpText = "Select OpenAI model")]
        public string OpenAIModel { get; set; }

        // Anthropic Settings
        [FieldDefinition(9, Label = "Anthropic API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Anthropic API key")]
        public string AnthropicApiKey { get; set; }
        
        [FieldDefinition(10, Label = "Anthropic Model", Type = FieldType.Select, SelectOptions = typeof(AnthropicModel), HelpText = "Select Anthropic model")]
        public string AnthropicModel { get; set; }

        // OpenRouter Settings (Gateway to 200+ models)
        [FieldDefinition(11, Label = "OpenRouter API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, 
            HelpText = "📝 Get key at: https://openrouter.ai/keys\n✨ Access Claude, GPT-4, Gemini, Llama + 200 more models\n💡 Great for testing different models with one key")]
        public string OpenRouterApiKey { get; set; }
        
        [FieldDefinition(12, Label = "OpenRouter Model", Type = FieldType.Select, SelectOptions = typeof(OpenRouterModel), HelpText = "Select model - Access Claude, GPT, Gemini, DeepSeek and more")]
        public string OpenRouterModel { get; set; }

        // DeepSeek Settings (Ultra cost-effective)
        [FieldDefinition(13, Label = "DeepSeek API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your DeepSeek API key - 10-20x cheaper than GPT-4")]
        public string DeepSeekApiKey { get; set; }
        
        [FieldDefinition(14, Label = "DeepSeek Model", Type = FieldType.Select, SelectOptions = typeof(DeepSeekModel), HelpText = "Select DeepSeek model")]
        public string DeepSeekModel { get; set; }

        // Google Gemini Settings (Free tier + massive context)
        [FieldDefinition(15, Label = "Gemini API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, 
            HelpText = "🆓 Get FREE key at: https://aistudio.google.com/apikey\n✨ Includes free tier - perfect for testing!\n📊 1M+ token context window")]
        public string GeminiApiKey { get; set; }
        
        [FieldDefinition(16, Label = "Gemini Model", Type = FieldType.Select, SelectOptions = typeof(GeminiModel), HelpText = "Select Gemini model - Flash for speed, Pro for capability")]
        public string GeminiModel { get; set; }

        // Groq Settings (Ultra-fast inference)
        [FieldDefinition(17, Label = "Groq API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Groq API key - 10x faster inference")]
        public string GroqApiKey { get; set; }
        
        [FieldDefinition(18, Label = "Groq Model", Type = FieldType.Select, SelectOptions = typeof(GroqModel), HelpText = "Select Groq model - Llama for best results")]
        public string GroqModel { get; set; }

        // Auto-detect model (show for all providers)
        [FieldDefinition(19, Label = "Auto-Detect Model", Type = FieldType.Checkbox, HelpText = "Automatically detect and select best available model")]
        public bool AutoDetectModel { get; set; }

        // Discovery Settings
        [FieldDefinition(20, Label = "Recommendations", Type = FieldType.Number, 
            HelpText = "Number of albums per sync (1-50, default: 10)\n💡 Start with 5-10 and increase if you like the results")]
        public int MaxRecommendations { get; set; }

        [FieldDefinition(21, Label = "Discovery Mode", Type = FieldType.Select, SelectOptions = typeof(DiscoveryMode), 
            HelpText = "How adventurous should recommendations be?\n• Similar: Stay close to current taste\n• Adjacent: Explore related genres\n• Exploratory: Discover new genres")]
        public DiscoveryMode DiscoveryMode { get; set; }

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