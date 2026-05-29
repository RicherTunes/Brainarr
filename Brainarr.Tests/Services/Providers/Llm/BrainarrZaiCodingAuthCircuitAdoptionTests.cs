using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.TestKit.Testing;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Llm
{
    /// <summary>
    /// Wave-7C characterization tests: verifies <see cref="BrainarrZaiCodingProvider"/>
    /// integrates correctly with <see cref="LlmAuthCircuit"/> (4-pact).
    ///
    /// <para>
    /// ZaiCoding speaks the Anthropic Messages wire format and — because Z.AI's Coding-Plan gate
    /// requires a custom User-Agent that Lidarr's IHttpClient forbids — dispatches via a RAW
    /// HttpClient. These tests therefore inject a fake <see cref="HttpMessageHandler"/> via the
    /// internal test-seam constructor rather than a fake IHttpClient.
    /// </para>
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class BrainarrZaiCodingAuthCircuitAdoptionTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const string ApiKey = "zai-test-coding-key-abc123";

        // Anthropic Messages wire shape (same as BrainarrAnthropicProvider / ClaudeCodeSub).
        private const string OkJson = @"{
            ""id"": ""msg_test"",
            ""content"": [{""type"": ""text"", ""text"": ""hello""}],
            ""stop_reason"": ""end_turn"",
            ""usage"": {""input_tokens"": 5, ""output_tokens"": 3}
        }";

        private static LlmRequest MakeRequest() =>
            new LlmRequest { Prompt = "test", MaxTokens = 10 };

        /// <summary>Stub raw-HTTP handler returning a fixed status/body and counting calls.</summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            public int Calls { get; private set; }

            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
            }
        }

        // ZaiCoding dispatches via a raw HttpClient; inject the handler through the test seam.
        // The IHttpClient arg is accepted for signature stability but unused by this provider.
        private static BrainarrZaiCodingProvider MakeProvider(HttpMessageHandler handler, LlmAuthCircuit circuit)
            => new BrainarrZaiCodingProvider(
                new Mock<IHttpClient>().Object, Logger, ApiKey, model: null, authCircuit: circuit, testHandler: handler);

        // ── Test 1: open circuit throws immediately (no HTTP call) ─────────────
        [Fact]
        public async Task CompleteAsync_OpenCircuit_ThrowsImmediately()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("zaicoding", ApiKey);

            var handler = new StubHandler(HttpStatusCode.OK, OkJson);
            var provider = MakeProvider(handler, circuit);

            var act = async () => await provider.CompleteAsync(MakeRequest());
            await act.Should().ThrowAsync<AuthenticationException>()
                .WithMessage("*Auth circuit open*");

            handler.Calls.Should().Be(0, "no HTTP call should be made when circuit is open");
        }

        // ── Test 2: 401 calls RecordAuthFailure ───────────────────────────────
        [Fact]
        public async Task CompleteAsync_OnHttp401_CallsRecordAuthFailure()
        {
            var clock = new FakeTimeProvider();
            // threshold=1 so a single 401 latches the circuit — observable side-effect
            var circuit = new LlmAuthCircuit(null, 1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);
            var handler = new StubHandler(HttpStatusCode.Unauthorized, @"{""error"": {""message"": ""invalid api key""}}");
            var provider = MakeProvider(handler, circuit);

            await Assert.ThrowsAsync<AuthenticationException>(() => provider.CompleteAsync(MakeRequest()));

            circuit.IsOpen("zaicoding", ApiKey, out _)
                .Should().BeTrue("RecordAuthFailure must have been called with the provider id + api key");
        }

        // ── Test 3: 200 calls RecordSuccess ───────────────────────────────────
        [Fact]
        public async Task CompleteAsync_OnSuccess_CallsRecordSuccess()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            // Pre-open circuit then reset via RecordSuccess to confirm the provider calls it.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("zaicoding", ApiKey);
            circuit.RecordSuccess("zaicoding", ApiKey); // allow one probe through

            var handler = new StubHandler(HttpStatusCode.OK, OkJson);
            var provider = MakeProvider(handler, circuit);

            var response = await provider.CompleteAsync(MakeRequest());
            response.Content.Should().Be("hello");
            circuit.IsOpen("zaicoding", ApiKey, out _)
                .Should().BeFalse("RecordSuccess must have been called, keeping the circuit closed");
        }

        // ── Test 4: 500 does NOT call RecordAuthFailure ───────────────────────
        [Fact]
        public async Task CompleteAsync_OnHttp500_DoesNotPoisonCircuit()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);
            var handler = new StubHandler(HttpStatusCode.InternalServerError, @"{""error"": {""message"": ""server error""}}");
            var provider = MakeProvider(handler, circuit);

            // A server error should throw but must NOT latch the auth circuit.
            // Wave-25 fix: contract is gate-state, not exception type.
            await Assert.ThrowsAnyAsync<Exception>(() => provider.CompleteAsync(MakeRequest()));

            circuit.IsOpen("zaicoding", ApiKey, out _)
                .Should().BeFalse("non-auth HTTP errors must not trigger RecordAuthFailure");
        }
    }
}
