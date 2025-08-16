using System;
using FluentValidation;
using NzbDrone.Core.Annotations;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers
{
    /// <summary>
    /// Settings for Ollama provider.
    /// </summary>
    public class OllamaProviderSettings : BaseProviderSettings<OllamaProviderSettings>
    {
        private string _url;
        private string _model;

        public OllamaProviderSettings()
        {
            _url = BrainarrConstants.DefaultOllamaUrl;
            _model = BrainarrConstants.DefaultOllamaModel;
        }

        [FieldDefinition(0, Label = "Ollama URL", Type = FieldType.Textbox,
            HelpText = "URL of your Ollama instance (default: http://localhost:11434)\n📝 Install: curl -fsSL https://ollama.com/install.sh | sh\nThen run: ollama pull llama3")]
        public string Url
        {
            get => string.IsNullOrEmpty(_url) ? BrainarrConstants.DefaultOllamaUrl : _url;
            set => _url = value;
        }

        [FieldDefinition(1, Label = "Ollama Model", Type = FieldType.Select, SelectOptionsProviderAction = "getOllamaOptions",
            HelpText = "⚠️ IMPORTANT: Click 'Test' first to populate models!\nRecommended: llama3 (best), mistral (fast), mixtral (quality)")]
        public string Model
        {
            get => string.IsNullOrEmpty(_model) ? BrainarrConstants.DefaultOllamaModel : _model;
            set => _model = value;
        }

        protected override AbstractValidator<OllamaProviderSettings> GetValidator()
        {
            return new OllamaProviderSettingsValidator();
        }
    }

    public class OllamaProviderSettingsValidator : AbstractValidator<OllamaProviderSettings>
    {
        public OllamaProviderSettingsValidator()
        {
            RuleFor(c => c.Url)
                .NotEmpty()
                .WithMessage("Ollama URL is required (default: http://localhost:11434)")
                .Must(BeValidUrl)
                .WithMessage("Please enter a valid URL like http://localhost:11434");
        }

        private bool BeValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var result)
                && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
        }
    }

    /// <summary>
    /// Settings for LM Studio provider.
    /// </summary>
    public class LMStudioProviderSettings : BaseProviderSettings<LMStudioProviderSettings>
    {
        private string _url;
        private string _model;

        public LMStudioProviderSettings()
        {
            _url = BrainarrConstants.DefaultLMStudioUrl;
            _model = BrainarrConstants.DefaultLMStudioModel;
        }

        [FieldDefinition(0, Label = "LM Studio URL", Type = FieldType.Textbox,
            HelpText = "URL of LM Studio server (default: http://localhost:1234)\n📝 Setup: Download from lmstudio.ai, load model, start server")]
        public string Url
        {
            get => string.IsNullOrEmpty(_url) ? BrainarrConstants.DefaultLMStudioUrl : _url;
            set => _url = value;
        }

        [FieldDefinition(1, Label = "LM Studio Model", Type = FieldType.Select, SelectOptionsProviderAction = "getLMStudioOptions",
            HelpText = "⚠️ IMPORTANT: Click 'Test' first to populate models!\nMake sure model is loaded in LM Studio")]
        public string Model
        {
            get => string.IsNullOrEmpty(_model) ? BrainarrConstants.DefaultLMStudioModel : _model;
            set => _model = value;
        }

        protected override AbstractValidator<LMStudioProviderSettings> GetValidator()
        {
            return new LMStudioProviderSettingsValidator();
        }
    }

    public class LMStudioProviderSettingsValidator : AbstractValidator<LMStudioProviderSettings>
    {
        public LMStudioProviderSettingsValidator()
        {
            RuleFor(c => c.Url)
                .NotEmpty()
                .WithMessage("LM Studio URL is required (default: http://localhost:1234)")
                .Must(BeValidUrl)
                .WithMessage("Please enter a valid URL like http://localhost:1234");
        }

        private bool BeValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var result)
                && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
        }
    }
}