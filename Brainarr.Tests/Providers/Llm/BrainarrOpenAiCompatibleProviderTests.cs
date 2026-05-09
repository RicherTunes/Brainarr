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
    /// Unit coverage for <see cref="BrainarrOpenAiCompatibleProvider"/>, the catch-all
    /// generic OpenAI-compatible provider for self-hosted/proxy backends.
    /// </summary>
    public class BrainarrOpenAiCompatibleProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrOpenAiCompatibleProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Theory]
        [InlineData("good-api-key-123", "good-api-key-123")]
        [InlineData("with\rcr", "withcr")]
        [InlineData("with\nlf", "withlf")]
        [InlineData("with\r\ncrlf", "withcrlf")]
        [InlineData("with\0nul", "withnul")]
        [InlineData("multi\r\n\0junk", "multijunk")]
        public async Task ApiKey_HeaderInjectionAttempt_IsStrippedBeforeHeaderEmission(string rawKey, string expectedHeaderTail)
        {
            // Wave 38 regression: API keys containing CR/LF/NUL must be sanitized
            // before being concatenated into the Authorization header. The pre-fix
            // code passed the user-supplied key straight through, allowing a key
            // like "valid\r\nX-Injected: ..." to split the request into two headers
            // and smuggle arbitrary header lines.
            var provider = new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://my-host:8080", "my-model", rawKey);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"data\":[]}"));

            await provider.CheckHealthAsync(CancellationToken.None);

            captured.Should().NotBeNull();
            captured!.Headers.Should().ContainKey("Authorization");
            var auth = captured.Headers["Authorization"]?.ToString() ?? string.Empty;
            auth.Should().Be($"Bearer {expectedHeaderTail}");
            auth.Should().NotContain("\r");
            auth.Should().NotContain("\n");
            auth.Should().NotContain("\0");
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags_JsonModeOptIn()
        {
            // Catch-all stays conservative: JsonMode is intentionally NOT set on the
            // capability flags (opt-in via ProviderOptions["json_mode"] = true).
            var provider = new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://my-host:8080", "my-model");

            provider.ProviderId.Should().Be("openai-compatible");
            provider.DisplayName.Should().Be("OpenAI-Compatible");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.Streaming).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeFalse();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrOpenAiCompatibleProvider(null!, _logger, "https://x", "m");
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_EmptyBaseUrl_Throws()
        {
            Action act = () => new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "", "m");
            act.Should().Throw<ArgumentException>().WithParameterName("baseUrl");
        }

        [Fact]
        public void Constructor_EmptyModel_Throws()
        {
            Action act = () => new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://x", "");
            act.Should().Throw<ArgumentException>().WithParameterName("model");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContentAndUsage()
        {
            var provider = new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://my-host:8080", "my-model");
            var apiObj = new
            {
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = "result" },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 7, completion_tokens = 2, total_tokens = 9 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" }, CancellationToken.None);

            response.Content.Should().Be("result");
            response.Usage!.InputTokens.Should().Be(7);
            response.Usage!.OutputTokens.Should().Be(2);
        }

        [Fact]
        public async Task CompleteAsync_JsonModeOptInViaProviderOptions_EmitsResponseFormat()
        {
            // Phase 4c legacy path: opt-in via ProviderOptions["json_mode"] = true must add
            // response_format to the body. Kept for back-compat with brainarr-side callers
            // that don't yet route through the unified LlmRequest.JsonMode flag.
            var provider = new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://my-host:8080", "my-model");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"{}\"},\"finish_reason\":\"stop\"}]}"));

            var request = new LlmRequest
            {
                Prompt = "hi",
                ProviderOptions = new Dictionary<string, object> { ["json_mode"] = true },
            };

            await provider.CompleteAsync(request, CancellationToken.None);

            captured.Should().NotBeNull();
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("response_format");
            body.Should().Contain("json_object");
        }

        [Fact]
        public async Task CompleteAsync_JsonModeOptInViaJsonModeFlag_EmitsResponseFormat()
        {
            // Phase 5b: the unified LlmRequest.JsonMode flag is now the canonical surface.
            // Setting JsonMode=true must add response_format to the body even without the
            // legacy ProviderOptions entry.
            var provider = new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://my-host:8080", "my-model");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"{}\"},\"finish_reason\":\"stop\"}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi", JsonMode = true }, CancellationToken.None);

            captured.Should().NotBeNull();
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("response_format");
            body.Should().Contain("json_object");
        }

        [Fact]
        public async Task CompleteAsync_JsonModeOff_OmitsResponseFormat()
        {
            // Default (no opt-in) must NOT include response_format — many self-hosted
            // backends 400 on unknown fields. This is the whole point of opt-in.
            var provider = new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://my-host:8080", "my-model");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"x\"},\"finish_reason\":\"stop\"}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" }, CancellationToken.None);

            captured.Should().NotBeNull();
            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().NotContain("response_format");
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthyWithExpectedAuth()
        {
            var provider = new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://my-host:8080", "my-model", apiKey: "secret");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"data\":[]}"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("openai-compatible");
            // Bearer token configured → auth method is apiKey.
            health.AuthMethod.Should().Be("apiKey");
        }

        [Fact]
        public async Task CheckHealthAsync_ConnectionRefused_ReportsDegraded()
        {
            // Phase 5b: no vendor-specific guidance for this catch-all, but the same
            // degradation pattern applies: don't cascade transport failures, and report
            // Degraded rather than Unhealthy. Degraded keeps IsHealthy=true so transient
            // failover doesn't blacklist the provider, and the [Degraded] StatusMessage
            // prefix + ConnectionFailed errorCode surface to the UI.
            var provider = new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://unreachable.example", "m");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("Connection refused"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue(); // Degraded => still healthy enough to attempt
            health.ErrorCode.Should().Be("ConnectionFailed");
            health.StatusMessage.Should().StartWith("[Degraded]");
            health.StatusMessage.Should().Contain("OpenAI-compatible endpoint not running");
        }

        [Fact]
        public void StreamAsync_ReturnsNull_StreamingNotYetWired()
        {
            var provider = new BrainarrOpenAiCompatibleProvider(_http.Object, _logger, "https://my-host:8080", "my-model");
            var stream = provider.StreamAsync(new LlmRequest { Prompt = "hi" });
            stream.Should().BeNull();
        }
    }
}
