using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class BrainarrSettingsValidatorTestsV2
    {
        private readonly BrainarrSettingsValidator _validator = new();

        [Theory]
        [InlineData(AIProvider.Ollama, nameof(BrainarrSettings.OllamaUrl))]
        [InlineData(AIProvider.LMStudio, nameof(BrainarrSettings.LMStudioUrl))]
        public void LocalProvider_WithEmptyUrl_UsesDefaults(AIProvider provider, string property)
        {
            var settings = new BrainarrSettings
            {
                Provider = provider,
                MaxRecommendations = 10
            };

            var result = _validator.Validate(settings);

            result.IsValid.Should().BeTrue();
            result.Errors.Should().NotContain(e => e.PropertyName == property);
        }

        [Theory]
        [InlineData(AIProvider.Perplexity, nameof(BrainarrSettings.PerplexityApiKey))]
        [InlineData(AIProvider.OpenAI, nameof(BrainarrSettings.OpenAIApiKey))]
        [InlineData(AIProvider.Anthropic, nameof(BrainarrSettings.AnthropicApiKey))]
        [InlineData(AIProvider.OpenRouter, nameof(BrainarrSettings.OpenRouterApiKey))]
        [InlineData(AIProvider.DeepSeek, nameof(BrainarrSettings.DeepSeekApiKey))]
        [InlineData(AIProvider.Gemini, nameof(BrainarrSettings.GeminiApiKey))]
        [InlineData(AIProvider.Groq, nameof(BrainarrSettings.GroqApiKey))]
        public void CloudProvider_MissingApiKey_FailsValidation(AIProvider provider, string property)
        {
            var settings = BuildCloudProviderSettings(provider);

            var result = _validator.Validate(settings);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == property);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(51)]
        [InlineData(100)]
        public void RecommendationsOutOfRange_FailsValidation(int maxRecommendations)
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                MaxRecommendations = maxRecommendations
            };

            var result = _validator.Validate(settings);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(BrainarrSettings.MaxRecommendations));
        }

        [Theory]
        [InlineData("http://localhost:11434", true)]
        [InlineData("https://api.example.com", true)]
        [InlineData("not-a-url", false)]
        [InlineData("ftp://invalid.com", false)]
        [InlineData("", true)]
        public void UrlValidation_AlignsWithExpectations(string url, bool expected)
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = url,
                MaxRecommendations = 10
            };

            var result = _validator.Validate(settings);

            result.IsValid.Should().Be(expected);
        }

        [Fact]
        public void ValidSettings_ReturnsSuccess()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                MaxRecommendations = 20,
                DiscoveryMode = DiscoveryMode.Similar
            };

            var result = _validator.Validate(settings);

            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        private static BrainarrSettings BuildCloudProviderSettings(AIProvider provider)
        {
            return new BrainarrSettings
            {
                Provider = provider,
                MaxRecommendations = 10,
                PerplexityApiKey = string.Empty,
                OpenAIApiKey = string.Empty,
                AnthropicApiKey = string.Empty,
                OpenRouterApiKey = string.Empty,
                DeepSeekApiKey = string.Empty,
                GeminiApiKey = string.Empty,
                GroqApiKey = string.Empty
            };
        }
    }
}
