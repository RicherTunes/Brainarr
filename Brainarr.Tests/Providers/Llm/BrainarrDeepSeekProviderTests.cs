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
    /// Unit coverage for <see cref="BrainarrDeepSeekProvider"/>.
    /// </summary>
    public class BrainarrDeepSeekProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrDeepSeekProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public async Task CompleteAsync_UsesBearerAuth_AgainstChatCompletionsEndpoint()
        {
            // Contract guard (MED-1, provider-matrix sweep): pin auth header + endpoint on CompleteAsync.
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-deepseek-xyz", "deepseek-chat");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            captured!.Headers.GetSingleValue("Authorization").Should().Be("Bearer sk-deepseek-xyz");
            captured.Url.ToString().Should().Be("https://api.deepseek.com/chat/completions");
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-deepseek-test", "deepseek-chat");

            provider.ProviderId.Should().Be("deepseek");
            provider.DisplayName.Should().Be("DeepSeek");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.ExtendedThinking).Should().BeTrue();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrDeepSeekProvider(null!, _logger, "sk-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => new BrainarrDeepSeekProvider(_http.Object, null!, "sk-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void Constructor_EmptyApiKey_Throws()
        {
            Action act = () => new BrainarrDeepSeekProvider(_http.Object, _logger, "");
            act.Should().Throw<ArgumentException>().WithParameterName("apiKey");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContentAndUsage()
        {
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-test", "deepseek-chat");
            var apiObj = new
            {
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = "{\"recommendations\":[]}" },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 11, completion_tokens = 4, total_tokens = 15 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hello" });

            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("stop");
            response.Usage!.InputTokens.Should().Be(11);
            response.Usage!.OutputTokens.Should().Be(4);
        }

        [Fact]
        public async Task CompleteAsync_DeepSeekReasoner_PopulatesReasoningContent()
        {
            // deepseek-reasoner emits a separate `reasoning_content` on the message object.
            // Provider must surface it via LlmResponse.ReasoningContent.
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-test", "deepseek-reasoner");
            var apiObj = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "[]",
                            reasoning_content = "Considered candidates A, B, C; preferred A.",
                        },
                        finish_reason = "stop",
                    },
                },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "x" });

            response.Content.Should().Be("[]");
            response.ReasoningContent.Should().Be("Considered candidates A, B, C; preferred A.");
        }

        [Fact]
        public async Task CompleteAsync_NonReasonerModel_LeavesReasoningContentNull()
        {
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-test", "deepseek-chat");
            var apiObj = new
            {
                choices = new[] { new { message = new { content = "[]" }, finish_reason = "stop" } },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "x" });

            response.Content.Should().Be("[]");
            response.ReasoningContent.Should().BeNull();
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-test", "deepseek-chat");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("deepseek");
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-test", "deepseek-chat");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<RateLimitException>();
        }

        [Fact]
        public async Task CompleteAsync_500_ThrowsProviderUnavailable()
        {
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-test", "deepseek-chat");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.InternalServerError));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ProviderUnavailable);
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-test", "deepseek-chat");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("deepseek");
            health.AuthMethod.Should().Be("apiKey");
        }
    }
}
