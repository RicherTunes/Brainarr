using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Moq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Unit coverage for <see cref="BrainarrOpenAiProvider"/>, the new
    /// <c>ILlmProvider</c>-shaped foundation introduced in Phase 4 wave 4a.
    /// </summary>
    public class BrainarrOpenAiProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrOpenAiProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public async Task CompleteAsync_UsesBearerAuth_AgainstChatCompletionsEndpoint()
        {
            // Contract guard (MED-1 from the provider-matrix sweep): pin the auth header + endpoint on
            // the CompleteAsync path the pipeline actually uses. Previously only StreamAsync was pinned
            // — the same coverage gap that let the sibling subscription provider's x-api-key bug ship.
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test-xyz", "gpt-4o-mini");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            captured!.Headers.GetSingleValue("Authorization").Should().Be("Bearer sk-test-xyz");
            captured.Url.ToString().Should().Be("https://api.openai.com/v1/chat/completions");
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");

            provider.ProviderId.Should().Be("openai");
            provider.DisplayName.Should().Be("OpenAI");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrOpenAiProvider(null!, _logger, "sk-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => new BrainarrOpenAiProvider(_http.Object, null!, "sk-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void Constructor_EmptyApiKey_Throws()
        {
            Action act = () => new BrainarrOpenAiProvider(_http.Object, _logger, "");
            act.Should().Throw<ArgumentException>().WithParameterName("apiKey");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContentAndUsage()
        {
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");
            var apiObj = new
            {
                id = "1",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = "{\"recommendations\":[]}" },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 12, completion_tokens = 7, total_tokens = 19 },
            };
            var responseBody = Newtonsoft.Json.JsonConvert.SerializeObject(apiObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(responseBody));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hello" }, CancellationToken.None);

            response.Should().NotBeNull();
            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("stop");
            response.Usage.Should().NotBeNull();
            response.Usage!.InputTokens.Should().Be(12);
            response.Usage!.OutputTokens.Should().Be(7);
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_api_key\"}"));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" }, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("openai");
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.AuthenticationFailed);
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429, "{\"error\":\"rate_limit\",\"retry_after\":5}"));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" }, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<RateLimitException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.RateLimited);
        }

        [Fact]
        public async Task CompleteAsync_5xx_ThrowsProviderException()
        {
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.ServiceUnavailable, "{\"error\":\"overloaded\"}"));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" }, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ProviderOverloaded);
        }

        [Fact]
        public async Task CheckHealthAsync_OkResponse_IsHealthy()
        {
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");
            var apiObj = new
            {
                id = "1",
                choices = new[]
                {
                    new { message = new { content = "OK" }, finish_reason = "stop" },
                },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("openai");
            health.AuthMethod.Should().Be("apiKey");
        }

        [Fact]
        public async Task CheckHealthAsync_401_IsUnhealthyWithErrorCode()
        {
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeFalse();
            health.ErrorCode.Should().Be("401");
        }

        [Fact]
        public void StreamAsync_NowReturnsNonNullEnumerable()
        {
            // Tech debt wave 2: StreamingHttpExecutor bridges IHttpClient → System.IO.Stream,
            // so StreamAsync now returns a non-null IAsyncEnumerable. Streaming behavior is
            // covered by BrainarrStreamingTests (uses fake HttpMessageHandler).
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");
            var stream = provider.StreamAsync(new LlmRequest { Prompt = "hello" });
            stream.Should().NotBeNull();
        }

        [Fact]
        public async Task CompleteAsync_JsonModeOn_EmitsResponseFormatJsonObject()
        {
            // Phase 5b: when LlmRequest.JsonMode=true, the provider must emit OpenAI's
            // response_format={"type":"json_object"} parameter.
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"{}\"},\"finish_reason\":\"stop\"}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi", JsonMode = true });

            captured.Should().NotBeNull();
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("response_format");
            body.Should().Contain("json_object");
        }

        [Fact]
        public async Task CompleteAsync_JsonModeOff_OmitsResponseFormat()
        {
            // Default (JsonMode=false) must NOT emit response_format — preserves the
            // pre-Phase-5b shape for callers that explicitly opt out.
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"x\"},\"finish_reason\":\"stop\"}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().NotContain("response_format");
        }
    }
}
