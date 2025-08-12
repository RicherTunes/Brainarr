using System;
using System.Collections.Generic;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Core.Annotations;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    /// <summary>
    /// Base class for provider-specific configuration.
    /// Enables extensible provider settings without modifying core configuration.
    /// </summary>
    public abstract class ProviderConfiguration
    {
        /// <summary>
        /// Gets or sets whether this provider is enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the priority order for this provider in the failover chain.
        /// Lower numbers have higher priority.
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// Gets or sets the maximum number of retries for this provider.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Gets or sets the timeout in seconds for requests to this provider.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Gets or sets rate limiting configuration for this provider.
        /// </summary>
        public RateLimitConfiguration RateLimit { get; set; } = new RateLimitConfiguration();

        /// <summary>
        /// Gets or sets custom headers to send with requests to this provider.
        /// </summary>
        public Dictionary<string, string> CustomHeaders { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Validates the provider configuration.
        /// </summary>
        public abstract ValidationResult Validate();

        /// <summary>
        /// Gets the provider type identifier.
        /// </summary>
        public abstract string ProviderType { get; }
    }

    /// <summary>
    /// Rate limiting configuration for a provider.
    /// </summary>
    public class RateLimitConfiguration
    {
        /// <summary>
        /// Gets or sets the maximum requests per minute.
        /// </summary>
        public int RequestsPerMinute { get; set; } = 60;

        /// <summary>
        /// Gets or sets the burst size for rate limiting.
        /// </summary>
        public int BurstSize { get; set; } = 10;

        /// <summary>
        /// Gets or sets whether rate limiting is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Ollama provider specific configuration.
    /// </summary>
    public class OllamaProviderConfiguration : ProviderConfiguration
    {
        public override string ProviderType => "Ollama";

        [FieldDefinition(1, Label = "Ollama URL", Type = FieldType.Textbox, HelpText = "URL of your Ollama instance")]
        public string Url { get; set; } = BrainarrConstants.DefaultOllamaUrl;

        [FieldDefinition(2, Label = "Model", Type = FieldType.Textbox, HelpText = "Ollama model to use")]
        public string Model { get; set; } = BrainarrConstants.DefaultOllamaModel;

        [FieldDefinition(3, Label = "Temperature", Type = FieldType.Number, HelpText = "Model temperature (0.0-1.0)", Advanced = true)]
        public double Temperature { get; set; } = 0.7;

        [FieldDefinition(4, Label = "Top P", Type = FieldType.Number, HelpText = "Top P sampling", Advanced = true)]
        public double TopP { get; set; } = 0.9;

        [FieldDefinition(5, Label = "Max Tokens", Type = FieldType.Number, HelpText = "Maximum response tokens", Advanced = true)]
        public int MaxTokens { get; set; } = 2000;

        [FieldDefinition(6, Label = "Stream Responses", Type = FieldType.Checkbox, HelpText = "Enable streaming responses", Advanced = true)]
        public bool StreamResponses { get; set; } = false;

        public override ValidationResult Validate()
        {
            var validator = new OllamaProviderConfigurationValidator();
            return validator.Validate(this);
        }
    }

    /// <summary>
    /// LM Studio provider specific configuration.
    /// </summary>
    public class LMStudioProviderConfiguration : ProviderConfiguration
    {
        public override string ProviderType => "LMStudio";

        [FieldDefinition(1, Label = "LM Studio URL", Type = FieldType.Textbox, HelpText = "URL of your LM Studio instance")]
        public string Url { get; set; } = BrainarrConstants.DefaultLMStudioUrl;

        [FieldDefinition(2, Label = "Model", Type = FieldType.Textbox, HelpText = "LM Studio model identifier")]
        public string Model { get; set; } = BrainarrConstants.DefaultLMStudioModel;

        [FieldDefinition(3, Label = "Temperature", Type = FieldType.Number, HelpText = "Model temperature (0.0-1.0)", Advanced = true)]
        public double Temperature { get; set; } = 0.7;

        [FieldDefinition(4, Label = "Max Tokens", Type = FieldType.Number, HelpText = "Maximum response tokens", Advanced = true)]
        public int MaxTokens { get; set; } = 2000;

        public override ValidationResult Validate()
        {
            var validator = new LMStudioProviderConfigurationValidator();
            return validator.Validate(this);
        }
    }

    /// <summary>
    /// Validator for Ollama provider configuration.
    /// </summary>
    public class OllamaProviderConfigurationValidator : AbstractValidator<OllamaProviderConfiguration>
    {
        public OllamaProviderConfigurationValidator()
        {
            RuleFor(c => c.Url)
                .NotEmpty().WithMessage("Ollama URL is required")
                .Must(BeValidUrl).WithMessage("Must be a valid URL");

            RuleFor(c => c.Model)
                .NotEmpty().WithMessage("Model is required");

            RuleFor(c => c.Temperature)
                .InclusiveBetween(0.0, 1.0).WithMessage("Temperature must be between 0.0 and 1.0");

            RuleFor(c => c.TopP)
                .InclusiveBetween(0.0, 1.0).WithMessage("Top P must be between 0.0 and 1.0");

            RuleFor(c => c.MaxTokens)
                .GreaterThan(0).WithMessage("Max tokens must be greater than 0")
                .LessThanOrEqualTo(10000).WithMessage("Max tokens cannot exceed 10000");
        }

        private bool BeValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var result) ||
                   Uri.TryCreate("http://" + url, UriKind.Absolute, out result);
        }
    }

    /// <summary>
    /// Validator for LM Studio provider configuration.
    /// </summary>
    public class LMStudioProviderConfigurationValidator : AbstractValidator<LMStudioProviderConfiguration>
    {
        public LMStudioProviderConfigurationValidator()
        {
            RuleFor(c => c.Url)
                .NotEmpty().WithMessage("LM Studio URL is required")
                .Must(BeValidUrl).WithMessage("Must be a valid URL");

            RuleFor(c => c.Model)
                .NotEmpty().WithMessage("Model is required");

            RuleFor(c => c.Temperature)
                .InclusiveBetween(0.0, 1.0).WithMessage("Temperature must be between 0.0 and 1.0");

            RuleFor(c => c.MaxTokens)
                .GreaterThan(0).WithMessage("Max tokens must be greater than 0")
                .LessThanOrEqualTo(10000).WithMessage("Max tokens cannot exceed 10000");
        }

        private bool BeValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var result) ||
                   Uri.TryCreate("http://" + url, UriKind.Absolute, out result);
        }
    }
}