using System;
using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using Xunit;

namespace Brainarr.Tests.Services.Registry
{
    public class ProviderSlugsTests
    {
        [Fact]
        [Trait("Area", "Registry")]
        public void ToRegistrySlug_MapsAllProviders()
        {
            var expectations = new Dictionary<AIProvider, string?>
            {
                [AIProvider.Ollama] = "ollama",
                [AIProvider.LMStudio] = "lmstudio",
                [AIProvider.Perplexity] = "perplexity",
                [AIProvider.OpenAI] = "openai",
                [AIProvider.Anthropic] = "anthropic",
                [AIProvider.OpenRouter] = "openrouter",
                [AIProvider.DeepSeek] = "deepseek",
                [AIProvider.Gemini] = "gemini",
                [AIProvider.Groq] = "groq",
                [AIProvider.ClaudeCodeSubscription] = "claude-code",
                [AIProvider.OpenAICodexSubscription] = "openai-codex",
                [AIProvider.ZaiGlm] = "zai-glm"
            };

            foreach (var provider in Enum.GetValues<AIProvider>())
            {
                Assert.True(expectations.ContainsKey(provider), $"Missing expected mapping for provider {provider}");
                var expected = expectations[provider];
                var actual = ProviderSlugs.ToRegistrySlug(provider);
                Assert.Equal(expected, actual);
            }
        }
    }
}
