using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Integration and hygiene contract for the Common OpenAI-chat transport adoption.
    /// These tests intentionally enter through the production registry so a provider that
    /// bypasses the registered factory cannot satisfy the contract.
    /// </summary>
    public sealed class SharedOpenAiChatAdoptionTests
    {
        private const string CommonBaseName =
            "Lidarr.Plugin.Common.Providers.OpenAi.OpenAiChatProviderBase";

        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();

        public static IEnumerable<object[]> OpenAiChatProviders()
        {
            yield return new object[] { AIProvider.OpenAI, typeof(BrainarrOpenAiProvider) };
            yield return new object[] { AIProvider.DeepSeek, typeof(BrainarrDeepSeekProvider) };
            yield return new object[] { AIProvider.Groq, typeof(BrainarrGroqProvider) };
            yield return new object[] { AIProvider.OpenRouter, typeof(BrainarrOpenRouterProvider) };
            yield return new object[] { AIProvider.Perplexity, typeof(BrainarrPerplexityProvider) };
            yield return new object[] { AIProvider.ZaiGlm, typeof(BrainarrZaiGlmProvider) };
        }

        [Theory]
        [MemberData(nameof(OpenAiChatProviders))]
        public void RegistryCreatesThinProviderShellOnCommonOpenAiChatBase(
            AIProvider providerType,
            Type expectedShellType)
        {
            var registry = new ProviderRegistry();
            var settings = SettingsFor(providerType);

            var adapter = registry.CreateProvider(providerType, settings, _http.Object, _logger)
                .Should().BeOfType<LlmProviderAdapter>().Subject;

            adapter.Inner.Should().BeOfType(expectedShellType);
            adapter.Inner.GetType().BaseType?.FullName.Should().Be(CommonBaseName,
                "all OpenAI-chat-format orchestration must come from Common");
        }

        [Fact]
        public void CompiledPluginDoesNotContainRetiredLocalOpenAiChatBase()
        {
            typeof(BrainarrOpenAiProvider).Assembly.GetType(
                    "NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.BrainarrOpenAiChatProviderBase")
                .Should().BeNull("the local transport base must not regrow after Common adoption");
        }

        [Fact]
        public void DistinctWireContractsDoNotInheritOpenAiChatBase()
        {
            var distinctContracts = new[]
            {
                typeof(BrainarrOpenAiCodexSubscriptionProvider),
                typeof(BrainarrAnthropicProvider),
                typeof(BrainarrGeminiProvider),
                typeof(BrainarrZaiCodingProvider),
            };

            distinctContracts.Should().OnlyContain(
                type => type.BaseType == null || type.BaseType.FullName != CommonBaseName,
                "Codex Responses, Anthropic Messages, Gemini, and Z.AI Coding have distinct wire contracts");
        }

        [Theory]
        [InlineData("{\"choices\":[]}")]
        [InlineData("{\"choices\":[{\"message\":{\"content\":\"\"},\"finish_reason\":\"stop\"}]}")]
        [InlineData("{\"choices\":[{\"message\":{},\"finish_reason\":\"stop\"}]}")]
        public async Task FactoryProviderRejectsSuccessfulResponseWithoutUsableContent(string responseBody)
        {
            _http.Setup(client => client.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(responseBody));
            var registry = new ProviderRegistry();
            var adapter = registry.CreateProvider(
                    AIProvider.OpenAI,
                    SettingsFor(AIProvider.OpenAI),
                    _http.Object,
                    _logger)
                .Should().BeOfType<LlmProviderAdapter>().Subject;

            Func<Task> act = () => adapter.Inner.CompleteAsync(
                new LlmRequest { Prompt = "recommend one album" },
                CancellationToken.None);

            var failure = await act.Should().ThrowAsync<LlmProviderException>();
            failure.Which.Message.Should().ContainEquivalentOf("content");
        }

        private static BrainarrSettings SettingsFor(AIProvider providerType)
        {
            return new BrainarrSettings
            {
                Provider = providerType,
                OpenAIApiKey = "openai-test-key",
                DeepSeekApiKey = "deepseek-test-key",
                GroqApiKey = "groq-test-key",
                OpenRouterApiKey = "openrouter-test-key",
                PerplexityApiKey = "perplexity-test-key",
                ZaiGlmApiKey = "zai-test-key",
                AnthropicApiKey = "anthropic-test-key",
                GeminiApiKey = "gemini-test-key",
            };
        }
    }
}
