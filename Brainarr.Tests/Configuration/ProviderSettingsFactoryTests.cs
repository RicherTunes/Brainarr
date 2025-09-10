using System;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class ProviderSettingsFactoryTests
    {
        [Fact]
        public void CreateSettings_returns_correct_types_for_each_provider()
        {
            var factory = new ProviderSettingsFactory();

            factory.CreateSettings(AIProvider.Ollama).Should().BeOfType<OllamaProviderSettings>();
            factory.CreateSettings(AIProvider.LMStudio).Should().BeOfType<LMStudioProviderSettings>();
            factory.CreateSettings(AIProvider.Perplexity).Should().BeOfType<PerplexityProviderSettings>();
            factory.CreateSettings(AIProvider.OpenAI).Should().BeOfType<OpenAIProviderSettings>();
            factory.CreateSettings(AIProvider.Anthropic).Should().BeOfType<AnthropicProviderSettings>();
            factory.CreateSettings(AIProvider.OpenRouter).Should().BeOfType<OpenRouterProviderSettings>();
            factory.CreateSettings(AIProvider.DeepSeek).Should().BeOfType<DeepSeekProviderSettings>();
            factory.CreateSettings(AIProvider.Gemini).Should().BeOfType<GeminiProviderSettings>();
            factory.CreateSettings(AIProvider.Groq).Should().BeOfType<GroqProviderSettings>();
        }

        [Fact]
        public void CreateSettings_throws_for_unsupported_provider()
        {
            var factory = new ProviderSettingsFactory();
            var unsupported = (AIProvider)999;
            Action act = () => factory.CreateSettings(unsupported);
            act.Should().Throw<ArgumentOutOfRangeException>()
                .WithMessage("*Unsupported provider*");
        }

        [Fact]
        public void GetSupportedProviders_contains_all_known_providers()
        {
            var factory = new ProviderSettingsFactory();
            var supported = factory.GetSupportedProviders().ToHashSet();

            supported.Should().Contain(new[]
            {
                AIProvider.Ollama,
                AIProvider.LMStudio,
                AIProvider.Perplexity,
                AIProvider.OpenAI,
                AIProvider.Anthropic,
                AIProvider.OpenRouter,
                AIProvider.DeepSeek,
                AIProvider.Gemini,
                AIProvider.Groq
            });
        }
    }
}
