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
    /// Unit coverage for <see cref="BrainarrGroqProvider"/>.
    /// </summary>
    public class BrainarrGroqProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrGroqProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public async Task CompleteAsync_UsesBearerAuth_AgainstChatCompletionsEndpoint()
        {
            // Contract guard (MED-1, provider-matrix sweep): pin auth header + endpoint on CompleteAsync.
            var provider = new BrainarrGroqProvider(_http.Object, _logger, "gsk-xyz", "llama-3.3-70b-versatile");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            captured!.Headers.GetSingleValue("Authorization").Should().Be("Bearer gsk-xyz");
            captured.Url.ToString().Should().Be("https://api.groq.com/openai/v1/chat/completions");
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrGroqProvider(_http.Object, _logger, "gsk-test", "llama-3.3-70b-versatile");

            provider.ProviderId.Should().Be("groq");
            provider.DisplayName.Should().Be("Groq");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrGroqProvider(null!, _logger, "gsk-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => new BrainarrGroqProvider(_http.Object, null!, "gsk-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void Constructor_EmptyApiKey_Throws()
        {
            Action act = () => new BrainarrGroqProvider(_http.Object, _logger, "");
            act.Should().Throw<ArgumentException>().WithParameterName("apiKey");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContentAndUsage()
        {
            var provider = new BrainarrGroqProvider(_http.Object, _logger, "gsk-test", "llama-3.3-70b-versatile");
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
                usage = new { prompt_tokens = 9, completion_tokens = 3, total_tokens = 12 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("stop");
            response.Usage!.InputTokens.Should().Be(9);
            response.Usage!.OutputTokens.Should().Be(3);
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            var provider = new BrainarrGroqProvider(_http.Object, _logger, "gsk-test", "llama-3.3-70b-versatile");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("groq");
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            var provider = new BrainarrGroqProvider(_http.Object, _logger, "gsk-test", "llama-3.3-70b-versatile");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<RateLimitException>();
        }

        [Fact]
        public async Task CompleteAsync_404_ThrowsModelNotFound()
        {
            var provider = new BrainarrGroqProvider(_http.Object, _logger, "gsk-test", "wrong-model");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.NotFound));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ModelNotFound);
        }

        [Fact]
        public async Task CompleteAsync_503_ThrowsProviderOverloaded()
        {
            var provider = new BrainarrGroqProvider(_http.Object, _logger, "gsk-test", "llama-3.3-70b-versatile");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.ServiceUnavailable));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ProviderOverloaded);
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            var provider = new BrainarrGroqProvider(_http.Object, _logger, "gsk-test", "llama-3.3-70b-versatile");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("groq");
            health.AuthMethod.Should().Be("apiKey");
        }
    }
}
