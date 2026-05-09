using System.Linq;
using FluentAssertions;
using FluentValidation.Results;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class BrainarrSettingsValidatorFastTests
    {
        [Fact]
        public void Ollama_ValidUrl_Passes()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                MaxRecommendations = BrainarrConstants.MinRecommendations
            };
            var validator = new BrainarrSettingsValidator();
            ValidationResult result = validator.Validate(settings);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void OpenAI_MissingApiKey_Fails()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                MaxRecommendations = BrainarrConstants.MinRecommendations
            };
            var validator = new BrainarrSettingsValidator();
            var result = validator.Validate(settings);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(BrainarrSettings.OpenAIApiKey));
        }

        [Theory]
        [InlineData(AIProvider.Ollama, nameof(BrainarrSettings.OllamaUrl))]
        [InlineData(AIProvider.LMStudio, nameof(BrainarrSettings.LMStudioUrl))]
        public void LocalProvider_BadUrl_ErrorMessage_ShowsValidExample(AIProvider provider, string propertyName)
        {
            // Wave 69 UX: pre-fix the error was just "Please enter a valid URL" —
            // doesn't tell the user what shape it should be. Local-provider URLs
            // have a very specific shape (http://host:port) and showing a working
            // example saves the user from typing 5 wrong variants.
            var settings = new BrainarrSettings
            {
                Provider = provider,
                MaxRecommendations = BrainarrConstants.MinRecommendations,
            };
            // Set the right URL property to an obviously-bogus value to trigger validation
            if (provider == AIProvider.Ollama) settings.OllamaUrl = "not://a-url";
            else settings.LMStudioUrl = "not://a-url";

            var validator = new BrainarrSettingsValidator();
            var result = validator.Validate(settings);

            result.IsValid.Should().BeFalse();
            var error = result.Errors.FirstOrDefault(e => e.PropertyName == propertyName);
            error.Should().NotBeNull();
            // Must show a concrete URL with scheme + host + port
            error!.ErrorMessage.Should().Contain("http://",
                because: "local-provider error should include a valid example URL with scheme");
        }

        [Theory]
        [InlineData(AIProvider.OpenAI, nameof(BrainarrSettings.OpenAIApiKey), "platform.openai.com")]
        [InlineData(AIProvider.Anthropic, nameof(BrainarrSettings.AnthropicApiKey), "console.anthropic.com")]
        [InlineData(AIProvider.Gemini, nameof(BrainarrSettings.GeminiApiKey), "aistudio.google.com")]
        [InlineData(AIProvider.Groq, nameof(BrainarrSettings.GroqApiKey), "console.groq.com")]
        [InlineData(AIProvider.Perplexity, nameof(BrainarrSettings.PerplexityApiKey), "perplexity.ai")]
        [InlineData(AIProvider.DeepSeek, nameof(BrainarrSettings.DeepSeekApiKey), "platform.deepseek.com")]
        [InlineData(AIProvider.OpenRouter, nameof(BrainarrSettings.OpenRouterApiKey), "openrouter.ai")]
        public void MissingApiKey_ErrorMessage_PointsToProviderConsole(AIProvider provider, string propertyName, string expectedHost)
        {
            // Wave 66 UX: previously the error was just "OpenAIApiKey is required",
            // forcing the user to Google "where do I get an OpenAI API key". The
            // message now embeds the canonical provider console URL so the user
            // can copy-paste it directly into a browser.
            var settings = new BrainarrSettings
            {
                Provider = provider,
                MaxRecommendations = BrainarrConstants.MinRecommendations,
            };
            var validator = new BrainarrSettingsValidator();
            var result = validator.Validate(settings);

            result.IsValid.Should().BeFalse();
            var error = result.Errors.FirstOrDefault(e => e.PropertyName == propertyName);
            error.Should().NotBeNull();
            error!.ErrorMessage.Should().Contain(expectedHost,
                because: $"{provider} API key error should point users to the provider console");
        }
    }
}
