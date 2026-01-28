using System;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class ProviderRegistryTests
    {
        private static (ProviderRegistry reg, BrainarrSettings settings, Logger logger) Make()
            => (new ProviderRegistry(), new BrainarrSettings(), LogManager.GetCurrentClassLogger());

        [Fact]
        public void IsRegistered_Known_Providers_Return_True()
        {
            var (reg, _, _) = Make();
            reg.IsRegistered(AIProvider.Ollama).Should().BeTrue();
            reg.IsRegistered(AIProvider.LMStudio).Should().BeTrue();
            reg.IsRegistered(AIProvider.OpenAI).Should().BeTrue();
            reg.IsRegistered(AIProvider.Anthropic).Should().BeTrue();
            reg.IsRegistered(AIProvider.OpenRouter).Should().BeTrue();
            reg.IsRegistered(AIProvider.Groq).Should().BeTrue();
            reg.IsRegistered(AIProvider.Gemini).Should().BeTrue();
            reg.IsRegistered(AIProvider.Perplexity).Should().BeTrue();
            reg.IsRegistered(AIProvider.DeepSeek).Should().BeTrue();
        }

    }
}
