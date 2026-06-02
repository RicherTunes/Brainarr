using System;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Unit")]
    public class ProviderRegistryTests
    {
        private static (ProviderRegistry reg, BrainarrSettings settings, Logger logger) Make()
            => (new ProviderRegistry(), new BrainarrSettings(), LogManager.GetCurrentClassLogger());

        // #70: LMStudioTemperature must actually reach the LM Studio request. ProviderRegistry passes
        // it to the adapter ctor; the adapter applies it to every LlmRequest.Temperature, which
        // BrainarrLmStudioProvider.BuildRequestBody serializes as `temperature`.
        [Fact]
        public void CreateProvider_LMStudio_WiresConfiguredTemperature()
        {
            var (reg, _, logger) = Make();
            var settings = new BrainarrSettings { Provider = AIProvider.LMStudio, LMStudioTemperature = 0.33 };
            var provider = reg.CreateProvider(AIProvider.LMStudio, settings, Mock.Of<IHttpClient>(), logger);
            ((LlmProviderAdapter)provider).Temperature.Should().BeApproximately(0.33f, 1e-6f,
                "LM Studio must run at the user-configured temperature, not the generic 0.8 default");
        }

        [Fact]
        public void CreateProvider_LMStudio_DefaultTemperature_IsPoint5()
        {
            var (reg, _, logger) = Make();
            var provider = reg.CreateProvider(AIProvider.LMStudio, new BrainarrSettings(), Mock.Of<IHttpClient>(), logger);
            ((LlmProviderAdapter)provider).Temperature.Should().BeApproximately(0.5f, 1e-6f,
                "the LMStudioTemperature default (0.5) is now the effective LM Studio temperature");
        }

        [Theory]
        [InlineData(5.0, 2.0)]    // above OpenAI-compatible max -> clamped to 2.0
        [InlineData(-1.0, 0.0)]   // below min -> clamped to 0.0
        [InlineData(1.5, 1.5)]    // in range -> unchanged
        public void CreateProvider_LMStudio_ClampsTemperatureToValidRange(double input, double expected)
        {
            var (reg, _, logger) = Make();
            var settings = new BrainarrSettings { Provider = AIProvider.LMStudio, LMStudioTemperature = input };
            var provider = reg.CreateProvider(AIProvider.LMStudio, settings, Mock.Of<IHttpClient>(), logger);
            ((LlmProviderAdapter)provider).Temperature.Should().BeApproximately((float)expected, 1e-6f,
                "an out-of-range temperature must be clamped to [0.0, 2.0] before reaching the LM Studio API");
        }

        [Fact]
        public void CreateProvider_CloudProvider_KeepsGenericDefaultTemperature()
        {
            var (reg, _, logger) = Make();
            var provider = reg.CreateProvider(AIProvider.OpenAI, new BrainarrSettings { OpenAIApiKey = "sk-test" }, Mock.Of<IHttpClient>(), logger);
            ((LlmProviderAdapter)provider).Temperature.Should().BeApproximately(0.8f, 1e-6f,
                "wiring LM Studio's temperature must not change other providers' generic default");
        }

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
