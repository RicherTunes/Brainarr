using FluentValidation;

namespace Brainarr.Plugin.Configuration.Providers
{
    public class DeepSeekSettingsValidator : AbstractValidator<DeepSeekSettings>
    {
        public DeepSeekSettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty().WithMessage("DeepSeek API key is required");
            RuleFor(c => c.Temperature).InclusiveBetween(0.0, 2.0);
            RuleFor(c => c.MaxTokens).InclusiveBetween(100, 64000);
        }
    }

    public class DeepSeekSettings : ProviderSettings
    {
        private static readonly DeepSeekSettingsValidator Validator = new DeepSeekSettingsValidator();

        public DeepSeekSettings()
        {
            Model = DeepSeekModel.DeepSeek_Chat;
            Temperature = 0.7;
            MaxTokens = 2000;
            TopP = 0.9;
        }

        public string ApiKey { get; set; }
        public DeepSeekModel Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public double TopP { get; set; }
        public bool ReasoningMode { get; set; } = false;

        public override FluentValidation.Results.ValidationResult Validate()
        {
            return Validator.Validate(this);
        }
    }

    public enum DeepSeekModel
    {
        DeepSeek_Chat = 0,        // deepseek-chat - Latest V3, best overall
        DeepSeek_Coder = 1,       // deepseek-coder - Optimized for code
        DeepSeek_Reasoner = 2     // deepseek-reasoner - R1 reasoning model
    }
}