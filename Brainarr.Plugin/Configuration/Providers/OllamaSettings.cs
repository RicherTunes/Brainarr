using System;
using FluentValidation;

namespace Brainarr.Plugin.Configuration.Providers
{
    public class OllamaSettingsValidator : AbstractValidator<OllamaSettings>
    {
        public OllamaSettingsValidator()
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

    public class OllamaSettings : ProviderSettings
    {
        private static readonly OllamaSettingsValidator Validator = new OllamaSettingsValidator();

        public OllamaSettings()
        {
            Endpoint = "http://localhost:11434";
            ModelName = "llama3.2";
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
