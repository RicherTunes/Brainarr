using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Verifies the brainarr-public <c>IAIProvider</c> seam still produces music
    /// recommendations when its inner LLM is replaced with the new
    /// <see cref="ILlmProvider"/> + <see cref="LlmProviderAdapter"/> path.
    /// </summary>
    public class LlmProviderAdapterTests
    {
        private readonly Logger _logger;

        public LlmProviderAdapterTests()
        {
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public async Task GetRecommendationsAsync_HappyPath_ParsesRecommendations()
        {
            var content = "{\"recommendations\":[{\"artist\":\"Pink Floyd\",\"album\":\"The Wall\",\"genre\":\"Progressive Rock\",\"confidence\":0.95,\"reason\":\"Genre match\"}]}";
            var llm = new StubLlmProvider("openai", "OpenAI", new LlmResponse { Content = content });
            var adapter = new LlmProviderAdapter(llm, _logger);

            var recommendations = await adapter.GetRecommendationsAsync("prompt");

            recommendations.Should().HaveCount(1);
            recommendations[0].Artist.Should().Be("Pink Floyd");
            recommendations[0].Album.Should().Be("The Wall");
            recommendations[0].Genre.Should().Be("Progressive Rock");
        }

        [Fact]
        public async Task GetRecommendationsAsync_ArrayContent_ParsesRecommendations()
        {
            var content = "[{\"artist\":\"A\",\"album\":\"B\",\"confidence\":0.5}]";
            var llm = new StubLlmProvider("openai", "OpenAI", new LlmResponse { Content = content });
            var adapter = new LlmProviderAdapter(llm, _logger);

            var recommendations = await adapter.GetRecommendationsAsync("prompt");

            recommendations.Should().HaveCount(1);
            recommendations[0].Artist.Should().Be("A");
        }

        [Fact]
        public async Task GetRecommendationsAsync_EmptyContent_ReturnsEmpty()
        {
            var llm = new StubLlmProvider("openai", "OpenAI", new LlmResponse { Content = string.Empty });
            var adapter = new LlmProviderAdapter(llm, _logger);

            var recommendations = await adapter.GetRecommendationsAsync("prompt");

            recommendations.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_LlmThrowsAuth_ReturnsEmptyAndCapturesHint()
        {
            var llm = new ThrowingLlmProvider(
                "openai",
                "OpenAI",
                new AuthenticationException("openai", "Invalid API key"));
            var adapter = new LlmProviderAdapter(llm, _logger);

            var recommendations = await adapter.GetRecommendationsAsync("prompt");

            recommendations.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().NotBeNull();
            adapter.GetLastUserMessage().Should().Contain("OpenAI");
        }

        [Fact]
        public async Task GetRecommendationsAsync_LlmThrowsRateLimit_ReturnsEmpty()
        {
            var llm = new ThrowingLlmProvider(
                "anthropic",
                "Anthropic",
                new RateLimitException("anthropic", "Rate limit exceeded"));
            var adapter = new LlmProviderAdapter(llm, _logger);

            var recommendations = await adapter.GetRecommendationsAsync("prompt");

            recommendations.Should().BeEmpty();
            adapter.GetLastUserMessage()!.ToLowerInvariant().Should().Contain("rate limit");
        }

        [Fact]
        public async Task TestConnectionAsync_Healthy_ReturnsTrue()
        {
            var llm = new StubLlmProvider(
                "openai",
                "OpenAI",
                new LlmResponse { Content = "OK" },
                ProviderHealthResult.Healthy());
            var adapter = new LlmProviderAdapter(llm, _logger);

            (await adapter.TestConnectionAsync()).Should().BeTrue();
        }

        [Fact]
        public async Task TestConnectionAsync_Unhealthy_ReturnsFalse()
        {
            var llm = new StubLlmProvider(
                "openai",
                "OpenAI",
                new LlmResponse { Content = "OK" },
                ProviderHealthResult.Unhealthy("HTTP 401"));
            var adapter = new LlmProviderAdapter(llm, _logger);

            (await adapter.TestConnectionAsync()).Should().BeFalse();
        }

        [Fact]
        public void ProviderName_DelegatesToLlmDisplayName()
        {
            var llm = new StubLlmProvider("openai", "OpenAI", new LlmResponse { Content = "" });
            var adapter = new LlmProviderAdapter(llm, _logger);

            adapter.ProviderName.Should().Be("OpenAI");
        }

        [Fact]
        public void Inner_ExposesWrappedProvider()
        {
            var llm = new StubLlmProvider("openai", "OpenAI", new LlmResponse { Content = "" });
            var adapter = new LlmProviderAdapter(llm, _logger);

            adapter.Inner.Should().BeSameAs(llm);
        }

        [Fact]
        public async Task GetRecommendationsAsync_PassesSystemPrompt_WhenCapabilitySet()
        {
            // Adapter only attaches a system prompt when the underlying provider advertises
            // the SystemPrompt capability flag.
            var captured = (LlmRequest?)null;
            var llm = new RecordingLlmProvider(
                "openai",
                "OpenAI",
                LlmCapabilityFlags.TextCompletion | LlmCapabilityFlags.SystemPrompt,
                req => { captured = req; return new LlmResponse { Content = "[]" }; });
            var adapter = new LlmProviderAdapter(llm, _logger, systemPrompt: "be helpful");

            await adapter.GetRecommendationsAsync("prompt");

            captured.Should().NotBeNull();
            captured!.SystemPrompt.Should().Be("be helpful");
        }

        [Fact]
        public async Task GetRecommendationsAsync_OmitsSystemPrompt_WhenCapabilityNotSet()
        {
            var captured = (LlmRequest?)null;
            var llm = new RecordingLlmProvider(
                "ollama",
                "Ollama",
                LlmCapabilityFlags.TextCompletion, // no SystemPrompt
                req => { captured = req; return new LlmResponse { Content = "[]" }; });
            var adapter = new LlmProviderAdapter(llm, _logger, systemPrompt: "be helpful");

            await adapter.GetRecommendationsAsync("prompt");

            captured.Should().NotBeNull();
            captured!.SystemPrompt.Should().BeNull();
        }

        [Fact]
        public async Task GetRecommendationsAsync_SetsJsonMode_WhenCapabilitySet()
        {
            // Phase 5b: brainarr always wants strict JSON for recommendation parsing. The
            // adapter must propagate JsonMode=true on the LlmRequest when the provider
            // advertises the JsonMode capability flag, so providers translate it to their
            // vendor-specific body field (response_format, responseMimeType, etc.).
            var captured = (LlmRequest?)null;
            var llm = new RecordingLlmProvider(
                "openai",
                "OpenAI",
                LlmCapabilityFlags.TextCompletion | LlmCapabilityFlags.SystemPrompt | LlmCapabilityFlags.JsonMode,
                req => { captured = req; return new LlmResponse { Content = "[]" }; });
            var adapter = new LlmProviderAdapter(llm, _logger);

            await adapter.GetRecommendationsAsync("prompt");

            captured.Should().NotBeNull();
            captured!.JsonMode.Should().BeTrue();
        }

        [Fact]
        public async Task GetRecommendationsAsync_OmitsJsonMode_WhenCapabilityNotSet()
        {
            // For providers that don't advertise JsonMode (Anthropic Messages API, Perplexity
            // Sonar, OpenAI-compatible catch-all), the adapter must NOT set the JsonMode
            // flag on the request — otherwise providers may emit response_format on routes
            // that 422 on the parameter.
            var captured = (LlmRequest?)null;
            var llm = new RecordingLlmProvider(
                "anthropic",
                "Anthropic",
                LlmCapabilityFlags.TextCompletion | LlmCapabilityFlags.SystemPrompt, // no JsonMode
                req => { captured = req; return new LlmResponse { Content = "[]" }; });
            var adapter = new LlmProviderAdapter(llm, _logger);

            await adapter.GetRecommendationsAsync("prompt");

            captured.Should().NotBeNull();
            captured!.JsonMode.Should().BeFalse();
        }

        // ---------------------------------------------------------------------
        // Stubs
        // ---------------------------------------------------------------------

        private sealed class StubLlmProvider : ILlmProvider
        {
            private readonly LlmResponse _response;
            private readonly ProviderHealthResult _health;

            public StubLlmProvider(
                string id,
                string name,
                LlmResponse response,
                ProviderHealthResult? health = null)
            {
                ProviderId = id;
                DisplayName = name;
                _response = response;
                _health = health ?? ProviderHealthResult.Healthy();
            }

            public string ProviderId { get; }
            public string DisplayName { get; }
            public LlmProviderCapabilities Capabilities => new()
            {
                Flags = LlmCapabilityFlags.TextCompletion | LlmCapabilityFlags.SystemPrompt,
            };

            public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(_health);

            public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
                => Task.FromResult(_response);

            public IAsyncEnumerable<LlmStreamChunk>? StreamAsync(LlmRequest request, CancellationToken cancellationToken = default)
                => null;
        }

        private sealed class ThrowingLlmProvider : ILlmProvider
        {
            private readonly Exception _toThrow;

            public ThrowingLlmProvider(string id, string name, Exception toThrow)
            {
                ProviderId = id;
                DisplayName = name;
                _toThrow = toThrow;
            }

            public string ProviderId { get; }
            public string DisplayName { get; }
            public LlmProviderCapabilities Capabilities => new()
            {
                Flags = LlmCapabilityFlags.TextCompletion | LlmCapabilityFlags.SystemPrompt,
            };

            public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(ProviderHealthResult.Unhealthy("forced"));

            public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
                => throw _toThrow;

            public IAsyncEnumerable<LlmStreamChunk>? StreamAsync(LlmRequest request, CancellationToken cancellationToken = default)
                => null;
        }

        private sealed class RecordingLlmProvider : ILlmProvider
        {
            private readonly LlmCapabilityFlags _flags;
            private readonly Func<LlmRequest, LlmResponse> _onComplete;

            public RecordingLlmProvider(
                string id,
                string name,
                LlmCapabilityFlags flags,
                Func<LlmRequest, LlmResponse> onComplete)
            {
                ProviderId = id;
                DisplayName = name;
                _flags = flags;
                _onComplete = onComplete;
            }

            public string ProviderId { get; }
            public string DisplayName { get; }
            public LlmProviderCapabilities Capabilities => new() { Flags = _flags };

            public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(ProviderHealthResult.Healthy());

            public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
                => Task.FromResult(_onComplete(request));

            public IAsyncEnumerable<LlmStreamChunk>? StreamAsync(LlmRequest request, CancellationToken cancellationToken = default)
                => null;
        }
    }
}
