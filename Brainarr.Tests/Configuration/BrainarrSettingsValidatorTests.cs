using System;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace NzbDrone.Core.ImportLists.Brainarr.Tests.Configuration
{
    [Trait("Category", "Unit")]
    public class BrainarrSettingsValidatorTests
    {
        [Fact]
        public void Defaults_Are_Valid()
        {
            var settings = new BrainarrSettings();
            var validator = new BrainarrSettingsValidator();

            var result = validator.Validate(settings);

            result.IsValid.Should().BeTrue(result.ToString());
        }

        [Theory]
        [InlineData(AIProvider.OpenAI, nameof(BrainarrSettings.OpenAIApiKey))]
        [InlineData(AIProvider.Anthropic, nameof(BrainarrSettings.AnthropicApiKey))]
        [InlineData(AIProvider.Perplexity, nameof(BrainarrSettings.PerplexityApiKey))]
        [InlineData(AIProvider.OpenRouter, nameof(BrainarrSettings.OpenRouterApiKey))]
        [InlineData(AIProvider.DeepSeek, nameof(BrainarrSettings.DeepSeekApiKey))]
        [InlineData(AIProvider.Gemini, nameof(BrainarrSettings.GeminiApiKey))]
        [InlineData(AIProvider.Groq, nameof(BrainarrSettings.GroqApiKey))]
        public void Selected_Provider_Requires_ApiKey(AIProvider provider, string propertyName)
        {
            var settings = new BrainarrSettings { Provider = provider };
            // Ensure all keys are null/empty
            settings.OpenAIApiKey = null;
            settings.AnthropicApiKey = null;
            settings.PerplexityApiKey = null;
            settings.OpenRouterApiKey = null;
            settings.DeepSeekApiKey = null;
            settings.GeminiApiKey = null;
            settings.GroqApiKey = null;

            var validator = new BrainarrSettingsValidator();
            var result = validator.Validate(settings);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName.Contains(propertyName),
                $"Expected validation error for {propertyName}");
        }

        [Theory]
        [InlineData(AIProvider.OpenAI, nameof(BrainarrSettings.OpenAIApiKey))]
        [InlineData(AIProvider.Anthropic, nameof(BrainarrSettings.AnthropicApiKey))]
        [InlineData(AIProvider.Perplexity, nameof(BrainarrSettings.PerplexityApiKey))]
        [InlineData(AIProvider.OpenRouter, nameof(BrainarrSettings.OpenRouterApiKey))]
        [InlineData(AIProvider.DeepSeek, nameof(BrainarrSettings.DeepSeekApiKey))]
        [InlineData(AIProvider.Gemini, nameof(BrainarrSettings.GeminiApiKey))]
        [InlineData(AIProvider.Groq, nameof(BrainarrSettings.GroqApiKey))]
        public void Whitespace_Only_ApiKey_Is_Invalid(AIProvider provider, string propertyName)
        {
            var settings = new BrainarrSettings { Provider = provider };
            // Assign whitespace to the selected provider key
            switch (provider)
            {
                case AIProvider.OpenAI: settings.OpenAIApiKey = "   "; break;
                case AIProvider.Anthropic: settings.AnthropicApiKey = "\t"; break;
                case AIProvider.Perplexity: settings.PerplexityApiKey = "\n"; break;
                case AIProvider.OpenRouter: settings.OpenRouterApiKey = " \r "; break;
                case AIProvider.DeepSeek: settings.DeepSeekApiKey = " "; break;
                case AIProvider.Gemini: settings.GeminiApiKey = "\t\t"; break;
                case AIProvider.Groq: settings.GroqApiKey = "\n\r"; break;
            }

            var validator = new BrainarrSettingsValidator();
            var result = validator.Validate(settings);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName.Contains(propertyName));
        }

        [Fact]
        public void Invalid_Local_Url_Is_Rejected_For_Selected_Local_Provider_Only()
        {
            var validator = new BrainarrSettingsValidator();

            // Ollama selected: invalid URL should trigger error
            var s1 = new BrainarrSettings { Provider = AIProvider.Ollama, OllamaUrl = "javascript:alert(1)" };
            var r1 = validator.Validate(s1);
            r1.IsValid.Should().BeFalse();
            r1.Errors.Any(e => e.ErrorMessage.Contains("valid URL", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();

            // OpenAI selected: same invalid OllamaUrl should be ignored by validator,
            // provided required OpenAI settings are valid
            var s2 = new BrainarrSettings { Provider = AIProvider.OpenAI, OllamaUrl = "javascript:alert(1)", OpenAIApiKey = "dummy" };
            var r2 = validator.Validate(s2);
            r2.IsValid.Should().BeTrue(r2.ToString());

            // LM Studio selected: invalid LMStudioUrl should trigger error
            var s3 = new BrainarrSettings { Provider = AIProvider.LMStudio, LMStudioUrl = "file:///etc/passwd" };
            var r3 = validator.Validate(s3);
            r3.IsValid.Should().BeFalse();
        }

        [Fact]
        public void Local_Provider_Url_Scheme_Inference_And_IPv6_Accepted()
        {
            var validator = new BrainarrSettingsValidator();

            // Missing scheme should be inferred for local providers
            var s1 = new BrainarrSettings { Provider = AIProvider.Ollama, OllamaUrl = "localhost:11434" };
            validator.Validate(s1).IsValid.Should().BeTrue();

            // IPv6 with brackets should be accepted
            var s2 = new BrainarrSettings { Provider = AIProvider.Ollama, OllamaUrl = "http://[::1]:11434" };
            validator.Validate(s2).IsValid.Should().BeTrue();
        }

        [Fact]
        public void Local_Provider_Port_OutOfRange_Is_Rejected()
        {
            var validator = new BrainarrSettingsValidator();

            var s = new BrainarrSettings { Provider = AIProvider.LMStudio, LMStudioUrl = "http://localhost:70000" };
            var result = validator.Validate(s);
            result.IsValid.Should().BeFalse();
            result.Errors.Any(e => e.ErrorMessage.Contains("valid URL", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        }
        [Fact]
        public void SamplingShape_With_Percentages_Exceeding_Total_Is_Invalid()
        {
            var settings = new BrainarrSettings
            {
                SamplingShape = SamplingShape.Default with
                {
                    Artist = SamplingShape.Default.Artist with
                    {
                        Similar = new SamplingShape.ModeDistribution(TopPercent: 80, RecentPercent: 30)
                    }
                }
            };

            var result = settings.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("SamplingShape.Artist"));
        }

    }
}
