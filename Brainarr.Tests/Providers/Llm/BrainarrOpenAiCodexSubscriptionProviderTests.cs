using System;
using System.IO;
using System.Net;
using System.Text.Json;
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
    /// Unit coverage for <see cref="BrainarrOpenAiCodexSubscriptionProvider"/>.
    /// File-based credential injection mirrors the Claude Code subscription tests.
    /// </summary>
    public class BrainarrOpenAiCodexSubscriptionProviderTests : IDisposable
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;
        private readonly string _tempCredentialsPath;

        public BrainarrOpenAiCodexSubscriptionProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
            _tempCredentialsPath = Path.Combine(
                Path.GetTempPath(),
                $"brainarr-codex-creds-{Guid.NewGuid():N}.json");
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_tempCredentialsPath))
                {
                    File.Delete(_tempCredentialsPath);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }

        private void WriteValidTokens(string token = "sess-codex-token")
        {
            // Format: { "tokens": { "access_token": "...", "expires_at": <unix-seconds>, ... } }
            var futureExpiry = DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeSeconds();
            var json = JsonSerializer.Serialize(new
            {
                tokens = new
                {
                    access_token = token,
                    expires_at = futureExpiry,
                    refresh_token = "refresh-xyz",
                },
            });
            File.WriteAllText(_tempCredentialsPath, json);
        }

        private void WriteFallbackApiKey(string key = "sk-codex-fallback")
        {
            // Loader also accepts a top-level OPENAI_API_KEY field as a legacy fallback.
            var json = JsonSerializer.Serialize(new { OPENAI_API_KEY = key });
            File.WriteAllText(_tempCredentialsPath, json);
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            WriteValidTokens();
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "gpt-4o");

            provider.ProviderId.Should().Be("openai-codex-subscription");
            provider.DisplayName.Should().Be("OpenAI Codex (Subscription)");
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrOpenAiCodexSubscriptionProvider(null!, _logger, _tempCredentialsPath);
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContent()
        {
            WriteValidTokens();
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "gpt-4o");

            var apiObj = new
            {
                choices = new object[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = "[]" },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 7, completion_tokens = 3, total_tokens = 10 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi", SystemPrompt = "you are a bot" });

            response.Content.Should().Be("[]");
            response.FinishReason.Should().Be("stop");
            response.Usage.Should().NotBeNull();
            response.Usage!.InputTokens.Should().Be(7);
            response.Usage!.OutputTokens.Should().Be(3);
        }

        [Fact]
        public async Task CompleteAsync_ApiKeyFallback_Works()
        {
            // Legacy `OPENAI_API_KEY` field in auth.json should be honored.
            WriteFallbackApiKey();
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "gpt-4o");

            var apiObj = new
            {
                choices = new object[]
                {
                    new { index = 0, message = new { role = "assistant", content = "ok" }, finish_reason = "stop" },
                },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            response.Content.Should().Be("ok");
        }

        [Fact]
        public async Task CompleteAsync_MissingCredentials_ThrowsAuthenticationException()
        {
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath);

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("openai-codex-subscription");
            ex.Which.Message.Should().Contain("codex auth login");
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            WriteValidTokens();
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "gpt-4o");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<AuthenticationException>();
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            WriteValidTokens();
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "gpt-4o");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<RateLimitException>();
        }

        [Fact]
        public async Task CheckHealthAsync_NoCredentials_ReportsUnhealthy()
        {
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath);

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeFalse();
            health.ErrorCode.Should().Be("CredentialsMissing");
            _http.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            WriteValidTokens();
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "gpt-4o");
            var apiObj = new
            {
                choices = new object[]
                {
                    new { index = 0, message = new { role = "assistant", content = "OK" }, finish_reason = "stop" },
                },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("openai-codex-subscription");
            health.AuthMethod.Should().Be("subscription");
        }

        [Fact]
        public void StreamAsync_ReturnsNull()
        {
            WriteValidTokens();
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "gpt-4o");

            var stream = provider.StreamAsync(new LlmRequest { Prompt = "hello" });
            stream.Should().BeNull();
        }
    }
}
