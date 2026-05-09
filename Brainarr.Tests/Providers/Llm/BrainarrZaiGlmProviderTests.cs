using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Unit coverage for <see cref="BrainarrZaiGlmProvider"/>, the Z.AI (Zhipu) GLM
    /// provider. Z.AI's API speaks the OpenAI Chat Completions wire format at
    /// https://api.z.ai/api/paas/v4/chat/completions.
    /// </summary>
    public class BrainarrZaiGlmProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrZaiGlmProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrZaiGlmProvider(_http.Object, _logger, "zai-test-key", "GLM_4_5_Air");

            provider.ProviderId.Should().Be("zaiglm");
            provider.DisplayName.Should().Be("Z.AI GLM");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.Streaming).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.ToolCalling).Should().BeTrue();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrZaiGlmProvider(null!, _logger, "k", "GLM_4_5_Air");
            act.Should().Throw<ArgumentNullException>().WithMessage("*httpClient*");
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => new BrainarrZaiGlmProvider(_http.Object, null!, "k", "GLM_4_5_Air");
            act.Should().Throw<ArgumentNullException>().WithMessage("*logger*");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_EmptyApiKey_Throws(string? apiKey)
        {
            Action act = () => new BrainarrZaiGlmProvider(_http.Object, _logger, apiKey!, "GLM_4_5_Air");
            act.Should().Throw<ArgumentException>().WithMessage("*Z.AI API key*");
        }

        [Theory]
        [InlineData("GLM_5_1", "glm-5.1")]
        [InlineData("GLM_5", "glm-5")]
        [InlineData("GLM_5_Turbo", "glm-5-turbo")]
        [InlineData("GLM_4_7", "glm-4.7")]
        [InlineData("GLM_4_6", "glm-4.6")]
        [InlineData("GLM_4_5", "glm-4.5")]
        [InlineData("GLM_4_5_Air", "glm-4.5-air")]
        [InlineData("GLM_4_32B", "glm-4-32b-0414-128k")]
        public async Task CompleteAsync_MapsCanonicalEnumToRawApiId(string canonicalEnum, string expectedRawId)
        {
            var provider = new BrainarrZaiGlmProvider(_http.Object, _logger, "k", canonicalEnum);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hello" });

            captured.Should().NotBeNull();
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain($"\"model\":\"{expectedRawId}\"");
        }

        [Fact]
        public async Task CompleteAsync_HitsZaiInternationalEndpoint()
        {
            var provider = new BrainarrZaiGlmProvider(_http.Object, _logger, "k", "GLM_4_5_Air");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            captured!.Url.ToString().Should().Be("https://api.z.ai/api/paas/v4/chat/completions");
        }

        [Fact]
        public async Task CompleteAsync_SendsBearerAuthorizationHeader()
        {
            var provider = new BrainarrZaiGlmProvider(_http.Object, _logger, "secret-key-xyz", "GLM_4_5_Air");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            captured!.Headers["Authorization"].ToString().Should().Be("Bearer secret-key-xyz");
        }

        [Fact]
        public async Task CompleteAsync_JsonModeFlag_EmitsResponseFormat()
        {
            var provider = new BrainarrZaiGlmProvider(_http.Object, _logger, "k", "GLM_4_5_Air");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi", JsonMode = true });

            captured.Should().NotBeNull();
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("response_format");
            body.Should().Contain("json_object");
        }

        [Fact]
        public async Task CompleteAsync_WithSystemPrompt_EmitsBothMessages()
        {
            var provider = new BrainarrZaiGlmProvider(_http.Object, _logger, "k", "GLM_4_5_Air");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            await provider.CompleteAsync(new LlmRequest
            {
                Prompt = "user message",
                SystemPrompt = "you are a music recommender",
            });

            captured.Should().NotBeNull();
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"role\":\"system\"");
            body.Should().Contain("\"role\":\"user\"");
            body.Should().Contain("you are a music recommender");
            body.Should().Contain("user message");
        }

        [Fact]
        public async Task CompleteAsync_ParsesContentAndUsage()
        {
            var provider = new BrainarrZaiGlmProvider(_http.Object, _logger, "k", "GLM_4_5_Air");
            const string responseJson = """
            {
              "choices": [
                { "index": 0, "message": { "role": "assistant", "content": "answer" }, "finish_reason": "stop" }
              ],
              "usage": { "prompt_tokens": 11, "completion_tokens": 22, "total_tokens": 33 }
            }
            """;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(responseJson));

            var result = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            result.Content.Should().Be("answer");
            result.FinishReason.Should().Be("stop");
            result.Usage.Should().NotBeNull();
            result.Usage!.InputTokens.Should().Be(11);
            result.Usage.OutputTokens.Should().Be(22);
        }

        [Fact]
        public void UpdateModel_PropagatesIntoNextRequest()
        {
            var provider = new BrainarrZaiGlmProvider(_http.Object, _logger, "k", "GLM_4_5_Air");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            provider.UpdateModel("GLM_5_1");
            provider.CompleteAsync(new LlmRequest { Prompt = "hi" }).GetAwaiter().GetResult();

            captured.Should().NotBeNull();
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"model\":\"glm-5.1\"");
        }
    }
}
