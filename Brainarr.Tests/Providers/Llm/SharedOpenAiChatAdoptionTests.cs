using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
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

        [Fact]
        public async Task FactoryProviderShortRequestTimeoutEndsBlockedStreamAsRecoverableTimeout()
        {
            var streamingExecutor = new StreamingHttpExecutor(new BlockingStreamHandler());
            var registry = new ProviderRegistry();
            registry.Register(AIProvider.OpenAI, (settings, http, logger) =>
                new LlmProviderAdapter(
                    new BrainarrOpenAiProvider(
                        http,
                        logger,
                        settings.OpenAIApiKey,
                        settings.OpenAIModel,
                        streamingExecutor),
                    logger));
            var adapter = registry.CreateProvider(
                    AIProvider.OpenAI,
                    SettingsFor(AIProvider.OpenAI),
                    _http.Object,
                    _logger)
                .Should().BeOfType<LlmProviderAdapter>().Subject;

            using var cleanupCancellation = new CancellationTokenSource();
            Func<Task> enumerate = async () =>
            {
                await foreach (var _ in adapter.Inner.StreamAsync(new LlmRequest
                {
                    Prompt = "recommend one album",
                    Timeout = TimeSpan.FromMilliseconds(50),
                }, cleanupCancellation.Token)!)
                {
                }
            };

            var enumeration = enumerate();
            var finished = await Task.WhenAny(enumeration, Task.Delay(TimeSpan.FromSeconds(2)));
            if (finished == enumeration)
            {
                Func<Task> completedEnumeration = () => enumeration;
                var failure = await completedEnumeration.Should().ThrowAsync<NetworkException>();
                failure.Which.ErrorCode.Should().Be(LlmErrorCode.Timeout);
                return;
            }

            cleanupCancellation.Cancel();
            try
            {
                await enumeration;
            }
            catch (OperationCanceledException)
            {
                // Test cleanup only. The assertion below records the missing request deadline.
            }

            finished.Should().BeSameAs(enumeration,
                "the per-request timeout must cover the complete streaming enumeration");
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

        private sealed class BlockingStreamHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new CancellationBlockingStream()),
                });
            }
        }

        private sealed class CancellationBlockingStream : Stream
        {
            private int _seeded;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Interlocked.Exchange(ref _seeded, 1) == 0)
                {
                    var prefix = new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a', (byte)':', (byte)' ' };
                    Array.Copy(prefix, 0, buffer, offset, prefix.Length);
                    return prefix.Length;
                }

                throw new NotSupportedException();
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }

            public override async Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }
        }
    }
}
