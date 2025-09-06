using System;
using FluentValidation;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    // Minimal validator to satisfy configuration tests and runtime guardrails
    public class BrainarrSettingsValidator : AbstractValidator<BrainarrSettings>
    {
        public BrainarrSettingsValidator()
        {
            // Recommendation count bounds
            RuleFor(s => s.MaxRecommendations)
                .InclusiveBetween(
                    Configuration.BrainarrConstants.MinRecommendations,
                    Configuration.BrainarrConstants.MaxRecommendations)
                .WithMessage($"MaxRecommendations must be between {Configuration.BrainarrConstants.MinRecommendations} and {Configuration.BrainarrConstants.MaxRecommendations}");

            // Local provider URLs (validate only when non-empty)
            When(s => s.Provider == AIProvider.Ollama, () =>
            {
                RuleFor(s => s.OllamaUrl)
                    .Must(url => string.IsNullOrWhiteSpace(url) || Configuration.UrlValidator.IsValidLocalProviderUrl(url))
                    .WithMessage("Please enter a valid URL");
            });

            When(s => s.Provider == AIProvider.LMStudio, () =>
            {
                RuleFor(s => s.LMStudioUrl)
                    .Must(url => string.IsNullOrWhiteSpace(url) || Configuration.UrlValidator.IsValidLocalProviderUrl(url))
                    .WithMessage("Please enter a valid URL");
            });

            // Cloud providers: API key required when selected
            When(s => s.Provider == AIProvider.Perplexity, () =>
            {
                RuleFor(s => s.PerplexityApiKey).NotEmpty().WithMessage("PerplexityApiKey is required");
            });
            When(s => s.Provider == AIProvider.OpenAI, () =>
            {
                RuleFor(s => s.OpenAIApiKey).NotEmpty().WithMessage("OpenAIApiKey is required");
            });
            When(s => s.Provider == AIProvider.Anthropic, () =>
            {
                RuleFor(s => s.AnthropicApiKey).NotEmpty().WithMessage("AnthropicApiKey is required");
            });
            When(s => s.Provider == AIProvider.OpenRouter, () =>
            {
                RuleFor(s => s.OpenRouterApiKey).NotEmpty().WithMessage("OpenRouterApiKey is required");
            });
            When(s => s.Provider == AIProvider.DeepSeek, () =>
            {
                RuleFor(s => s.DeepSeekApiKey).NotEmpty().WithMessage("DeepSeekApiKey is required");
            });
            When(s => s.Provider == AIProvider.Gemini, () =>
            {
                RuleFor(s => s.GeminiApiKey).NotEmpty().WithMessage("GeminiApiKey is required");
            });
            When(s => s.Provider == AIProvider.Groq, () =>
            {
                RuleFor(s => s.GroqApiKey).NotEmpty().WithMessage("GroqApiKey is required");
            });
        }

        private static bool BeValidUrlOrEmpty(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true; // use defaults

            // Accept http/https absolute URLs
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                       || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            }

            // If a scheme is present but not http/https, reject immediately (e.g., file:, javascript:)
            if (url.Contains("://")) return false;

            // Also accept host:port (no scheme); try with http:// prefix
            if (Uri.TryCreate("http://" + url, UriKind.Absolute, out var prefixed))
            {
                return prefixed.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                       || prefixed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
