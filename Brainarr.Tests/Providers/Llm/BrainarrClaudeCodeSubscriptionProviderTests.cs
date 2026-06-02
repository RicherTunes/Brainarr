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
    /// Unit coverage for <see cref="BrainarrClaudeCodeSubscriptionProvider"/>.
    ///
    /// <para>
    /// These tests use a temporary credentials file rather than mocking
    /// <c>SubscriptionCredentialLoader</c> directly — the loader is intentionally a static
    /// service over <c>~/.claude/.credentials.json</c> (external CLI tool's state, not
    /// brainarr-owned), and the file path is the seam we're allowed to inject.
    /// </para>
    /// </summary>
    public class BrainarrClaudeCodeSubscriptionProviderTests : IDisposable
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;
        private readonly string _tempDir;
        private readonly string _tempCredentialsPath;

        public BrainarrClaudeCodeSubscriptionProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
            // SubscriptionCredentialLoader.IsPathSafe requires credential files to live UNDER the
            // user home directory (production reads ~/.claude/.credentials.json). Path.GetTempPath() is
            // OUTSIDE $HOME on Linux (/tmp vs /home/runner) — anchor the fixture under home for cross-platform parity.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _tempDir = Path.Combine(home, $".brainarr-cc-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _tempCredentialsPath = Path.Combine(_tempDir, ".credentials.json");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup; not failing tests for it.
            }
        }

        private void WriteValidCredentials(string token = "oauth-token-abc")
        {
            var futureExpiry = DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeMilliseconds();
            var json = JsonSerializer.Serialize(new
            {
                claudeAiOauth = new
                {
                    accessToken = token,
                    expiresAt = futureExpiry,
                    refreshToken = "refresh-xyz",
                    subscriptionType = "max",
                },
            });
            File.WriteAllText(_tempCredentialsPath, json);
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            WriteValidCredentials();
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "claude-sonnet-4-5-20250514");

            provider.ProviderId.Should().Be("claude-code-subscription");
            provider.DisplayName.Should().Be("Claude Code (Subscription)");
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            // Streaming intentionally NOT exposed — same gap as BrainarrAnthropicProvider.
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.Streaming).Should().BeFalse();
            provider.Capabilities.MaxContextTokens.Should().Be(200_000);
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrClaudeCodeSubscriptionProvider(null!, _logger, _tempCredentialsPath);
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_MissingCredentialsFile_ConstructsButFlagsError()
        {
            // Constructor must not throw — the user can run `claude login` and the next
            // request reloads the file. This matches the legacy behavior and keeps the
            // registry construction-path crash-free when no credentials exist yet.
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath);

            provider.ProviderId.Should().Be("claude-code-subscription");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContent()
        {
            WriteValidCredentials();
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "claude-sonnet-4-5-20250514");

            var apiObj = new
            {
                id = "msg_1",
                content = new[] { new { type = "text", text = "{\"recommendations\":[]}" } },
                stop_reason = "end_turn",
                usage = new { input_tokens = 5, output_tokens = 2 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("end_turn");
            response.Usage.Should().NotBeNull();
            response.Usage!.InputTokens.Should().Be(5);
            response.Usage!.OutputTokens.Should().Be(2);
        }

        [Fact]
        public async Task CompleteAsync_UsesBearerOAuth_NotXApiKey()
        {
            // Subscription credentials are OAuth access tokens (claudeAiOauth.accessToken). Anthropic
            // authenticates those via `Authorization: Bearer` + the `anthropic-beta` oauth flag — NOT
            // `x-api-key`, which is only for sk-ant- API keys. Sending the OAuth token as x-api-key
            // 401s every subscriber. The sibling BrainarrZaiCodingProvider (which emulates Claude Code)
            // uses the same Bearer scheme. Regression guard for the day-one auth bug.
            WriteValidCredentials("oauth-token-abc");
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "claude-sonnet-4-5-20250514");

            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"content\":[{\"type\":\"text\",\"text\":\"{}\"}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            captured!.Headers.GetSingleValue("Authorization").Should().Be("Bearer oauth-token-abc",
                "OAuth subscription tokens authenticate via Authorization: Bearer");
            captured.Headers.GetSingleValue("x-api-key").Should().BeNullOrEmpty(
                "the OAuth token must NOT be sent as x-api-key (that header is for sk-ant- API keys)");
            captured.Headers.GetSingleValue("anthropic-beta").Should().Be("oauth-2025-04-20",
                "the Anthropic OAuth path requires the oauth anthropic-beta flag");
        }

        [Fact]
        public async Task CompleteAsync_MissingCredentials_ThrowsAuthenticationException()
        {
            // No file written — loader will fail with a not-found message.
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath);

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("claude-code-subscription");
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.AuthenticationFailed);
            // The loader's hint should bubble up so the user sees actionable text.
            ex.Which.Message.Should().Contain("claude login");
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            WriteValidCredentials();
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "claude-sonnet-4-5-20250514");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("claude-code-subscription");
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            WriteValidCredentials();
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "claude-sonnet-4-5-20250514");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<RateLimitException>();
        }

        [Fact]
        public async Task CheckHealthAsync_NoCredentials_ReportsUnhealthy()
        {
            // No credentials — health check should fail without making an HTTP call.
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath);

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeFalse();
            health.ErrorCode.Should().Be("CredentialsMissing");
            _http.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Never,
                "no HTTP request should be issued when credentials are missing");
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            WriteValidCredentials();
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "claude-sonnet-4-5-20250514");
            var apiObj = new
            {
                content = new[] { new { type = "text", text = "OK" } },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("claude-code-subscription");
            health.AuthMethod.Should().Be("subscription");
        }

        [Fact]
        public void StreamAsync_ReturnsNull()
        {
            WriteValidCredentials();
            var provider = new BrainarrClaudeCodeSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "claude-sonnet-4-5-20250514");

            var stream = provider.StreamAsync(new LlmRequest { Prompt = "hello" });
            stream.Should().BeNull();
        }
    }
}
