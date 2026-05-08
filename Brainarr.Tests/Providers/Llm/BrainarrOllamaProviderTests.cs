using System;
using System.Collections.Generic;
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
    /// Unit coverage for <see cref="BrainarrOllamaProvider"/>.
    /// </summary>
    public class BrainarrOllamaProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrOllamaProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrOllamaProvider(_http.Object, _logger);

            provider.ProviderId.Should().Be("ollama");
            provider.DisplayName.Should().Be("Ollama");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeTrue();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrOllamaProvider(null!, _logger);
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => new BrainarrOllamaProvider(_http.Object, null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContentAndUsage()
        {
            var provider = new BrainarrOllamaProvider(_http.Object, _logger, model: "qwen2.5:latest");
            var apiObj = new
            {
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = "{\"items\":[]}" },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 10, completion_tokens = 6, total_tokens = 16 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hello" }, CancellationToken.None);

            response.Content.Should().Be("{\"items\":[]}");
            response.FinishReason.Should().Be("stop");
            response.Usage!.InputTokens.Should().Be(10);
            response.Usage!.OutputTokens.Should().Be(6);
        }

        [Fact]
        public async Task CompleteAsync_KeepAliveOption_PassesThroughToBody()
        {
            // Ollama-specific quirk: keep_alive controls model unloading. The provider must
            // surface ProviderOptions["keep_alive"] into the request body. We don't intercept
            // the serialized body here (would couple us to JSON shape) — we only assert the
            // call happened, and that no exception was raised when the option is present.
            var provider = new BrainarrOllamaProvider(_http.Object, _logger, model: "qwen2.5:latest");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"OK\"},\"finish_reason\":\"stop\"}]}"));

            var request = new LlmRequest
            {
                Prompt = "hello",
                ProviderOptions = new Dictionary<string, object> { ["keep_alive"] = "5m" },
            };

            var response = await provider.CompleteAsync(request, CancellationToken.None);

            response.Content.Should().Be("OK");
            captured.Should().NotBeNull();
            // keep_alive value should appear in the JSON-serialized body.
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("keep_alive");
            body.Should().Contain("5m");
        }

        [Fact]
        public async Task CompleteAsync_404_ThrowsModelNotFound()
        {
            var provider = new BrainarrOllamaProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.NotFound));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ModelNotFound);
        }

        [Fact]
        public async Task CheckHealthAsync_V1ModelsOk_IsHealthy()
        {
            var provider = new BrainarrOllamaProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"data\":[{\"id\":\"qwen2.5:latest\"}]}"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("ollama");
            health.AuthMethod.Should().Be("none");
        }

        [Fact]
        public async Task CheckHealthAsync_V1Models404_FallsBackToApiTags()
        {
            // Older Ollama builds don't implement /v1/models. The provider must fall back to
            // /api/tags to confirm liveness. We sequence the mock: first call → 404, second
            // call → 200.
            var provider = new BrainarrOllamaProvider(_http.Object, _logger);
            var calls = 0;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(() =>
                {
                    calls++;
                    return calls == 1
                        ? Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.NotFound)
                        : Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"models\":[{\"name\":\"qwen2.5:latest\"}]}");
                });

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            calls.Should().Be(2);
        }

        [Fact]
        public async Task CheckHealthAsync_ConnectionRefused_DegradesGracefully()
        {
            // Local-specific quirk: when Ollama isn't running, transport fails. The provider
            // must NOT cascade — it returns Unhealthy with a ConnectionFailed code so the UI
            // can surface an "Ollama not running" hint.
            var provider = new BrainarrOllamaProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("Connection refused"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeFalse();
            health.ErrorCode.Should().Be("ConnectionFailed");
            health.StatusMessage.Should().Contain("Cannot reach Ollama");
        }

        [Fact]
        public void StreamAsync_ReturnsNull_StreamingNotYetWired()
        {
            var provider = new BrainarrOllamaProvider(_http.Object, _logger);
            var stream = provider.StreamAsync(new LlmRequest { Prompt = "hi" });
            stream.Should().BeNull();
        }
    }
}
