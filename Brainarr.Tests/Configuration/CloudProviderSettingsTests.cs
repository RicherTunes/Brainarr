using FluentAssertions;
using FluentValidation.Results;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class CloudProviderSettingsTests
    {
        [Fact]
        public void OpenAIProviderSettings_behaves_as_expected()
        {
            var s = new OpenAIProviderSettings
            {
                ApiKey = "key-123",
                ModelName = "gpt-4o-mini"
            };

            s.ProviderType.Should().Be(AIProvider.OpenAI);
            s.GetApiKey().Should().Be("key-123");
            s.GetModel().Should().Be("gpt-4o-mini");
            s.GetBaseUrl().Should().BeNull();

            var validator = new OpenAIProviderSettingsValidator();
            validator.Validate(s).IsValid.Should().BeTrue();

            // Invalid when API key missing
            var bad = new OpenAIProviderSettings { ApiKey = "", ModelName = "gpt-4o-mini" };
            validator.Validate(bad).IsValid.Should().BeFalse();
        }

        [Fact]
        public void AnthropicProviderSettings_behaves_as_expected()
        {
            var s = new AnthropicProviderSettings { ApiKey = "a", ModelName = "claude" };
            s.ProviderType.Should().Be(AIProvider.Anthropic);
            s.GetApiKey().Should().Be("a");
            s.GetModel().Should().Be("claude");
            s.GetBaseUrl().Should().BeNull();

            var vr = new AnthropicProviderSettingsValidator().Validate(s);
            vr.IsValid.Should().BeTrue();
        }

        [Fact]
        public void OpenRouterProviderSettings_behaves_as_expected()
        {
            var s = new OpenRouterProviderSettings { ApiKey = "r", ModelName = "gpt-4o-mini" };
            s.ProviderType.Should().Be(AIProvider.OpenRouter);
            s.GetApiKey().Should().Be("r");
            s.GetModel().Should().Be("gpt-4o-mini");
            s.GetBaseUrl().Should().BeNull();
            new OpenRouterProviderSettingsValidator().Validate(s).IsValid.Should().BeTrue();
        }

        [Fact]
        public void PerplexityProviderSettings_behaves_as_expected()
        {
            var s = new PerplexityProviderSettings { ApiKey = "p", ModelName = "sonar-large" };
            s.ProviderType.Should().Be(AIProvider.Perplexity);
            s.GetApiKey().Should().Be("p");
            s.GetModel().Should().Be("sonar-large");
            s.GetBaseUrl().Should().BeNull();
            new PerplexityProviderSettingsValidator().Validate(s).IsValid.Should().BeTrue();
        }

        [Fact]
        public void DeepSeekProviderSettings_behaves_as_expected()
        {
            var s = new DeepSeekProviderSettings { ApiKey = "d", ModelName = "deepseek-chat" };
            s.ProviderType.Should().Be(AIProvider.DeepSeek);
            s.GetApiKey().Should().Be("d");
            s.GetModel().Should().Be("deepseek-chat");
            s.GetBaseUrl().Should().BeNull();
            new DeepSeekProviderSettingsValidator().Validate(s).IsValid.Should().BeTrue();
        }

        [Fact]
        public void GeminiProviderSettings_behaves_as_expected()
        {
            var s = new GeminiProviderSettings { ApiKey = "g", ModelName = "gemini-1.5-flash" };
            s.ProviderType.Should().Be(AIProvider.Gemini);
            s.GetApiKey().Should().Be("g");
            s.GetModel().Should().Be("gemini-1.5-flash");
            s.GetBaseUrl().Should().BeNull();
            new GeminiProviderSettingsValidator().Validate(s).IsValid.Should().BeTrue();
        }

        [Fact]
        public void GroqProviderSettings_behaves_as_expected()
        {
            var s = new GroqProviderSettings { ApiKey = "q", ModelName = "llama-3.3-70b" };
            s.ProviderType.Should().Be(AIProvider.Groq);
            s.GetApiKey().Should().Be("q");
            s.GetModel().Should().Be("llama-3.3-70b");
            s.GetBaseUrl().Should().BeNull();
            new GroqProviderSettingsValidator().Validate(s).IsValid.Should().BeTrue();
        }
    }
}
