using System;
using System.Collections.Generic;
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
    /// Wave-7C adoption tests: verifies <see cref="BrainarrAnthropicProvider"/>
    /// integrates correctly with <see cref="LlmAuthCircuit"/>.
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class AnthropicAuthCircuitAdoptionTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const string ApiKey = "test-anthropic-key-abc";
        private const string OkJson = @"{
            ""id"": ""msg_test"",
            ""content"": [{""type"": ""text"", ""text"": ""hello""}],
            ""stop_reason"": ""end_turn"",
            ""usage"": {""input_tokens"": 5, ""output_tokens"": 3}
        }";

        private static LlmRequest MakeRequest() =>
            new LlmRequest { Prompt = "test", MaxTokens = 10 };

        private static BrainarrAnthropicProvider MakeProvider(
            FakeHttpClient http,
            LlmAuthCircuit circuit)
            => new BrainarrAnthropicProvider(http, Logger, ApiKey, model: null, streamingExecutor: null, authCircuit: circuit);

        // ── Test 1: open circuit throws immediately (no HTTP call) ─────────────
        [Fact]
        public async Task CompleteAsync_OpenCircuit_ThrowsImmediately()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("anthropic", ApiKey);

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
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized, @"{""error"": {""message"": ""invalid api key""}}"));
            var provider = MakeProvider(http, circuit);

            for (int i = 0; i < 3; i++)
            {
                await Assert.ThrowsAsync<AuthenticationException>(() => provider.CompleteAsync(MakeRequest()));
            }

            circuit.IsOpen("anthropic", ApiKey, out _).Should().BeTrue("circuit opens after 3 consecutive 401s");
        }

        // ── Test 3: success resets circuit ────────────────────────────────────
        [Fact]
        public async Task CompleteAsync_Success_ResetsCircuit()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("anthropic", ApiKey);
            circuit.RecordSuccess("anthropic", ApiKey); // simulate token rotation

            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, OkJson));
            var provider = MakeProvider(http, circuit);

            var response = await provider.CompleteAsync(MakeRequest());
            response.Content.Should().Be("hello");
            circuit.IsOpen("anthropic", ApiKey, out _).Should().BeFalse("success closes circuit");
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

            circuit.IsOpen("anthropic", ApiKey, out _).Should().BeFalse("rate-limit errors must not open auth circuit");
        }
    }
}
