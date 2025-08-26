using System;
using FluentValidation;

namespace Brainarr.Plugin.Configuration.Providers
{
    public class AnthropicSettingsValidator : AbstractValidator<AnthropicSettings>
    {
        public AnthropicSettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty();
            RuleFor(c => c.Model).NotEmpty();
            RuleFor(c => c.Temperature).InclusiveBetween(0.0, 1.0);
            RuleFor(c => c.MaxTokens).InclusiveBetween(100, 200000);
        }
    }

    public class AnthropicSettings : ProviderSettings
    {
        private static readonly AnthropicSettingsValidator Validator = new AnthropicSettingsValidator();

        public AnthropicSettings()
        {
            ApiEndpoint = "https://api.anthropic.com/v1";
            Model = "claude-3-5-sonnet-20241022";
            Temperature = 0.7;
            MaxTokens = 4000;
            Timeout = 30;
        }

        public string ApiKey { get; set; } = string.Empty;
        public string ApiEndpoint { get; set; }
        public string Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public double TopP { get; set; } = 0.9;
        public int TopK { get; set; } = 0;
        public int Timeout { get; set; }

        public override FluentValidation.Results.ValidationResult Validate()
        {
            return Validator.Validate(this);
        }
    }
}
