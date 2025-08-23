using System;
using FluentValidation;

namespace Brainarr.Plugin.Configuration.Providers
{
    public class LMStudioSettingsValidator : AbstractValidator<LMStudioSettings>
    {
        public LMStudioSettingsValidator()
        {
            RuleFor(c => c.Endpoint).NotEmpty().Must(BeAValidUrl);
            RuleFor(c => c.ModelName).NotEmpty();
            RuleFor(c => c.Temperature).InclusiveBetween(0.0, 2.0);
            RuleFor(c => c.MaxTokens).InclusiveBetween(100, 32000);
        }

        private bool BeAValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out _);
        }
    }

    public class LMStudioSettings : ProviderSettings
    {
        private static readonly LMStudioSettingsValidator Validator = new LMStudioSettingsValidator();

        public LMStudioSettings()
        {
            Endpoint = "http://localhost:1234";
            ModelName = "Auto-detected";
            Temperature = 0.7;
            MaxTokens = 2000;
            Timeout = 30;
        }

        public string Endpoint { get; set; }
        public string ModelName { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public double TopP { get; set; } = 0.9;
        public int TopK { get; set; } = 40;
        public int Timeout { get; set; }

        public override FluentValidation.Results.ValidationResult Validate()
        {
            return Validator.Validate(this);
        }
    }
}