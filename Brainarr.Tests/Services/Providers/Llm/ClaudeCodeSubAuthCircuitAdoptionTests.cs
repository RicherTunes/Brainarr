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
    /// Wave-7C adoption tests: verifies <see cref="BrainarrClaudeCodeSubscriptionProvider"/>
    /// integrates correctly with <see cref="LlmAuthCircuit"/>.
    ///
    /// <para>
    /// The credentials-path is used as the key material (not the token itself, which rotates).
    /// Each test uses a temp file containing a valid mock token with a far-future expiry.
    /// </para>
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class ClaudeCodeSubAuthCircuitAdoptionTests : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly string _tempDir;
        private readonly string _credPath;

        // Far-future expiry (Unix ms) so the loader considers the token valid.
        private const long FarFutureMs = 9_999_999_999_999L;
        private const string FakeToken = "fake-oauth-token-abc123";

        private const string OkJson = @"{
            ""id"": ""msg_test"",
            ""content"": [{""type"": ""text"", ""text"": ""hello""}],
            ""stop_reason"": ""end_turn"",
            ""usage"": {""input_tokens"": 5, ""output_tokens"": 3}
        }";

        public ClaudeCodeSubAuthCircuitAdoptionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"brainarr_sub_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _credPath = Path.Combine(_tempDir, ".credentials.json");
            WriteValidCredentials();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private void WriteValidCredentials()
        {
            var json = $@"{{
                ""claudeAiOauth"": {{
                    ""accessToken"": ""{FakeToken}"",
                    ""expiresAt"": {FarFutureMs}
                }}
            }}";
            File.WriteAllText(_credPath, json);
        }

        private static LlmRequest MakeRequest() =>
            new LlmRequest { Prompt = "test", MaxTokens = 10 };

        private BrainarrClaudeCodeSubscriptionProvider MakeProvider(
            FakeHttpClient http,
            LlmAuthCircuit circuit)
            => new BrainarrClaudeCodeSubscriptionProvider(
                http, Logger, _credPath, model: null, authCircuit: circuit);

        // ── Test 1: open circuit throws immediately (no HTTP call) ─────────────
        [Fact]
        public async Task CompleteAsync_OpenCircuit_ThrowsImmediately()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("claude-code-subscription", _credPath);

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

            callCount.Should().Be(0, "no HTTP call when circuit is open");
        }

        // ── Test 2: 3 consecutive 401s open the circuit ───────────────────────
        [Fact]
        public async Task CompleteAsync_3xConsecutive401_OpensCircuit()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized, @"{""error"": ""invalid token""}"));
            var provider = MakeProvider(http, circuit);

            for (int i = 0; i < 3; i++)
            {
                await Assert.ThrowsAsync<AuthenticationException>(() => provider.CompleteAsync(MakeRequest()));
            }

            circuit.IsOpen("claude-code-subscription", _credPath, out _).Should()
                .BeTrue("circuit opens after 3 consecutive 401s");
        }

        // ── Test 3: success resets circuit ────────────────────────────────────
        [Fact]
        public async Task CompleteAsync_Success_ResetsCircuit()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("claude-code-subscription", _credPath);
            // Simulate token refresh via `claude login` → success resets circuit.
            circuit.RecordSuccess("claude-code-subscription", _credPath);

            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, OkJson));
            var provider = MakeProvider(http, circuit);

            var response = await provider.CompleteAsync(MakeRequest());
            response.Content.Should().Be("hello");
            circuit.IsOpen("claude-code-subscription", _credPath, out _)
                .Should().BeFalse("success closes the circuit");
        }

        // ── Test 4: 429 (rate limit) does NOT open auth circuit ───────────────
        [Fact]
        public async Task CompleteAsync_401vs429_OnlyAuthOpensCircuit()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.TooManyRequests, @"{""error"": {""message"": ""rate limited""}}"));
            var provider = MakeProvider(http, circuit);

            for (int i = 0; i < 5; i++)
            {
                await Assert.ThrowsAsync<RateLimitException>(() => provider.CompleteAsync(MakeRequest()));
            }

            circuit.IsOpen("claude-code-subscription", _credPath, out _)
                .Should().BeFalse("rate-limit errors must not open auth circuit");
        }
    }
}
