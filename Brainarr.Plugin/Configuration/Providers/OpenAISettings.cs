using System;
using FluentValidation;

namespace Brainarr.Plugin.Configuration.Providers
{
    public class OpenAISettingsValidator : AbstractValidator<OpenAISettings>
    {
        public OpenAISettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty();
            RuleFor(c => c.Model).NotEmpty();
            RuleFor(c => c.Temperature).InclusiveBetween(0.0, 2.0);
            RuleFor(c => c.MaxTokens).InclusiveBetween(100, 128000);
        }
    }

    public class OpenAISettings : ProviderSettings
    {
        private static readonly OpenAISettingsValidator Validator = new OpenAISettingsValidator();

        public OpenAISettings()
        {
            ApiEndpoint = "https://api.openai.com/v1";
            Model = "gpt-4o-mini";
            Temperature = 0.7;
            MaxTokens = 2000;
            Timeout = 30;
        }

        public string ApiKey { get; set; }
        public string ApiEndpoint { get; set; }
        public string Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public double TopP { get; set; } = 1.0;
        public double FrequencyPenalty { get; set; } = 0.0;
        public double PresencePenalty { get; set; } = 0.0;
        public int Timeout { get; set; }

        public override FluentValidation.Results.ValidationResult Validate()
        {
            return Validator.Validate(this);
        }
    }
}
