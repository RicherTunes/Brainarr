using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Brainarr.TestKit.Providers.Fakes;
using Brainarr.TestKit.Providers.Http;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.TestKit.Testing;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Llm
{
    /// <summary>
    /// Wave-7C characterization tests: verifies <see cref="BrainarrOpenAiCodexSubscriptionProvider"/>
    /// integrates correctly with <see cref="LlmAuthCircuit"/> (4-pact).
    ///
    /// <para>
    /// The credentials-path is used as the circuit key (not the bearer token, which is loaded
    /// per-call from disk). Each test uses a temp file containing a valid mock access_token so
    /// the credential loader considers the file valid and the provider reaches the HTTP call.
    /// </para>
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class BrainarrOpenAiCodexSubscriptionAuthCircuitAdoptionTests : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly string _tempDir;
        private readonly string _credPath;

        private const string FakeToken = "fake-codex-access-token-abc123";

        // OpenAI Chat Completions shape (same wire format as BrainarrOpenAiProvider).
        private const string OkJson = @"{
            ""choices"": [{""message"": {""content"": ""hello"", ""role"": ""assistant""}, ""finish_reason"": ""stop""}],
            ""usage"": {""prompt_tokens"": 5, ""completion_tokens"": 3, ""total_tokens"": 8}
        }";

        public BrainarrOpenAiCodexSubscriptionAuthCircuitAdoptionTests()
        {
            // SubscriptionCredentialLoader.IsPathSafe requires credential files to live UNDER the
            // user home directory. Path.GetTempPath() is under home on Windows but /tmp on Linux
            // (outside /home/runner) — anchor the fixture under home for cross-platform parity.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _tempDir = Path.Combine(home, $".brainarr_codex_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _credPath = Path.Combine(_tempDir, "auth.json");
            WriteValidCredentials();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private void WriteValidCredentials()
        {
            // SubscriptionCredentialLoader.LoadCodexCredentials reads either
            // { "OPENAI_API_KEY": "..." } or { "tokens": { "access_token": "..." } }.
            // The OPENAI_API_KEY form is the simpler fallback path.
            var json = $@"{{
                ""OPENAI_API_KEY"": ""{FakeToken}""
            }}";
            File.WriteAllText(_credPath, json);
        }

        private static LlmRequest MakeRequest() =>
            new LlmRequest { Prompt = "test", MaxTokens = 10 };

        private BrainarrOpenAiCodexSubscriptionProvider MakeProvider(
            FakeHttpClient http,
            LlmAuthCircuit circuit)
            => new BrainarrOpenAiCodexSubscriptionProvider(
                http, Logger, _credPath, model: null, authCircuit: circuit);

        // ── Test 1: open circuit throws immediately (no HTTP call) ─────────────
        [Fact]
        public async Task CompleteAsync_OpenCircuit_ThrowsImmediately()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("openai-codex-subscription", _credPath);

            var callCount = 0;
            var http = new FakeHttpClient(req =>
            {
                callCount++;
                return HttpResponseFactory.Ok(req, OkJson);
            });
            var provider = MakeProvider(http, circuit);

            var act = async () => await provider.CompleteAsync(MakeRequest());
            await act.Should().ThrowAsync<AuthenticationException>()
                .WithMessage("*Auth circuit open*");

            callCount.Should().Be(0, "no HTTP call should be made when circuit is open");
        }

        // ── Test 2: 401 calls RecordAuthFailure ───────────────────────────────
        [Fact]
        public async Task CompleteAsync_OnHttp401_CallsRecordAuthFailure()
        {
            var clock = new FakeTimeProvider();
            // threshold=1 so a single 401 latches the circuit — observable side-effect
            var circuit = new LlmAuthCircuit(null, 1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized, @"{""error"": ""invalid_api_key""}"));
            var provider = MakeProvider(http, circuit);

            await Assert.ThrowsAsync<AuthenticationException>(() => provider.CompleteAsync(MakeRequest()));

            circuit.IsOpen("openai-codex-subscription", _credPath, out _)
                .Should().BeTrue("RecordAuthFailure must have been called with the provider id + credentials path");
        }

        // ── Test 3: 200 calls RecordSuccess ───────────────────────────────────
        [Fact]
        public async Task CompleteAsync_OnSuccess_CallsRecordSuccess()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            // Pre-open circuit then reset via RecordSuccess to confirm the provider calls it.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("openai-codex-subscription", _credPath);
            circuit.RecordSuccess("openai-codex-subscription", _credPath); // allow one probe through

            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, OkJson));
            var provider = MakeProvider(http, circuit);

            var response = await provider.CompleteAsync(MakeRequest());
            response.Content.Should().Be("hello");
            circuit.IsOpen("openai-codex-subscription", _credPath, out _)
                .Should().BeFalse("RecordSuccess must have been called, keeping the circuit closed");
        }

        // ── Test 4: 500 does NOT call RecordAuthFailure ───────────────────────
        [Fact]
        public async Task CompleteAsync_OnHttp500_DoesNotPoisonCircuit()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.InternalServerError, @"{""error"": ""server error""}"));
            var provider = MakeProvider(http, circuit);

            // A server error should throw but must NOT latch the auth circuit.
            // Wave-25 fix: contract is gate-state, not exception type.
            await Assert.ThrowsAnyAsync<Exception>(() => provider.CompleteAsync(MakeRequest()));

            circuit.IsOpen("openai-codex-subscription", _credPath, out _)
                .Should().BeFalse("non-auth HTTP errors must not trigger RecordAuthFailure");
        }
    }
}
