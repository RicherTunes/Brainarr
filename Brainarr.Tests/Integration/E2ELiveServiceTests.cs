using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.TestKit.Providers.Http;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Xunit;
using Xunit.Abstractions;

namespace Brainarr.Tests.Integration
{
    /// <summary>
    /// M5-6 / N1-1: Live-service E2E tests that call real AI provider APIs.
    /// Guarded by environment variables containing API keys — skipped when absent.
    /// Run nightly; rate-limited failures are classified as inconclusive (not fail).
    /// <para>
    /// <b>Design constraints:</b>
    /// <list type="bullet">
    /// <item>Uses <see cref="LiveHttpClient"/> which bypasses Lidarr's HTTP pipeline
    /// (proxy/certs) and transparently retries HTTP 429 with Retry-After support.
    /// Results verify provider API connectivity and response parsing,
    /// not Lidarr HTTP infrastructure.</item>
    /// <item>Each test is a cheap single-request probe (TestConnection or one
    /// GetRecommendations call) to avoid rate-limit noise and API cost.</item>
    /// <item>If a provider returns 429 after all retries, the test passes with an
    /// <c>[INCONCLUSIVE:RATE_LIMITED]</c> marker instead of failing. The nightly
    /// workflow uses these markers to classify runs as inconclusive vs real failures.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Area", "E2E/Live")]
    public class E2ELiveServiceTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Logger _logger = LogManager.CreateNullLogger();
        private readonly LiveHttpClient _http = new();

        public E2ELiveServiceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose() => _http.Dispose();

        private static string? GetEnv(string name) =>
            Environment.GetEnvironmentVariable(name);

        private bool RequireKey(string envVar)
        {
            if (!string.IsNullOrWhiteSpace(GetEnv(envVar))) return true;
            _output.WriteLine($"SKIPPED: {envVar} not configured");
            return false;
        }

        /// <summary>
        /// Assert a connection result, treating rate-limited failures as inconclusive.
        /// If the assertion would fail but the HTTP client saw 429 responses, the test
        /// passes with an [INCONCLUSIVE:RATE_LIMITED] marker for workflow classification.
        /// </summary>
        private void AssertConnectedOrInconclusive(bool connected, string provider)
        {
            _output.WriteLine($"{provider} connection test: {(connected ? "SUCCESS" : "FAILED")}");

            if (!connected && _http.ThrottledRequestCount > 0)
            {
                _output.WriteLine($"[INCONCLUSIVE:RATE_LIMITED] {provider} failed after {_http.ThrottledRequestCount} throttled request(s)");
                return;
            }

            connected.Should().BeTrue($"{provider} API should be reachable with valid key");
        }

        /// <summary>
        /// Assert recommendations are non-empty, treating rate-limited failures as inconclusive.
        /// </summary>
        private void AssertRecsOrInconclusive(IList<Recommendation> recs, string provider)
        {
            _output.WriteLine($"{provider} returned {recs.Count} recommendations");
            foreach (var r in recs)
                _output.WriteLine($"  - {r.Artist} / {r.Album} ({r.Genre}) [{r.Confidence:P0}]");

            if (recs.Count == 0 && _http.ThrottledRequestCount > 0)
            {
                _output.WriteLine($"[INCONCLUSIVE:RATE_LIMITED] {provider} returned 0 recs after {_http.ThrottledRequestCount} throttled request(s)");
                return;
            }

            recs.Should().NotBeEmpty($"{provider} should return at least one recommendation");
        }

        // ─── OpenAI Live Tests ──────────────────────────────────────────────

        [Fact]
        public async Task Live_OpenAI_TestConnection()
        {
            if (!RequireKey("OPENAI_API_KEY")) return;

            var provider = new OpenAIProvider(_http, _logger, apiKey: GetEnv("OPENAI_API_KEY")!, model: "gpt-4o-mini", preferStructured: false);
            var connected = await provider.TestConnectionAsync();

            AssertConnectedOrInconclusive(connected, "OpenAI");
        }

        [Fact]
        public async Task Live_OpenAI_GetRecommendations()
        {
            if (!RequireKey("OPENAI_API_KEY")) return;

            var provider = new OpenAIProvider(_http, _logger, apiKey: GetEnv("OPENAI_API_KEY")!, model: "gpt-4o-mini", preferStructured: false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var recs = await provider.GetRecommendationsAsync("Recommend 3 progressive rock albums similar to Pink Floyd", cts.Token);

            AssertRecsOrInconclusive(recs, "OpenAI");
            if (recs.Count > 0)
                recs[0].Artist.Should().NotBeNullOrEmpty();
        }

        // ─── Anthropic Live Tests ───────────────────────────────────────────

        [Fact]
        public async Task Live_Anthropic_TestConnection()
        {
            if (!RequireKey("ANTHROPIC_API_KEY")) return;

            var provider = new AnthropicProvider(_http, _logger, apiKey: GetEnv("ANTHROPIC_API_KEY")!, model: "claude-3-5-haiku-latest");
            var connected = await provider.TestConnectionAsync();

            AssertConnectedOrInconclusive(connected, "Anthropic");
        }

        [Fact]
        public async Task Live_Anthropic_GetRecommendations()
        {
            if (!RequireKey("ANTHROPIC_API_KEY")) return;

            var provider = new AnthropicProvider(_http, _logger, apiKey: GetEnv("ANTHROPIC_API_KEY")!, model: "claude-3-5-haiku-latest");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var recs = await provider.GetRecommendationsAsync("Recommend 3 jazz albums similar to Miles Davis Kind of Blue", cts.Token);

            AssertRecsOrInconclusive(recs, "Anthropic");
        }

        // ─── Groq Live Tests ────────────────────────────────────────────────

        [Fact]
        public async Task Live_Groq_TestConnection()
        {
            if (!RequireKey("GROQ_API_KEY")) return;

            var provider = new GroqProvider(_http, _logger, apiKey: GetEnv("GROQ_API_KEY")!);
            var connected = await provider.TestConnectionAsync();

            AssertConnectedOrInconclusive(connected, "Groq");
        }

        [Fact]
        public async Task Live_Groq_GetRecommendations()
        {
            if (!RequireKey("GROQ_API_KEY")) return;

            var provider = new GroqProvider(_http, _logger, apiKey: GetEnv("GROQ_API_KEY")!);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var recs = await provider.GetRecommendationsAsync("Recommend 3 electronic music albums", cts.Token);

            AssertRecsOrInconclusive(recs, "Groq");
        }

        // ─── DeepSeek Live Tests ────────────────────────────────────────────

        [Fact]
        public async Task Live_DeepSeek_TestConnection()
        {
            if (!RequireKey("DEEPSEEK_API_KEY")) return;

            var provider = new DeepSeekProvider(_http, _logger, apiKey: GetEnv("DEEPSEEK_API_KEY")!);
            var connected = await provider.TestConnectionAsync();

            AssertConnectedOrInconclusive(connected, "DeepSeek");
        }

        // ─── Gemini Live Tests ──────────────────────────────────────────────

        [Fact]
        public async Task Live_Gemini_TestConnection()
        {
            if (!RequireKey("GEMINI_API_KEY")) return;

            var provider = new GeminiProvider(_http, _logger, apiKey: GetEnv("GEMINI_API_KEY")!, model: "gemini-1.5-flash");
            var connected = await provider.TestConnectionAsync();

            AssertConnectedOrInconclusive(connected, "Gemini");
        }

        [Fact]
        public async Task Live_Gemini_GetRecommendations()
        {
            if (!RequireKey("GEMINI_API_KEY")) return;

            var provider = new GeminiProvider(_http, _logger, apiKey: GetEnv("GEMINI_API_KEY")!, model: "gemini-1.5-flash");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var recs = await provider.GetRecommendationsAsync("Recommend 3 classical music albums", cts.Token);

            AssertRecsOrInconclusive(recs, "Gemini");
        }
    }
}
