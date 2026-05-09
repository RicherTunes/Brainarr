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
