using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Unit coverage for <see cref="BrainarrOpenAiCodexSubscriptionProvider"/> after the
    /// ChatGPT-subscription rework.
    ///
    /// <para>
    /// A pure ChatGPT-subscription login (auth_mode=chatgpt, no OPENAI_API_KEY) is dispatched to the
    /// ChatGPT backend Responses API via a RAW HttpClient (custom originator/User-Agent the host's
    /// IHttpClient forbids) — these tests inject a fake <see cref="HttpMessageHandler"/> through the
    /// internal test-seam ctor to verify the headers/body/SSE parsing that reach the socket, plus the
    /// 401 → token-refresh → retry path. The legacy OPENAI_API_KEY fallback still routes through the
    /// Platform chat/completions endpoint over Lidarr's IHttpClient (mocked).
    /// </para>
    /// </summary>
    public class BrainarrOpenAiCodexSubscriptionProviderTests : IDisposable
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;
        private readonly string _tempDir;
        private readonly string _tempCredentialsPath;

        public BrainarrOpenAiCodexSubscriptionProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
            // IsPathSafe requires credential files UNDER the user home dir (prod reads ~/.codex/auth.json);
            // Path.GetTempPath() is outside $HOME on Linux, so anchor the fixture under home.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _tempDir = Path.Combine(home, $".brainarr-codex-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _tempCredentialsPath = Path.Combine(_tempDir, "auth.json");
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
            catch { /* best-effort */ }
        }

        // ChatGPT-subscription auth.json: no OPENAI_API_KEY, tokens.* with an account_id.
        private void WriteChatGptTokens(string token = "sess-codex-token", string account = "acct-123")
        {
            var json = JsonSerializer.Serialize(new
            {
                auth_mode = "chatgpt",
                tokens = new
                {
                    access_token = token,
                    refresh_token = "refresh-xyz",
                    account_id = account,
                },
                last_refresh = "2026-08-24T00:00:00Z",
            });
            File.WriteAllText(_tempCredentialsPath, json);
        }

        private void WriteFallbackApiKey(string key = "sk-codex-fallback")
        {
            var json = JsonSerializer.Serialize(new { OPENAI_API_KEY = key });
            File.WriteAllText(_tempCredentialsPath, json);
        }

        private static string SampleSse(string text = "[]") =>
            string.Join("\n", new[]
            {
                "event: response.created",
                "data: {\"type\":\"response.created\"}",
                "",
                "event: response.output_text.delta",
                "data: {\"type\":\"response.output_text.delta\",\"delta\":" + JsonSerializer.Serialize(text) + "}",
                "",
                "event: response.output_text.done",
                "data: {\"type\":\"response.output_text.done\",\"text\":" + JsonSerializer.Serialize(text) + "}",
                "",
                "event: response.completed",
                "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"usage\":{\"input_tokens\":7,\"output_tokens\":3}}}",
                "",
            });

        /// <summary>Fake handler: captures the outbound request and replays a queued sequence of responses.</summary>
        private sealed class SequenceHandler : HttpMessageHandler
        {
            private readonly Queue<(HttpStatusCode Status, string Body)> _responses;
            public Uri? LastUri { get; private set; }
            public string LastBody { get; private set; } = string.Empty;
            public Dictionary<string, string> LastHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int Calls { get; private set; }

            public SequenceHandler(params (HttpStatusCode, string)[] responses)
                => _responses = new Queue<(HttpStatusCode, string)>(responses);

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                LastUri = request.RequestUri;
                LastHeaders.Clear();
                foreach (var h in request.Headers)
                {
                    LastHeaders[h.Key] = string.Join(",", h.Value);
                }
                if (request.Content != null)
                {
                    LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                var (status, body) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
                return new HttpResponseMessage(status) { Content = new StringContent(body) };
            }
        }

        private BrainarrOpenAiCodexSubscriptionProvider CreateChatGptProvider(
            HttpMessageHandler handler,
            Func<CancellationToken, Task<CodexRefreshResult>>? refreshOverride = null,
            string? model = "gpt-5.6-terra")
            => new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, model, authCircuit: null,
                rawHandler: handler, refreshOverride: refreshOverride);

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            WriteChatGptTokens();
            var provider = CreateChatGptProvider(new SequenceHandler((HttpStatusCode.OK, SampleSse())));

            provider.ProviderId.Should().Be("openai-codex-subscription");
            provider.DisplayName.Should().Be("OpenAI Codex (Subscription)");
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            // Subscription path speaks the ChatGPT Responses API, not OpenAI Chat Completions.
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeFalse();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrOpenAiCodexSubscriptionProvider(null!, _logger, _tempCredentialsPath);
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public async Task CompleteAsync_ChatGptMode_SendsCodexHeadersToResponsesEndpoint()
        {
            WriteChatGptTokens("tok-abc", "acct-999");
            var handler = new SequenceHandler((HttpStatusCode.OK, SampleSse("[]")));
            var provider = CreateChatGptProvider(handler);

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi", SystemPrompt = "sys" });

            response.Content.Should().Be("[]");
            response.Usage.Should().NotBeNull();
            response.Usage!.InputTokens.Should().Be(7);
            response.Usage!.OutputTokens.Should().Be(3);

            handler.LastUri!.ToString().Should().Contain("backend-api/codex/responses");
            handler.LastHeaders["Authorization"].Should().Be("Bearer tok-abc");
            handler.LastHeaders["chatgpt-account-id"].Should().Be("acct-999");
            handler.LastHeaders["originator"].Should().Be("codex_cli_rs");
            handler.LastHeaders.Should().ContainKey("OpenAI-Beta");
            handler.LastBody.Should().Contain("\"instructions\":\"sys\"");
            handler.LastBody.Should().Contain("\"stream\":true");
            handler.LastBody.Should().Contain("input_text");
        }

        [Fact]
        public async Task CompleteAsync_401ThenRefresh_RetriesAndSucceeds()
        {
            WriteChatGptTokens();
            var handler = new SequenceHandler(
                (HttpStatusCode.Unauthorized, "{\"detail\":\"expired\"}"),
                (HttpStatusCode.OK, SampleSse("ok")));
            var refreshed = false;
            var provider = CreateChatGptProvider(handler, refreshOverride: _ =>
            {
                refreshed = true;
                return Task.FromResult(CodexRefreshResult.Success("tok-new", DateTimeOffset.UtcNow.AddHours(1)));
            });

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            refreshed.Should().BeTrue();
            handler.Calls.Should().Be(2);
            response.Content.Should().Be("ok");
        }

        [Fact]
        public async Task CompleteAsync_401AndRefreshFails_ThrowsAuthenticationException()
        {
            WriteChatGptTokens();
            var handler = new SequenceHandler((HttpStatusCode.Unauthorized, "{\"detail\":\"expired\"}"));
            var provider = CreateChatGptProvider(handler, refreshOverride: _ =>
                Task.FromResult(CodexRefreshResult.Failure("no refresh token")));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<AuthenticationException>();
        }

        [Fact]
        public async Task CompleteAsync_401AndRefreshPersistsFails_StillCompletesOnFreshToken()
        {
            // Rotation succeeded server-side but the write-back failed: the refresh token on disk
            // is dead, yet the fresh access token in hand is valid for days. The run must COMPLETE
            // on that token (retry succeeds) instead of failing a request we hold credentials for
            // — the plain-Failure contrast is the test above this one.
            WriteChatGptTokens();
            var handler = new SequenceHandler(
                (HttpStatusCode.Unauthorized, "{\"detail\":\"expired\"}"),
                (HttpStatusCode.OK, SampleSse("ok")));
            var provider = CreateChatGptProvider(handler, refreshOverride: _ =>
                Task.FromResult(CodexRefreshResult.PersistenceFailure(
                    "tok-fresh", DateTimeOffset.UtcNow.AddHours(1),
                    "Refreshed token could not be written to disk; re-login required.")));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.Calls.Should().Be(2, "the fresh access token must be used for the retry");
            handler.LastHeaders["Authorization"].Should().Be("Bearer tok-fresh");
            response.Content.Should().Be("ok");
        }

        [Fact]
        public void ProviderRegistry_WithUnsetModel_ApiKeyModeGetsPlatformDefault_NotCodexSlug()
        {
            // THE REGISTRY SEAM (adversarial review round 2): the factory used to substitute
            // DefaultOpenAICodexModel ("gpt-5.6-terra") before construction, which made the
            // provider's mode-aware default unreachable — API-key users who never chose a model
            // got a ChatGPT-backend slug sent to the Platform API and a dead provider. The
            // registry must pass the unset model through and let the provider resolve per mode.
            WriteFallbackApiKey("sk-xyz");
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAICodexSubscription,
                OpenAICodexCredentialsPath = _tempCredentialsPath,
                ManualModelId = null,
                OpenAICodexModelId = null,
            };

            var registry = new ProviderRegistry();
            var adapter = (LlmProviderAdapter)registry.CreateProvider(AIProvider.OpenAICodexSubscription, settings, _http.Object, _logger);

            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            adapter.Inner.CompleteAsync(new LlmRequest { Prompt = "hi" }).GetAwaiter().GetResult();

            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"model\":\"gpt-4o\"",
                "an API-key user with no stored model must get the Platform default, never a codex slug");
        }

        [Fact]
        public async Task CompleteAsync_ModelNotSupported_400_Throws()
        {
            WriteChatGptTokens();
            var handler = new SequenceHandler(
                (HttpStatusCode.BadRequest, "{\"detail\":\"The 'gpt-5' model is not supported when using Codex with a ChatGPT account.\"}"));
            var provider = CreateChatGptProvider(handler, model: "gpt-5");

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<LlmProviderException>();
        }

        [Fact]
        public async Task CompleteAsync_ApiKeyFallback_UsesChatCompletions()
        {
            WriteFallbackApiKey("sk-xyz");
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "gpt-4o");

            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be("ok");
            captured.Should().NotBeNull();
            captured!.Headers.GetSingleValue("Authorization").Should().Be("Bearer sk-xyz");
            captured.Url.ToString().Should().Contain("chat/completions");

            // API-key mode targets the Platform API: the stored Platform model must reach the
            // wire VERBATIM — never a ChatGPT-backend slug coerced into it.
            var body = System.Text.Encoding.UTF8.GetString(captured.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"model\":\"gpt-4o\"");
        }

        [Fact]
        public async Task CompleteAsync_ApiKeyFallback_ExplicitCodexSlug_IsNotCoerced()
        {
            // API-key mode preserves the pre-port contract: whatever model the user stored is sent
            // as-is. The codex-slug coercion exists to escape the settings-UI deadlock on the
            // ChatGPT backend, where those slugs are the only accepted ids; applying it here would
            // silently migrate Platform users onto ids their API rejects.
            WriteFallbackApiKey("sk-xyz");
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath, "gpt-5.6-terra");

            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"model\":\"gpt-5.6-terra\"");
        }

        [Fact]
        public async Task CompleteAsync_ApiKeyFallback_NoModel_DefaultsToPlatformModel()
        {
            WriteFallbackApiKey("sk-xyz");
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(
                _http.Object, _logger, _tempCredentialsPath);

            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain($"\"model\":\"{BrainarrConstants.DefaultOpenAICodexApiModel}\"");
        }

        [Fact]
        public async Task CompleteAsync_MissingCredentials_ThrowsAuthenticationException()
        {
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(_http.Object, _logger, _tempCredentialsPath);

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("openai-codex-subscription");
            ex.Which.Message.Should().Contain("codex auth login");
        }

        [Fact]
        public async Task CheckHealthAsync_NoCredentials_ReportsUnhealthy()
        {
            var provider = new BrainarrOpenAiCodexSubscriptionProvider(_http.Object, _logger, _tempCredentialsPath);

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeFalse();
            health.ErrorCode.Should().Be("CredentialsMissing");
        }

        [Fact]
        public async Task CompleteAsync_StaleNonCodexModel_IsCoercedToDefault()
        {
            // The Lidarr UI can hold the previously-selected provider's model when switching to Codex
            // (it doesn't refetch the schema on a provider-dropdown change). Sending it would 400 and
            // fail the Test, which blocks Save entirely — so it must be coerced.
            WriteChatGptTokens();
            var handler = new SequenceHandler((HttpStatusCode.OK, SampleSse("[]")));
            var provider = CreateChatGptProvider(handler, model: "GPT41_Mini");

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.LastBody.Should().Contain("\"model\":\"gpt-5.6-terra\"");
        }

        [Fact]
        public async Task CompleteAsync_RetiredGpt54Slug_IsCoercedToDefault()
        {
            // gpt-5.4 answers 200 today but leaves Codex on 2026-08-31. A "gpt-5" prefix test would
            // admit it and start 400-ing after that date, so coercion matches the known-good set.
            WriteChatGptTokens();
            var handler = new SequenceHandler((HttpStatusCode.OK, SampleSse("[]")));
            var provider = CreateChatGptProvider(handler, model: "gpt-5.4");

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.LastBody.Should().Contain("\"model\":\"gpt-5.6-terra\"");
        }

        [Fact]
        public async Task CompleteAsync_KnownCodexModel_IsSentUnchanged()
        {
            WriteChatGptTokens();
            var handler = new SequenceHandler((HttpStatusCode.OK, SampleSse("[]")));
            var provider = CreateChatGptProvider(handler, model: "gpt-5.6-luna");

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.LastBody.Should().Contain("\"model\":\"gpt-5.6-luna\"");
        }

        [Fact]
        public async Task CompleteAsync_StreamReportsFailure_ThrowsInsteadOfEmptySuccess()
        {
            // HTTP 200 but the stream itself failed. Returning an empty success here would log
            // "0 recommendations" with no cause AND record success on the auth circuit.
            WriteChatGptTokens();
            var errorSse = string.Join("\n", new[]
            {
                "event: response.created",
                "data: {\"type\":\"response.created\"}",
                "",
                "event: response.failed",
                "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"message\":\"server had an error\"}}}",
                "",
            });
            var provider = CreateChatGptProvider(new SequenceHandler((HttpStatusCode.OK, errorSse)));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            (await act.Should().ThrowAsync<LlmProviderException>())
                .Which.Message.Should().Contain("server had an error");
        }

        [Fact]
        public async Task CheckHealthAsync_401ThenRefresh_RetriesAndReportsHealthy()
        {
            // The access token is a ~10-day JWT. Without a refresh on the health path, a Test taken
            // after expiry goes red while a sync succeeds — and Lidarr won't save a list whose Test
            // failed, stranding the user.
            WriteChatGptTokens();
            var handler = new SequenceHandler(
                (HttpStatusCode.Unauthorized, "{\"detail\":\"expired\"}"),
                (HttpStatusCode.OK, SampleSse("OK")));
            var refreshed = false;
            var provider = CreateChatGptProvider(handler, refreshOverride: _ =>
            {
                refreshed = true;
                return Task.FromResult(CodexRefreshResult.Success("tok-new", DateTimeOffset.UtcNow.AddHours(1)));
            });

            var health = await provider.CheckHealthAsync();

            refreshed.Should().BeTrue();
            handler.Calls.Should().Be(2);
            health.IsHealthy.Should().BeTrue();
        }

        [Fact]
        public void Capabilities_DoesNotAdvertiseJsonMode()
        {
            // The backend rejects text.format=json_object unless the input message contains the word
            // "json", so we can't guarantee it. Advertising the flag would make the pipeline believe
            // strict JSON is enforced when nothing enforces it.
            WriteChatGptTokens();
            var provider = CreateChatGptProvider(new SequenceHandler((HttpStatusCode.OK, SampleSse())));

            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeFalse();
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            WriteChatGptTokens();
            var handler = new SequenceHandler((HttpStatusCode.OK, SampleSse("OK")));
            var provider = CreateChatGptProvider(handler);

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("openai-codex-subscription");
            health.AuthMethod.Should().Be("subscription");
        }

        [Fact]
        public void StreamAsync_ReturnsNull()
        {
            WriteChatGptTokens();
            var provider = CreateChatGptProvider(new SequenceHandler((HttpStatusCode.OK, SampleSse())));

            provider.StreamAsync(new LlmRequest { Prompt = "hello" }).Should().BeNull();
        }
    }
}
