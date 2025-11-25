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
            Model = GeminiModel.Gemini_25_Flash;
            Temperature = 0.7;
            MaxTokens = 2000;
            TopP = 0.95;
            TopK = 40;
        }

        public string ApiKey { get; set; } = string.Empty;
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
        Gemini_25_Pro = 0,
        Gemini_25_Flash = 1,
        Gemini_25_Flash_Lite = 2,
        Gemini_20_Flash = 3,
        Gemini_15_Flash = 4,
        Gemini_15_Flash_8B = 5,
        Gemini_15_Pro = 6
    }

}
