using System;
using FluentValidation;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using FluentValidation.Validators;

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

            // AI request timeout bounds
            RuleFor(s => s.AIRequestTimeoutSeconds)
                .InclusiveBetween(
                    Configuration.BrainarrConstants.MinAITimeout,
                    Configuration.BrainarrConstants.MaxAITimeout)
                .WithMessage($"AI Request Timeout must be between {Configuration.BrainarrConstants.MinAITimeout} and {Configuration.BrainarrConstants.MaxAITimeout} seconds");

            // Local provider URLs (validate only when non-empty). Wave 69 UX:
            // examples in the error message save the user from typing 5 wrong
            // variants when their first guess at the URL shape is off.
            When(s => s.Provider == AIProvider.Ollama, () =>
            {
                RuleFor(s => s.OllamaUrl)
                    .Must(url => string.IsNullOrWhiteSpace(url) || Configuration.UrlValidator.IsValidLocalProviderUrl(url))
                    .WithMessage("Please enter a valid URL for Ollama (scheme + host + port). Example: http://localhost:11434 (or http://192.168.1.10:11434 for a remote server).");
            });

            When(s => s.Provider == AIProvider.LMStudio, () =>
            {
                RuleFor(s => s.LMStudioUrl)
                    .Must(url => string.IsNullOrWhiteSpace(url) || Configuration.UrlValidator.IsValidLocalProviderUrl(url))
                    .WithMessage("Please enter a valid URL for LM Studio (scheme + host + port). Example: http://localhost:1234 (LM Studio's default OpenAI-compatible endpoint).");
            });

            // Cloud providers: API key required when selected. Wave 66 UX: messages
            // embed the canonical provider console URL so users can copy-paste it
            // straight into a browser instead of Googling "where do I get an X API key".
            When(s => s.Provider == AIProvider.Perplexity, () =>
            {
                RuleFor(s => s.PerplexityApiKey).NotEmpty().WithMessage(
                    "Perplexity API key is required. Get one at https://www.perplexity.ai/settings/api");
            });
            When(s => s.Provider == AIProvider.OpenAI, () =>
            {
                RuleFor(s => s.OpenAIApiKey).NotEmpty().WithMessage(
                    "OpenAI API key is required. Get one at https://platform.openai.com/account/api-keys");
            });
            When(s => s.Provider == AIProvider.Anthropic, () =>
            {
                RuleFor(s => s.AnthropicApiKey).NotEmpty().WithMessage(
                    "Anthropic API key is required. Get one at https://console.anthropic.com/settings/keys");
            });
            When(s => s.Provider == AIProvider.OpenRouter, () =>
            {
                RuleFor(s => s.OpenRouterApiKey).NotEmpty().WithMessage(
                    "OpenRouter API key is required. Get one at https://openrouter.ai/keys");
            });
            When(s => s.Provider == AIProvider.DeepSeek, () =>
            {
                RuleFor(s => s.DeepSeekApiKey).NotEmpty().WithMessage(
                    "DeepSeek API key is required. Get one at https://platform.deepseek.com/api_keys");
            });
            When(s => s.Provider == AIProvider.Gemini, () =>
            {
                RuleFor(s => s.GeminiApiKey).NotEmpty().WithMessage(
                    "Gemini API key is required. Get one at https://aistudio.google.com/app/apikey");
            });
            When(s => s.Provider == AIProvider.Groq, () =>
            {
                RuleFor(s => s.GroqApiKey).NotEmpty().WithMessage(
                    "Groq API key is required. Get one at https://console.groq.com/keys");
            });
            When(s => s.Provider == AIProvider.ZaiGlm, () =>
            {
                RuleFor(s => s.ZaiGlmApiKey).NotEmpty().WithMessage(
                    "Z.AI (Zhipu) API key is required. Get one at https://z.ai/manage-apikey/apikey-list");
            });
            RuleFor(s => s.SamplingShape)
                .Custom((shape, context) =>
                {
                    var effective = shape ?? SamplingShape.Default;

                    ValidateModeShape(effective.Artist, "SamplingShape.Artist", context);
                    ValidateModeShape(effective.Album, "SamplingShape.Album", context);

                    if (effective.MaxAlbumsPerGroupFloor < 0 || effective.MaxAlbumsPerGroupFloor > 10)
                    {
                        context.AddFailure("SamplingShape.MaxAlbumsPerGroupFloor must be between 0 and 10.");
                    }

                    if (effective.MaxRelaxedInflation < 1.0 || effective.MaxRelaxedInflation > 5.0)
                    {
                        context.AddFailure("SamplingShape.MaxRelaxedInflation must be between 1.0 and 5.0.");
                    }
                });

            // Wave 98 UX: explicit messages with the concrete bounds + a hint at
            // why the bound exists, so a user setting an out-of-range value sees
            // *why* it was rejected rather than just "invalid".
            RuleFor(s => s.PlanCacheCapacity)
                .InclusiveBetween(CacheSettings.MinCapacity, CacheSettings.MaxCapacity)
                .WithMessage($"Plan cache capacity must be between {CacheSettings.MinCapacity} and {CacheSettings.MaxCapacity} entries. Higher values use more memory; lower values cause more cache misses on similar prompts.");

            RuleFor(s => s.PlanCacheTtlMinutes)
                .InclusiveBetween(CacheSettings.MinTtlMinutes, CacheSettings.MaxTtlMinutes)
                .WithMessage($"Plan cache TTL must be between {CacheSettings.MinTtlMinutes} and {CacheSettings.MaxTtlMinutes} minutes. Shorter TTLs keep results fresh; longer TTLs reduce AI provider calls.");
        }

        private static void ValidateModeShape(SamplingShape.ModeShape mode, string path, CustomContext context)
        {
            ValidateDistribution(mode.Similar, $"{path}.Similar", context);
            ValidateDistribution(mode.Adjacent, $"{path}.Adjacent", context);
            ValidateDistribution(mode.Exploratory, $"{path}.Exploratory", context);
        }

        private static void ValidateDistribution(SamplingShape.ModeDistribution distribution, string path, CustomContext context)
        {
            if (distribution.TopPercent < 0 || distribution.TopPercent > 100)
            {
                context.AddFailure($"{path}.TopPercent must be between 0 and 100.");
            }

            if (distribution.RecentPercent < 0 || distribution.RecentPercent > 100)
            {
                context.AddFailure($"{path}.RecentPercent must be between 0 and 100.");
            }

            if (distribution.TopPercent + distribution.RecentPercent > 100)
            {
                context.AddFailure($"{path}.TopPercent + RecentPercent cannot exceed 100.");
            }
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
