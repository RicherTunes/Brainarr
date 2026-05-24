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
    /// Wave-7C adoption tests: verifies <see cref="BrainarrOpenAiProvider"/>
    /// integrates correctly with <see cref="LlmAuthCircuit"/>.
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class OpenAiAuthCircuitAdoptionTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const string ApiKey = "sk-test-openai-key";
        private const string OkJson = @"{
            ""choices"": [{""message"": {""content"": ""hello"", ""role"": ""assistant""}, ""finish_reason"": ""stop""}],
            ""usage"": {""prompt_tokens"": 5, ""completion_tokens"": 3, ""total_tokens"": 8}
        }";

        private static LlmRequest MakeRequest() =>
            new LlmRequest { Prompt = "test", MaxTokens = 10 };

        private static BrainarrOpenAiProvider MakeProvider(
            FakeHttpClient http,
            LlmAuthCircuit circuit)
            => new BrainarrOpenAiProvider(http, Logger, ApiKey, model: null, streamingExecutor: null, authCircuit: circuit);

        // ── Test 1: open circuit throws immediately (no HTTP call) ─────────────
        [Fact]
        public async Task CompleteAsync_OpenCircuit_ThrowsImmediately()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            // Manually open the circuit.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("openai", ApiKey);

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

        // ── Test 2: 3 consecutive 401s open the circuit ───────────────────────
        [Fact]
        public async Task CompleteAsync_3xConsecutive401_OpensCircuit()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized, @"{""error"": ""invalid_api_key""}"));
            var provider = MakeProvider(http, circuit);

            for (int i = 0; i < 3; i++)
            {
                await Assert.ThrowsAsync<AuthenticationException>(() => provider.CompleteAsync(MakeRequest()));
            }

            circuit.IsOpen("openai", ApiKey, out _).Should().BeTrue("circuit should open after 3 consecutive 401s");
        }

        // ── Test 3: success resets circuit ────────────────────────────────────
        [Fact]
        public async Task CompleteAsync_Success_ResetsCircuit()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);

            // Open it.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure("openai", ApiKey);
            circuit.IsOpen("openai", ApiKey, out _).Should().BeTrue();

            // Now reset via RecordSuccess directly (simulating token rotation).
            circuit.RecordSuccess("openai", ApiKey);

            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, OkJson));
            var provider = MakeProvider(http, circuit);

            var response = await provider.CompleteAsync(MakeRequest());
            response.Content.Should().Be("hello");
            circuit.IsOpen("openai", ApiKey, out _).Should().BeFalse("success via provider closes circuit");
        }

        // ── Test 4: 429 (rate limit) does NOT open auth circuit ───────────────
        [Fact]
        public async Task CompleteAsync_401vs429_OnlyAuthOpensCircuit()
        {
            var clock = new FakeTimeProvider();
            var circuit = new LlmAuthCircuit(null, 3, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), clock);
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.TooManyRequests, @"{""error"": ""rate_limited""}"));
            var provider = MakeProvider(http, circuit);

            // 5 rate-limit errors — circuit should NOT open (wrong error code).
            for (int i = 0; i < 5; i++)
            {
                await Assert.ThrowsAsync<RateLimitException>(() => provider.CompleteAsync(MakeRequest()));
            }

            circuit.IsOpen("openai", ApiKey, out _).Should().BeFalse("rate-limit errors must not open the auth circuit");
        }
    }
}
