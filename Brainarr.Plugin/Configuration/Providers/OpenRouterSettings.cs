using FluentValidation;

namespace Brainarr.Plugin.Configuration.Providers
{
    public class OpenRouterSettingsValidator : AbstractValidator<OpenRouterSettings>
    {
        public OpenRouterSettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty().WithMessage("OpenRouter API key is required");
            RuleFor(c => c.Temperature).InclusiveBetween(0.0, 2.0);
            RuleFor(c => c.MaxTokens).InclusiveBetween(100, 200000);
        }
    }

    public class OpenRouterSettings : ProviderSettings
    {
        private static readonly OpenRouterSettingsValidator Validator = new OpenRouterSettingsValidator();

        public OpenRouterSettings()
        {
            Model = OpenRouterModel.Claude35_Sonnet;
            Temperature = 0.7;
            MaxTokens = 2000;
            TopP = 0.9;
        }

        public string ApiKey { get; set; }
        public OpenRouterModel Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public double TopP { get; set; }
        public int TopK { get; set; } = 40;

        public override FluentValidation.Results.ValidationResult Validate()
        {
            return Validator.Validate(this);
        }
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
}