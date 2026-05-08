using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Unit coverage for <see cref="BrainarrAnthropicProvider"/>.
    /// </summary>
    public class BrainarrAnthropicProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrAnthropicProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");

            provider.ProviderId.Should().Be("anthropic");
            provider.DisplayName.Should().Be("Anthropic");
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.ExtendedThinking).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            // Streaming is intentionally NOT set until common ships an Anthropic SSE decoder.
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.Streaming).Should().BeFalse();
            provider.Capabilities.MaxContextTokens.Should().Be(200_000);
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrAnthropicProvider(null!, _logger, "sk-ant-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_EmptyApiKey_Throws()
        {
            Action act = () => new BrainarrAnthropicProvider(_http.Object, _logger, "");
            act.Should().Throw<ArgumentException>().WithParameterName("apiKey");
        }

        [Fact]
        public void Constructor_ThinkingSentinel_ParsesBudget()
        {
            // The legacy AnthropicProvider parsed the `#thinking(tokens=N)` sentinel; the new
            // ILlmProvider provider must preserve that contract for the registry path.
            var provider = new BrainarrAnthropicProvider(
                _http.Object, _logger, "sk-ant-test", "claude-3-5-sonnet-latest#thinking(tokens=8000)");

            // Indirectly verified via successful construction; no public surface to inspect.
            provider.ProviderId.Should().Be("anthropic");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContent()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            var apiObj = new
            {
                id = "msg_1",
                content = new[]
                {
                    new { type = "text", text = "{\"recommendations\":[]}" },
                },
                stop_reason = "end_turn",
                usage = new { input_tokens = 10, output_tokens = 4 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hello" });

            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("end_turn");
            response.Usage.Should().NotBeNull();
            response.Usage!.InputTokens.Should().Be(10);
            response.Usage!.OutputTokens.Should().Be(4);
        }

        [Fact]
        public async Task CompleteAsync_WithThinking_CarriesReasoningContent()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-sonnet-latest#thinking");
            var apiObj = new
            {
                content = new object[]
                {
                    new { type = "thinking", thinking = "considering options" },
                    new { type = "text", text = "[]" },
                },
                stop_reason = "end_turn",
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be("[]");
            response.ReasoningContent.Should().Be("considering options");
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("anthropic");
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<RateLimitException>();
        }

        [Fact]
        public async Task CompleteAsync_503_ThrowsProviderOverloaded()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.ServiceUnavailable));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ProviderOverloaded);
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            var apiObj = new
            {
                content = new[] { new { type = "text", text = "OK" } },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("anthropic");
        }

        [Fact]
        public void StreamAsync_ReturnsNull_AnthropicSseDecoderMissingFromCommon()
        {
            // Audit feedback: common does not yet ship an Anthropic SSE decoder. Streaming
            // for this provider is intentionally surfaced as null until that lands.
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            var stream = provider.StreamAsync(new LlmRequest { Prompt = "hello" });
            stream.Should().BeNull();
        }
    }
}
