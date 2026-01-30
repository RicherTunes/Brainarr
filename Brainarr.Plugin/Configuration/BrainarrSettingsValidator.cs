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

            // Subscription-based providers: validate credentials file or CLI path
            When(s => s.Provider == AIProvider.ClaudeCodeSubscription, () =>
            {
                RuleFor(s => s.ClaudeCodeCredentialsPath)
                    .Must(path => !string.IsNullOrEmpty(path) && System.IO.File.Exists(Services.SubscriptionCredentialLoader.ExpandPath(path)))
                    .WithMessage("Claude Code credentials file not found. Run 'claude login' to authenticate, or verify the credentials path is correct.");

                RuleFor(s => s.ClaudeCodeCliPath)
                    .Must(path => string.IsNullOrEmpty(path) || System.IO.File.Exists(path))
                    .WithMessage("Claude CLI path specified but file not found. Leave empty for auto-detection or verify the path is correct.");
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

            RuleFor(s => s.PlanCacheCapacity)
                .InclusiveBetween(CacheSettings.MinCapacity, CacheSettings.MaxCapacity);

            RuleFor(s => s.PlanCacheTtlMinutes)
                .InclusiveBetween(CacheSettings.MinTtlMinutes, CacheSettings.MaxTtlMinutes);
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
