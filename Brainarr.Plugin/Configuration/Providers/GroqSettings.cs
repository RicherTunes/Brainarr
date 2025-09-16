using FluentValidation;

namespace Brainarr.Plugin.Configuration.Providers
{
    public class GroqSettingsValidator : AbstractValidator<GroqSettings>
    {
        public GroqSettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty().WithMessage("Groq API key is required");
            RuleFor(c => c.Temperature).InclusiveBetween(0.0, 2.0);
            RuleFor(c => c.MaxTokens).InclusiveBetween(100, 32768);
        }
    }

    public class GroqSettings : ProviderSettings
    {
        private static readonly GroqSettingsValidator Validator = new GroqSettingsValidator();

        public GroqSettings()
        {
            Model = GroqModel.Llama33_70B_Versatile;
            Temperature = 0.7;
            MaxTokens = 2000;
            TopP = 0.9;
        }

        public string ApiKey { get; set; } = string.Empty;
        public GroqModel Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public double TopP { get; set; }
        public double FrequencyPenalty { get; set; } = 0.0;
        public double PresencePenalty { get; set; } = 0.0;

        public override FluentValidation.Results.ValidationResult Validate()
        {
            return Validator.Validate(this);
        }
    }

    public enum GroqModel
    {
        Llama33_70B_Versatile = 0,
        Llama33_70B_SpecDec = 1,
        DeepSeek_R1_Distill_L70B = 2,
        Llama31_8B_Instant = 3
    }

}
