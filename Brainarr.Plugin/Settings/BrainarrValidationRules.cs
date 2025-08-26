using System;
using System.Linq;
using FluentValidation;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Settings
{
    /// <summary>
    /// Validation rules for Brainarr settings.
    /// Separated from configuration for single responsibility principle.
    /// </summary>
    public class BrainarrValidationRules : AbstractValidator<BrainarrSettings>
    {
        public BrainarrValidationRules()
        {
            // Core validation rules
            RuleFor(c => c.MaxRecommendations)
                .InclusiveBetween(BrainarrConstants.MinRecommendations, BrainarrConstants.MaxRecommendations)
                .WithMessage($"Recommendations must be between {BrainarrConstants.MinRecommendations} and {BrainarrConstants.MaxRecommendations}");
            
            // Library analysis validation
            RuleFor(c => c.LibrarySampleSize)
                .InclusiveBetween(10, 500)
                .When(c => c.EnableLibraryAnalysis)
                .WithMessage("Library sample size must be between 10 and 500 artists");
            
            // Cache validation
            RuleFor(c => c.CacheExpirationHours)
                .InclusiveBetween(1, 168) // 1 hour to 1 week
                .When(c => c.EnableCaching)
                .WithMessage("Cache expiration must be between 1 hour and 1 week (168 hours)");
            
            RuleFor(c => c.MaxCacheSize)
                .InclusiveBetween(100, 10000)
                .When(c => c.EnableCaching)
                .WithMessage("Max cache size must be between 100 and 10,000 entries");
            
            // Iteration validation
            RuleFor(c => c.MaxIterations)
                .InclusiveBetween(1, 5)
                .When(c => c.EnableIterativeRefinement)
                .WithMessage("Max iterations must be between 1 and 5");
            
            // Hallucination detection validation
            RuleFor(c => c.HallucinationThreshold)
                .InclusiveBetween(0.1, 1.0)
                .When(c => c.EnableHallucinationDetection)
                .WithMessage("Hallucination threshold must be between 0.1 and 1.0");
            
            // Timeout validation
            RuleFor(c => c.RequestTimeoutSeconds)
                .InclusiveBetween(10, 300)
                .WithMessage("Request timeout must be between 10 and 300 seconds");
            
            RuleFor(c => c.MaxRetryAttempts)
                .InclusiveBetween(0, 10)
                .WithMessage("Max retry attempts must be between 0 and 10");
            
            // Provider-specific validation
            ConfigureOllamaValidation();
            ConfigureLMStudioValidation();
            ConfigureOpenAIValidation();
            ConfigureAnthropicValidation();
            ConfigureGeminiValidation();
            ConfigureGroqValidation();
            ConfigurePerplexityValidation();
            ConfigureDeepSeekValidation();
            ConfigureOpenRouterValidation();
        }
        
        private void ConfigureOllamaValidation()
        {
            When(c => c.Provider == AIProvider.Ollama, () =>
            {
                RuleFor(c => c.OllamaUrlRaw)
                    .Must(url => string.IsNullOrEmpty(url) || BeValidUrl(url))
                    .WithMessage("Please enter a valid URL like http://localhost:11434")
                    .OverridePropertyName("OllamaUrl");
                
                RuleFor(c => c.OllamaModel)
                    .NotEmpty()
                    .WithMessage("Ollama model name is required");
            });
        }
        
        private void ConfigureLMStudioValidation()
        {
            When(c => c.Provider == AIProvider.LMStudio, () =>
            {
                RuleFor(c => c.LMStudioUrlRaw)
                    .Must(url => string.IsNullOrEmpty(url) || BeValidUrl(url))
                    .WithMessage("Please enter a valid URL like http://localhost:1234")
                    .OverridePropertyName("LMStudioUrl");
                
                RuleFor(c => c.LMStudioModel)
                    .NotEmpty()
                    .WithMessage("LM Studio model name is required");
            });
        }
        
        private void ConfigureOpenAIValidation()
        {
            When(c => c.Provider == AIProvider.OpenAI, () =>
            {
                RuleFor(c => c.OpenAIApiKey)
                    .NotEmpty()
                    .WithMessage("OpenAI API key is required")
                    .Must(BeValidApiKey)
                    .WithMessage("OpenAI API key appears to be invalid");
                
                RuleFor(c => c.OpenAIModel)
                    .NotEmpty()
                    .WithMessage("OpenAI model selection is required")
                    .Must(BeValidOpenAIModel)
                    .WithMessage("Invalid OpenAI model selected");
            });
        }
        
        private void ConfigureAnthropicValidation()
        {
            When(c => c.Provider == AIProvider.Anthropic, () =>
            {
                RuleFor(c => c.AnthropicApiKey)
                    .NotEmpty()
                    .WithMessage("Anthropic API key is required")
                    .Must(BeValidApiKey)
                    .WithMessage("Anthropic API key appears to be invalid");
                
                RuleFor(c => c.AnthropicModel)
                    .NotEmpty()
                    .WithMessage("Anthropic model selection is required")
                    .Must(BeValidAnthropicModel)
                    .WithMessage("Invalid Anthropic model selected");
            });
        }
        
        private void ConfigureGeminiValidation()
        {
            When(c => c.Provider == AIProvider.Gemini, () =>
            {
                RuleFor(c => c.GeminiApiKey)
                    .NotEmpty()
                    .WithMessage("Gemini API key is required")
                    .Must(BeValidApiKey)
                    .WithMessage("Gemini API key appears to be invalid");
                
                RuleFor(c => c.GeminiModel)
                    .NotEmpty()
                    .WithMessage("Gemini model selection is required");
            });
        }
        
        private void ConfigureGroqValidation()
        {
            When(c => c.Provider == AIProvider.Groq, () =>
            {
                RuleFor(c => c.GroqApiKey)
                    .NotEmpty()
                    .WithMessage("Groq API key is required")
                    .Must(BeValidApiKey)
                    .WithMessage("Groq API key appears to be invalid");
                
                RuleFor(c => c.GroqModel)
                    .NotEmpty()
                    .WithMessage("Groq model selection is required");
            });
        }
        
        private void ConfigurePerplexityValidation()
        {
            When(c => c.Provider == AIProvider.Perplexity, () =>
            {
                RuleFor(c => c.PerplexityApiKey)
                    .NotEmpty()
                    .WithMessage("Perplexity API key is required")
                    .Must(BeValidApiKey)
                    .WithMessage("Perplexity API key appears to be invalid");
                
                RuleFor(c => c.PerplexityModel)
                    .NotEmpty()
                    .WithMessage("Perplexity model selection is required");
            });
        }
        
        private void ConfigureDeepSeekValidation()
        {
            When(c => c.Provider == AIProvider.DeepSeek, () =>
            {
                RuleFor(c => c.DeepSeekApiKey)
                    .NotEmpty()
                    .WithMessage("DeepSeek API key is required")
                    .Must(BeValidApiKey)
                    .WithMessage("DeepSeek API key appears to be invalid");
                
                RuleFor(c => c.DeepSeekModel)
                    .NotEmpty()
                    .WithMessage("DeepSeek model selection is required");
            });
        }
        
        private void ConfigureOpenRouterValidation()
        {
            When(c => c.Provider == AIProvider.OpenRouter, () =>
            {
                RuleFor(c => c.OpenRouterApiKey)
                    .NotEmpty()
                    .WithMessage("OpenRouter API key is required")
                    .Must(BeValidApiKey)
                    .WithMessage("OpenRouter API key appears to be invalid");
                
                RuleFor(c => c.OpenRouterModel)
                    .NotEmpty()
                    .WithMessage("OpenRouter model selection is required");
            });
        }
        
        // Validation helper methods
        private bool BeValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            
            return Uri.TryCreate(url, UriKind.Absolute, out var result) &&
                   (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
        }
        
        private bool BeValidApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return false;
            
            // Basic API key validation
            if (apiKey.Length < 20)
                return false;
            
            // Check for obvious test/invalid keys
            var invalidPatterns = new[] { "test", "demo", "xxx", "123", "sample", "key" };
            var lowerKey = apiKey.ToLowerInvariant();
            
            return !invalidPatterns.Any(pattern => lowerKey.Contains(pattern));
        }
        
        private bool BeValidOpenAIModel(string model)
        {
            var validModels = new[]
            {
                "gpt-4", "gpt-4-turbo", "gpt-4-turbo-preview",
                "gpt-3.5-turbo", "gpt-3.5-turbo-16k"
            };
            
            return validModels.Contains(model);
        }
        
        private bool BeValidAnthropicModel(string model)
        {
            var validModels = new[]
            {
                "claude-3-opus-20240229", "claude-3-sonnet-20240229",
                "claude-3-haiku-20240307", "claude-2.1", "claude-2.0"
            };
            
            return validModels.Contains(model);
        }
    }
}