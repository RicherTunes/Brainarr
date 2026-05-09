using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists.Brainarr;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers
{
    /// <summary>
    /// Base class for cloud provider settings that require an API key.
    /// </summary>
    public abstract class CloudProviderSettings<T> : BaseProviderSettings<T> where T : CloudProviderSettings<T>
    {
        public abstract string ApiKey { get; set; }
        public abstract string ModelName { get; set; }

        public override string? GetApiKey() => ApiKey;
        public override string? GetModel() => ModelName;
        public override string? GetBaseUrl() => null; // Cloud providers don't have base URLs
    }

    /// <summary>
    /// Settings for Perplexity provider.
    /// </summary>
    public class PerplexityProviderSettings : CloudProviderSettings<PerplexityProviderSettings>
    {
        [FieldDefinition(0, Label = "Perplexity API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Your Perplexity API key")]
        public override string ApiKey { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "Perplexity Model", Type = FieldType.Select, SelectOptions = typeof(global::Brainarr.Plugin.Configuration.Providers.PerplexityModel),
            HelpText = "Select Perplexity model")]
        public override string ModelName { get; set; } = string.Empty;

        public override AIProvider ProviderType => AIProvider.Perplexity;

        protected override AbstractValidator<PerplexityProviderSettings> GetValidator()
        {
            return new PerplexityProviderSettingsValidator();
        }
    }

    public class PerplexityProviderSettingsValidator : AbstractValidator<PerplexityProviderSettings>
    {
        public PerplexityProviderSettingsValidator()
        {
            RuleFor(c => c.ApiKey)
                .NotEmpty()
                .WithMessage("Perplexity API key is required");
        }
    }

    /// <summary>
    /// Settings for OpenAI provider.
    /// </summary>
    public class OpenAIProviderSettings : CloudProviderSettings<OpenAIProviderSettings>
    {
        [FieldDefinition(0, Label = "OpenAI API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Your OpenAI API key")]
        public override string ApiKey { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "OpenAI Model", Type = FieldType.Select, SelectOptions = typeof(global::NzbDrone.Core.ImportLists.Brainarr.OpenAIModelKind),
            HelpText = "Select OpenAI model")]
        public override string ModelName { get; set; } = string.Empty;

        public override AIProvider ProviderType => AIProvider.OpenAI;

        protected override AbstractValidator<OpenAIProviderSettings> GetValidator()
        {
            return new OpenAIProviderSettingsValidator();
        }
    }

    public class OpenAIProviderSettingsValidator : AbstractValidator<OpenAIProviderSettings>
    {
        public OpenAIProviderSettingsValidator()
        {
            RuleFor(c => c.ApiKey)
                .NotEmpty()
                .WithMessage("OpenAI API key is required");
        }
    }

    /// <summary>
    /// Settings for Anthropic provider.
    /// </summary>
    public class AnthropicProviderSettings : CloudProviderSettings<AnthropicProviderSettings>
    {
        [FieldDefinition(0, Label = "Anthropic API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Your Anthropic API key")]
        public override string ApiKey { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "Anthropic Model", Type = FieldType.Select, SelectOptions = typeof(global::NzbDrone.Core.ImportLists.Brainarr.AnthropicModelKind),
            HelpText = "Select Anthropic model")]
        public override string ModelName { get; set; } = string.Empty;

        public override AIProvider ProviderType => AIProvider.Anthropic;

        protected override AbstractValidator<AnthropicProviderSettings> GetValidator()
        {
            return new AnthropicProviderSettingsValidator();
        }
    }

    public class AnthropicProviderSettingsValidator : AbstractValidator<AnthropicProviderSettings>
    {
        public AnthropicProviderSettingsValidator()
        {
            RuleFor(c => c.ApiKey)
                .NotEmpty()
                .WithMessage("Anthropic API key is required");
        }
    }

    /// <summary>
    /// Settings for OpenRouter provider.
    /// </summary>
    public class OpenRouterProviderSettings : CloudProviderSettings<OpenRouterProviderSettings>
    {
        [FieldDefinition(0, Label = "OpenRouter API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = " Get key at: https://openrouter.ai/keys\n✨ Access Claude, GPT-4, Gemini, Llama + 200 more models\n💡 Great for testing different models with one key")]
        public override string ApiKey { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "OpenRouter Model", Type = FieldType.Select, SelectOptions = typeof(global::NzbDrone.Core.ImportLists.Brainarr.OpenRouterModelKind),
            HelpText = "Select model - Access Claude, GPT, Gemini, DeepSeek and more.\nNote: If Thinking Mode (Advanced) is Auto/On and the selected model is Anthropic/Claude, Brainarr will automatically use the ':thinking' variant.")]
        public override string ModelName { get; set; } = string.Empty;

        public override AIProvider ProviderType => AIProvider.OpenRouter;

        protected override AbstractValidator<OpenRouterProviderSettings> GetValidator()
        {
            return new OpenRouterProviderSettingsValidator();
        }
    }

    public class OpenRouterProviderSettingsValidator : AbstractValidator<OpenRouterProviderSettings>
    {
        public OpenRouterProviderSettingsValidator()
        {
            RuleFor(c => c.ApiKey)
                .NotEmpty()
                .WithMessage("OpenRouter API key is required");
        }
    }

    /// <summary>
    /// Settings for DeepSeek provider.
    /// </summary>
    public class DeepSeekProviderSettings : CloudProviderSettings<DeepSeekProviderSettings>
    {
        [FieldDefinition(0, Label = "DeepSeek API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Your DeepSeek API key - 10-20x cheaper than GPT-4")]
        public override string ApiKey { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "DeepSeek Model", Type = FieldType.Select, SelectOptions = typeof(global::NzbDrone.Core.ImportLists.Brainarr.DeepSeekModelKind),
            HelpText = "Select DeepSeek model")]
        public override string ModelName { get; set; } = string.Empty;

        public override AIProvider ProviderType => AIProvider.DeepSeek;

        protected override AbstractValidator<DeepSeekProviderSettings> GetValidator()
        {
            return new DeepSeekProviderSettingsValidator();
        }
    }

    public class DeepSeekProviderSettingsValidator : AbstractValidator<DeepSeekProviderSettings>
    {
        public DeepSeekProviderSettingsValidator()
        {
            RuleFor(c => c.ApiKey)
                .NotEmpty()
                .WithMessage("DeepSeek API key is required");
        }
    }

    /// <summary>
    /// Settings for Google Gemini provider.
    /// </summary>
    public class GeminiProviderSettings : CloudProviderSettings<GeminiProviderSettings>
    {
        [FieldDefinition(0, Label = "Gemini API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "🆓 Get FREE key at: https://aistudio.google.com/apikey\n✨ Includes free tier - perfect for testing!\n📊 1M+ token context window\n⚠️ If you see 403 SERVICE_DISABLED, enable the Generative Language API for your Google Cloud project or use an AI Studio key.")]
        public override string ApiKey { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "Gemini Model", Type = FieldType.Select, SelectOptions = typeof(global::Brainarr.Plugin.Configuration.Providers.GeminiModel),
            HelpText = "Select Gemini model - Flash for speed, Pro for capability")]
        public override string ModelName { get; set; } = string.Empty;

        public override AIProvider ProviderType => AIProvider.Gemini;

        protected override AbstractValidator<GeminiProviderSettings> GetValidator()
        {
            return new GeminiProviderSettingsValidator();
        }
    }

    public class GeminiProviderSettingsValidator : AbstractValidator<GeminiProviderSettings>
    {
        public GeminiProviderSettingsValidator()
        {
            RuleFor(c => c.ApiKey)
                .NotEmpty()
                .WithMessage("Google Gemini API key is required");
        }
    }

    /// <summary>
    /// Settings for Groq provider.
    /// </summary>
    public class GroqProviderSettings : CloudProviderSettings<GroqProviderSettings>
    {
        [FieldDefinition(0, Label = "Groq API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Your Groq API key - 10x faster inference")]
        public override string ApiKey { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "Groq Model", Type = FieldType.Select, SelectOptions = typeof(global::Brainarr.Plugin.Configuration.Providers.GroqModel),
            HelpText = "Select Groq model - Llama for best results")]
        public override string ModelName { get; set; } = string.Empty;

        public override AIProvider ProviderType => AIProvider.Groq;

        protected override AbstractValidator<GroqProviderSettings> GetValidator()
        {
            return new GroqProviderSettingsValidator();
        }
    }

    public class GroqProviderSettingsValidator : AbstractValidator<GroqProviderSettings>
    {
        public GroqProviderSettingsValidator()
        {
            RuleFor(c => c.ApiKey)
                .NotEmpty()
                .WithMessage("Groq API key is required");
        }
    }

    /// <summary>
    /// Settings for Z.AI (Zhipu) GLM provider. OpenAI-compatible API at
    /// https://api.z.ai/api/paas/v4. Lineup verified against docs.z.ai as of
    /// May 2026: GLM-5.1 (flagship, 200K context, 128K output), GLM-5 (745B MoE),
    /// GLM-5-Turbo, GLM-4.7, GLM-4.6 (200K), GLM-4.5, GLM-4.5-Air (default),
    /// GLM-4-32B.
    /// </summary>
    public class ZaiGlmProviderSettings : CloudProviderSettings<ZaiGlmProviderSettings>
    {
        [FieldDefinition(0, Label = "Z.AI API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Z.AI (Zhipu) API key. Get one at https://z.ai/manage-apikey/apikey-list — supports GLM-5.1 / GLM-5 / GLM-4.x family.")]
        public override string ApiKey { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "GLM Model", Type = FieldType.Select, SelectOptions = typeof(global::NzbDrone.Core.ImportLists.Brainarr.ZaiGlmModelKind),
            HelpText = "GLM-5.1 = current flagship (long-horizon agent, 200K ctx). GLM-5 = 745B MoE. GLM-5-Turbo = fast/cheap. GLM-4.5-Air (default) = balanced cost/quality.")]
        public override string ModelName { get; set; } = string.Empty;

        public override AIProvider ProviderType => AIProvider.ZaiGlm;

        protected override AbstractValidator<ZaiGlmProviderSettings> GetValidator()
        {
            return new ZaiGlmProviderSettingsValidator();
        }
    }

    public class ZaiGlmProviderSettingsValidator : AbstractValidator<ZaiGlmProviderSettings>
    {
        public ZaiGlmProviderSettingsValidator()
        {
            RuleFor(c => c.ApiKey)
                .NotEmpty()
                .WithMessage("Z.AI API key is required. Get one at https://z.ai/manage-apikey/apikey-list");
        }
    }
}
