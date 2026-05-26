using System;
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
    /// Wave-7C characterization tests: verifies <see cref="BrainarrDeepSeekProvider"/>
    /// integrates correctly with <see cref="LlmAuthCircuit"/> (4-pact).
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class BrainarrDeepSeekAuthCircuitAdoptionTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const string ApiKey = "sk-test-deepseek-key-abc";
        private const string OkJson = @"{
            ""choices"": [{""message"": {""content"": ""hello"", ""role"": ""assistant""}, ""finish_reason"": ""stop""}],
            ""usage"": {""prompt_tokens"": 5, ""completion_tokens"": 3, ""total_tokens"": 8}
        }";

        private static LlmRequest MakeRequest() =>
            new LlmRequest { Prompt = "test", MaxTokens = 10 };

        private static BrainarrDeepSeekProvider MakeProvider(
            FakeHttpClient http,
            LlmAuthCircuit circuit)
            => new BrainarrDeepSeekProvider(http, Logger, ApiKey, model: null, streamingExecutor: null, authCircuit: circuit);

        // ── Test 1: open circuit throws immediately (no HTTP call) ─────────────
        [Fact]
        public async Task CompleteAsync_OpenCircuit_ThrowsImmediately()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("deepseek", ApiKey);

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

            circuit.IsOpen("deepseek", ApiKey, out _)
                .Should().BeTrue("RecordAuthFailure must have been called with the provider id + api key");
        }

        // ── Test 3: 200 calls RecordSuccess ───────────────────────────────────
        [Fact]
        public async Task CompleteAsync_OnSuccess_CallsRecordSuccess()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            // Pre-open circuit then reset via RecordSuccess to confirm the provider calls it.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("deepseek", ApiKey);
            circuit.RecordSuccess("deepseek", ApiKey); // allow one probe through

            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, OkJson));
            var provider = MakeProvider(http, circuit);

            var response = await provider.CompleteAsync(MakeRequest());
            response.Content.Should().Be("hello");
            circuit.IsOpen("deepseek", ApiKey, out _)
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
            await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(MakeRequest()));

            circuit.IsOpen("deepseek", ApiKey, out _)
                .Should().BeFalse("non-auth HTTP errors must not trigger RecordAuthFailure");
        }
    }
}
