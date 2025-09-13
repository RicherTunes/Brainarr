using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class TokenBudgetServiceTests
    {
        private static BrainarrSettings Make(AIProvider provider, SamplingStrategy strategy, string model)
        {
            return new BrainarrSettings
            {
                Provider = provider,
                SamplingStrategy = strategy,
                // Assigning ModelSelection through per-provider properties as tests often expect
                OllamaModel = model,
                LMStudioModel = model,
                OpenAIModelId = model,
                AnthropicModelId = model,
                OpenRouterModelId = model,
                DeepSeekModelId = model,
                GeminiModelId = model,
                GroqModelId = model
            };
        }

        [Fact]
        public void Comprehensive_OpenAI_4oMini_Should_Be_64k()
        {
            var svc = new TokenBudgetService(LogManager.GetCurrentClassLogger());
            var s = Make(AIProvider.OpenAI, SamplingStrategy.Comprehensive, "gpt-4o-mini");
            svc.GetLimit(s).Should().Be(64000);
        }

        [Fact]
        public void Comprehensive_Anthropic_Claude37_Should_Be_120k()
        {
            var svc = new TokenBudgetService(LogManager.GetCurrentClassLogger());
            var s = Make(AIProvider.Anthropic, SamplingStrategy.Comprehensive, "claude-3.7-sonnet");
            svc.GetLimit(s).Should().Be(120000);
        }

        [Fact]
        public void Comprehensive_Llama_70B_Should_Be_32k()
        {
            var svc = new TokenBudgetService(LogManager.GetCurrentClassLogger());
            var s = Make(AIProvider.OpenRouter, SamplingStrategy.Comprehensive, "llama-3.1-70b");
            svc.GetLimit(s).Should().Be(32000);
        }

        [Fact]
        public void Local_Providers_Should_Apply_Multipliers()
        {
            var svc = new TokenBudgetService(LogManager.GetCurrentClassLogger());
            var balancedLocal = Make(AIProvider.Ollama, SamplingStrategy.Balanced, "qwen2.5:latest");
            // Base balanced = 6000, Ollama multiplier 1.6
            svc.GetLimit(balancedLocal).Should().Be(9600);
        }

        [Fact]
        public void Override_Should_Win()
        {
            var svc = new TokenBudgetService(LogManager.GetCurrentClassLogger());
            var s = Make(AIProvider.OpenAI, SamplingStrategy.Comprehensive, "gpt-4o-mini");
            s.ComprehensiveTokenBudgetOverride = 77777;
            svc.GetLimit(s).Should().Be(77777);
        }
    }
}

