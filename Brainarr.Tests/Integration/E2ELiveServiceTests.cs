using System;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.TestKit.Providers.Http;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Xunit;
using Xunit.Abstractions;

namespace Brainarr.Tests.Integration
{
    /// <summary>
    /// M5-6: Live-service E2E tests that call real AI provider APIs.
    /// Guarded by environment variables containing API keys — skipped when absent.
    /// Run nightly; failures are warnings, not blockers (live services are flaky).
    /// <para>
    /// <b>Design constraints:</b>
    /// <list type="bullet">
    /// <item>Uses <see cref="LiveHttpClient"/> which bypasses Lidarr's HTTP pipeline
    /// (proxy/certs). Results verify provider API connectivity and response parsing,
    /// not Lidarr HTTP infrastructure.</item>
    /// <item>Each test is a cheap single-request probe (TestConnection or one
    /// GetRecommendations call) to avoid rate-limit noise and API cost.</item>
    /// <item>Providers that return 429 will fail gracefully (empty result) via
    /// the provider's built-in retry/error handling — no special backoff here.</item>
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

        // ─── OpenAI Live Tests ──────────────────────────────────────────────

        [Fact]
        public async Task Live_OpenAI_TestConnection()
        {
            if (!RequireKey("OPENAI_API_KEY")) return;

            var provider = new OpenAIProvider(_http, _logger, apiKey: GetEnv("OPENAI_API_KEY")!, model: "gpt-4o-mini", preferStructured: false);

            var connected = await provider.TestConnectionAsync();

            _output.WriteLine($"OpenAI connection test: {(connected ? "SUCCESS" : "FAILED")}");
            connected.Should().BeTrue("OpenAI API should be reachable with valid key");
        }

        [Fact]
        public async Task Live_OpenAI_GetRecommendations()
        {
            if (!RequireKey("OPENAI_API_KEY")) return;

            var provider = new OpenAIProvider(_http, _logger, apiKey: GetEnv("OPENAI_API_KEY")!, model: "gpt-4o-mini", preferStructured: false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var recs = await provider.GetRecommendationsAsync("Recommend 3 progressive rock albums similar to Pink Floyd", cts.Token);

            _output.WriteLine($"OpenAI returned {recs.Count} recommendations");
            foreach (var r in recs)
                _output.WriteLine($"  - {r.Artist} / {r.Album} ({r.Genre}) [{r.Confidence:P0}]");

            recs.Should().NotBeEmpty("should return at least one recommendation");
            recs[0].Artist.Should().NotBeNullOrEmpty();
        }

        // ─── Anthropic Live Tests ───────────────────────────────────────────

        [Fact]
        public async Task Live_Anthropic_TestConnection()
        {
            if (!RequireKey("ANTHROPIC_API_KEY")) return;

            var provider = new AnthropicProvider(_http, _logger, apiKey: GetEnv("ANTHROPIC_API_KEY")!, model: "claude-3-5-haiku-latest");

            var connected = await provider.TestConnectionAsync();

            _output.WriteLine($"Anthropic connection test: {(connected ? "SUCCESS" : "FAILED")}");
            connected.Should().BeTrue("Anthropic API should be reachable with valid key");
        }

        [Fact]
        public async Task Live_Anthropic_GetRecommendations()
        {
            if (!RequireKey("ANTHROPIC_API_KEY")) return;

            var provider = new AnthropicProvider(_http, _logger, apiKey: GetEnv("ANTHROPIC_API_KEY")!, model: "claude-3-5-haiku-latest");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var recs = await provider.GetRecommendationsAsync("Recommend 3 jazz albums similar to Miles Davis Kind of Blue", cts.Token);

            _output.WriteLine($"Anthropic returned {recs.Count} recommendations");
            foreach (var r in recs)
                _output.WriteLine($"  - {r.Artist} / {r.Album} ({r.Genre}) [{r.Confidence:P0}]");

            recs.Should().NotBeEmpty("should return at least one recommendation");
        }

        // ─── Groq Live Tests ────────────────────────────────────────────────

        [Fact]
        public async Task Live_Groq_TestConnection()
        {
            if (!RequireKey("GROQ_API_KEY")) return;

            var provider = new GroqProvider(_http, _logger, apiKey: GetEnv("GROQ_API_KEY")!);

            var connected = await provider.TestConnectionAsync();

            _output.WriteLine($"Groq connection test: {(connected ? "SUCCESS" : "FAILED")}");
            connected.Should().BeTrue("Groq API should be reachable with valid key");
        }

        [Fact]
        public async Task Live_Groq_GetRecommendations()
        {
            if (!RequireKey("GROQ_API_KEY")) return;

            var provider = new GroqProvider(_http, _logger, apiKey: GetEnv("GROQ_API_KEY")!);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var recs = await provider.GetRecommendationsAsync("Recommend 3 electronic music albums", cts.Token);

            _output.WriteLine($"Groq returned {recs.Count} recommendations");
            foreach (var r in recs)
                _output.WriteLine($"  - {r.Artist} / {r.Album} ({r.Genre}) [{r.Confidence:P0}]");

            recs.Should().NotBeEmpty("should return at least one recommendation");
        }

        // ─── DeepSeek Live Tests ────────────────────────────────────────────

        [Fact]
        public async Task Live_DeepSeek_TestConnection()
        {
            if (!RequireKey("DEEPSEEK_API_KEY")) return;

            var provider = new DeepSeekProvider(_http, _logger, apiKey: GetEnv("DEEPSEEK_API_KEY")!);

            var connected = await provider.TestConnectionAsync();

            _output.WriteLine($"DeepSeek connection test: {(connected ? "SUCCESS" : "FAILED")}");
            connected.Should().BeTrue("DeepSeek API should be reachable with valid key");
        }

        // ─── Gemini Live Tests ──────────────────────────────────────────────

        [Fact]
        public async Task Live_Gemini_TestConnection()
        {
            if (!RequireKey("GEMINI_API_KEY")) return;

            var provider = new GeminiProvider(_http, _logger, apiKey: GetEnv("GEMINI_API_KEY")!, model: "gemini-1.5-flash");

            var connected = await provider.TestConnectionAsync();

            _output.WriteLine($"Gemini connection test: {(connected ? "SUCCESS" : "FAILED")}");
            connected.Should().BeTrue("Gemini API should be reachable with valid key");
        }

        [Fact]
        public async Task Live_Gemini_GetRecommendations()
        {
            if (!RequireKey("GEMINI_API_KEY")) return;

            var provider = new GeminiProvider(_http, _logger, apiKey: GetEnv("GEMINI_API_KEY")!, model: "gemini-1.5-flash");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var recs = await provider.GetRecommendationsAsync("Recommend 3 classical music albums", cts.Token);

            _output.WriteLine($"Gemini returned {recs.Count} recommendations");
            foreach (var r in recs)
                _output.WriteLine($"  - {r.Artist} / {r.Album} ({r.Genre}) [{r.Confidence:P0}]");

            recs.Should().NotBeEmpty("should return at least one recommendation");
        }
    }
}
