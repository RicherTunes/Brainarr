using FluentValidation;

namespace Brainarr.Plugin.Configuration.Providers
{
    public class GeminiSettingsValidator : AbstractValidator<GeminiSettings>
    {
        public GeminiSettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty().WithMessage("Google Gemini API key is required");
            RuleFor(c => c.Temperature).InclusiveBetween(0.0, 2.0);
            RuleFor(c => c.MaxTokens).InclusiveBetween(100, 2000000);
        }
    }

    public class GeminiSettings : ProviderSettings
    {
        private static readonly GeminiSettingsValidator Validator = new GeminiSettingsValidator();

        public GeminiSettings()
        {
            Model = GeminiModel.Gemini_15_Flash;
            Temperature = 0.7;
            MaxTokens = 2000;
            TopP = 0.95;
            TopK = 40;
        }

        public string ApiKey { get; set; }
        public GeminiModel Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public double TopP { get; set; }
        public int TopK { get; set; }
        public string SafetyLevel { get; set; } = "BLOCK_MEDIUM_AND_ABOVE";

        public override FluentValidation.Results.ValidationResult Validate()
        {
            return Validator.Validate(this);
        }
    }

    public enum GeminiModel
    {
        Gemini_15_Flash = 0,      // gemini-1.5-flash - Fast, 1M context
        Gemini_15_Flash_8B = 1,   // gemini-1.5-flash-8b - Smaller, faster
        Gemini_15_Pro = 2,        // gemini-1.5-pro - Most capable, 2M context
        Gemini_20_Flash = 3       // gemini-2.0-flash-exp - Latest experimental
    }
}