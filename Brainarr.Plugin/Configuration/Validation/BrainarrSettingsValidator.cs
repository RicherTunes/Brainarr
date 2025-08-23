using System;
using FluentValidation;
using NzbDrone.Core.ImportLists.Brainarr.Configuration.Enums;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration.Validation
{
    /// <summary>
    /// Validates Brainarr settings for correctness and security
    /// </summary>
    public class BrainarrSettingsValidator : AbstractValidator<BrainarrSettings>
    {
        public BrainarrSettingsValidator()
        {
            RuleFor(c => c.MaxRecommendations)
                .InclusiveBetween(BrainarrConstants.MinRecommendations, BrainarrConstants.MaxRecommendations)
                .WithMessage($"Recommendations must be between {BrainarrConstants.MinRecommendations} and {BrainarrConstants.MaxRecommendations}");

            When(c => c.Provider == AIProvider.Ollama, () =>
            {
                RuleFor(c => c.OllamaUrlRaw)
                    .NotEmpty()
                    .WithMessage("Ollama URL is required")
                    .Must(BeValidUrl)
                    .WithMessage("Please enter a valid URL like http://localhost:11434")
                    .OverridePropertyName("OllamaUrl"); 
            });

            When(c => c.Provider == AIProvider.LMStudio, () =>
            {
                RuleFor(c => c.LMStudioUrlRaw)
                    .NotEmpty()
                    .WithMessage("LM Studio URL is required")  
                    .Must(BeValidUrl)
                    .WithMessage("Please enter a valid URL like http://localhost:1234")
                    .OverridePropertyName("LMStudioUrl");
            });

            When(c => c.Provider == AIProvider.Perplexity, () =>
            {
                RuleFor(c => c.PerplexityApiKey)
                    .NotEmpty()
                    .WithMessage("Perplexity API key is required");
            });

            When(c => c.Provider == AIProvider.OpenAI, () =>
            {
                RuleFor(c => c.OpenAIApiKey)
                    .NotEmpty()
                    .WithMessage("OpenAI API key is required");
            });

            When(c => c.Provider == AIProvider.Anthropic, () =>
            {
                RuleFor(c => c.AnthropicApiKey)
                    .NotEmpty()
                    .WithMessage("Anthropic API key is required");
            });

            When(c => c.Provider == AIProvider.OpenRouter, () =>
            {
                RuleFor(c => c.OpenRouterApiKey)
                    .NotEmpty()
                    .WithMessage("OpenRouter API key is required");
            });

            When(c => c.Provider == AIProvider.DeepSeek, () =>
            {
                RuleFor(c => c.DeepSeekApiKey)
                    .NotEmpty()
                    .WithMessage("DeepSeek API key is required");
            });

            When(c => c.Provider == AIProvider.Gemini, () =>
            {
                RuleFor(c => c.GeminiApiKey)
                    .NotEmpty()
                    .WithMessage("Google Gemini API key is required");
            });

            When(c => c.Provider == AIProvider.Groq, () =>
            {
                RuleFor(c => c.GroqApiKey)
                    .NotEmpty()
                    .WithMessage("Groq API key is required");
            });
        }

        private bool BeValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return true; // Let NotEmpty() handle null/empty validation
            
            // Reject dangerous schemes upfront
            var lowerUrl = url.ToLowerInvariant();
            if (lowerUrl.StartsWith("javascript:") || 
                lowerUrl.StartsWith("file:") || 
                lowerUrl.StartsWith("ftp:") ||
                lowerUrl.StartsWith("data:") ||
                lowerUrl.StartsWith("vbscript:"))
            {
                return false;
            }
            
            // If no scheme provided, assume http:// and validate
            string urlToValidate = url;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                // Basic check for valid format before adding http://
                if (url.Contains(' ') || url.StartsWith('.') || url.EndsWith('.'))
                    return false;
                
                // Reject strings that don't look like URLs
                // Must have at least a dot or colon to be considered a valid URL/host
                if (!url.Contains('.') && !url.Contains(':'))
                    return false;
                    
                urlToValidate = "http://" + url;
            }
            
            return System.Uri.TryCreate(urlToValidate, System.UriKind.Absolute, out var result) 
                && (result.Scheme == System.Uri.UriSchemeHttp || result.Scheme == System.Uri.UriSchemeHttps);
        }
    }
}