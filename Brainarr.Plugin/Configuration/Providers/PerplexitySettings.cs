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
            Model = PerplexityModel.Sonar_Large;
            Temperature = 0.7;
            MaxTokens = 2000;
            TopP = 0.9;
        }

        public string ApiKey { get; set; }
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
        Sonar_Large = 0,  // llama-3.1-sonar-large-128k-online - Best for online search
        Sonar_Small = 1,  // llama-3.1-sonar-small-128k-online - Faster, lower cost
        Sonar_Huge = 2    // llama-3.1-sonar-huge-128k-online - Most powerful
    }
}