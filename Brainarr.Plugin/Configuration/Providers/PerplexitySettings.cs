using FluentValidation;

namespace Brainarr.Plugin.Configuration.Providers
{
    public class PerplexitySettingsValidator : AbstractValidator<PerplexitySettings>
    {
        public PerplexitySettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty().WithMessage("Perplexity API key is required");
            RuleFor(c => c.Temperature).InclusiveBetween(0.0, 2.0);
            RuleFor(c => c.MaxTokens).InclusiveBetween(100, 128000);
        }
    }

    public class PerplexitySettings : ProviderSettings
    {
        private static readonly PerplexitySettingsValidator Validator = new PerplexitySettingsValidator();

        public PerplexitySettings()
        {
            Model = PerplexityModel.Sonar_Pro;
            Temperature = 0.7;
            MaxTokens = 2000;
            TopP = 0.9;
        }

        public string ApiKey { get; set; } = string.Empty;
        public PerplexityModel Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public double TopP { get; set; }
        public bool SearchRecency { get; set; } = true;

        public override FluentValidation.Results.ValidationResult Validate()
        {
            return Validator.Validate(this);
        }
    }

    public enum PerplexityModel
    {
        Sonar_Pro = 0,
        Sonar_Reasoning_Pro = 1,
        Sonar_Reasoning = 2,
        Sonar = 3,

        Llama31_70B_Instruct = 10,
        Llama31_8B_Instruct = 11,
        Mixtral_8x7B_Instruct = 12
    }

}
