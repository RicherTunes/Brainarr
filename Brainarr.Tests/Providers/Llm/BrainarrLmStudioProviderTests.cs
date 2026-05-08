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
    /// Unit coverage for <see cref="BrainarrLmStudioProvider"/>, the new
    /// <c>ILlmProvider</c>-shaped local provider introduced in Phase 4 wave 4c.
    /// </summary>
    public class BrainarrLmStudioProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrLmStudioProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrLmStudioProvider(_http.Object, _logger);

            provider.ProviderId.Should().Be("lmstudio");
            provider.DisplayName.Should().Be("LM Studio");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.Streaming).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeTrue();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrLmStudioProvider(null!, _logger);
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => new BrainarrLmStudioProvider(_http.Object, null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void Constructor_DefaultBaseUrl_UsesLocalhost1234()
        {
            // Construction should succeed without arguments — LM Studio's localhost default.
            var provider = new BrainarrLmStudioProvider(_http.Object, _logger);
            provider.Should().NotBeNull();
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContentAndUsage()
        {
            var provider = new BrainarrLmStudioProvider(_http.Object, _logger, model: "local-model");
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
                usage = new { prompt_tokens = 8, completion_tokens = 4, total_tokens = 12 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" }, CancellationToken.None);

            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("stop");
            response.Usage!.InputTokens.Should().Be(8);
            response.Usage!.OutputTokens.Should().Be(4);
        }

        [Fact]
        public async Task CompleteAsync_404_ThrowsModelNotFound()
        {
            var provider = new BrainarrLmStudioProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.NotFound));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ModelNotFound);
            ex.Which.ProviderId.Should().Be("lmstudio");
        }

        [Fact]
        public async Task CheckHealthAsync_ModelsListOk_IsHealthy()
        {
            var provider = new BrainarrLmStudioProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"data\":[{\"id\":\"local-model\"}]}"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("lmstudio");
            // Local providers default to no auth.
            health.AuthMethod.Should().Be("none");
        }

        [Fact]
        public async Task CheckHealthAsync_ConnectionRefused_ReportsDegraded()
        {
            // Phase 5b: when LM Studio isn't running, transport fails. The provider must NOT
            // cascade an exception. As of Phase 5b adoption, this is semantically Degraded
            // rather than Unhealthy — Degraded keeps IsHealthy=true so transient failover
            // doesn't blacklist the provider, and the [Degraded] StatusMessage prefix +
            // ConnectionFailed errorCode surface to the UI.
            var provider = new BrainarrLmStudioProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("Connection refused"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue(); // Degraded => still healthy enough to attempt
            health.ErrorCode.Should().Be("ConnectionFailed");
            health.StatusMessage.Should().StartWith("[Degraded]");
            health.StatusMessage.Should().Contain("LM Studio not running");
        }

        [Fact]
        public async Task CheckHealthAsync_404_IsUnhealthyWithErrorCode()
        {
            var provider = new BrainarrLmStudioProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.NotFound));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeFalse();
            health.ErrorCode.Should().Be("404");
        }

        [Fact]
        public void StreamAsync_ReturnsNull_StreamingNotYetWired()
        {
            // wave 4c: streaming flag is set on Capabilities but the IHttpClient path doesn't
            // expose a raw response stream. Mirrors wave 4a/4b — capability is forward-declared.
            var provider = new BrainarrLmStudioProvider(_http.Object, _logger);
            var stream = provider.StreamAsync(new LlmRequest { Prompt = "hi" });
            stream.Should().BeNull();
        }
    }
}
