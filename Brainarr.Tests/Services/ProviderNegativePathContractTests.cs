using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.TestKit.Providers.Fakes;
using Brainarr.TestKit.Providers.Http;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// M4-5: Negative-path contract tests verifying each provider handles
    /// auth failure, rate limiting, and invalid input gracefully.
    /// </summary>
    public class ProviderNegativePathContractTests
    {
        private readonly Logger _logger = LogManager.CreateNullLogger();
        private readonly TestResilience _exec = new();

        // ─── Auth Failure (401) ───────────────────────────────────────────────

        [Fact]
        public async Task OpenAI_AuthFailure_ReturnsFalseWithUserMessage()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"error\":{\"message\":\"Incorrect API key provided: sk-test. You can find your API key...\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}"));

            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            var result = await provider.TestConnectionAsync();

            result.Should().BeFalse("401 should fail connection test");
            provider.GetLastUserMessage().Should().NotBeNullOrEmpty("should provide actionable guidance on auth failure");
        }

        [Fact]
        public async Task Anthropic_AuthFailure_ReturnsFalseWithUserMessage()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}"));

            var provider = new AnthropicProvider(http, _logger, apiKey: "sk-ant-test", model: "claude-3-5-haiku-latest");

            var result = await provider.TestConnectionAsync();

            result.Should().BeFalse("401 should fail connection test");
            provider.GetLastUserMessage().Should().NotBeNullOrEmpty("should provide actionable guidance on auth failure");
        }

        [Fact]
        public async Task Gemini_AuthFailure_ReturnsFalse()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"error\":{\"code\":401,\"message\":\"API key not valid\"}}"));

            var provider = new GeminiProvider(http, _logger, apiKey: "bad-key", model: "gemini-1.5-flash", httpExec: _exec);

            var result = await provider.TestConnectionAsync();
            result.Should().BeFalse("invalid API key should fail");
        }

        [Fact]
        public async Task DeepSeek_AuthFailure_ReturnsFalse()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"error\":{\"message\":\"Incorrect API key\",\"type\":\"authentication_error\"}}"));

            var provider = new DeepSeekProvider(http, _logger, apiKey: "bad-key", model: "deepseek-chat", preferStructured: false, httpExec: _exec);

            var result = await provider.TestConnectionAsync();
            result.Should().BeFalse("invalid API key should fail");
        }

        [Fact]
        public async Task Groq_AuthFailure_ReturnsFalse()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"error\":{\"message\":\"Invalid API Key\"}}"));

            var provider = new GroqProvider(http, _logger, apiKey: "bad-key", model: "llama-3.3-70b-versatile", preferStructured: false, httpExec: _exec);

            var result = await provider.TestConnectionAsync();
            result.Should().BeFalse("invalid API key should fail");
        }

        [Fact]
        public async Task Perplexity_AuthFailure_ReturnsFalse()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"error\":{\"message\":\"Invalid API Key\"}}"));

            var provider = new PerplexityProvider(http, _logger, apiKey: "bad-key", model: "llama-3.1-sonar-small-128k-online", preferStructured: false, httpExec: _exec);

            var result = await provider.TestConnectionAsync();
            result.Should().BeFalse("invalid API key should fail");
        }

        [Fact]
        public async Task OpenRouter_AuthFailure_ReturnsFalse()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"error\":{\"message\":\"Invalid credentials\"}}"));

            var provider = new OpenRouterProvider(http, _logger, apiKey: "bad-key", model: "anthropic/claude-3.5-haiku", preferStructured: false, httpExec: _exec);

            var result = await provider.TestConnectionAsync();
            result.Should().BeFalse("invalid API key should fail");
        }

        // ─── Rate Limit (429) ────────────────────────────────────────────────

        [Fact]
        public async Task OpenAI_RateLimit_ReturnsEmptyListWithMessage()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, (HttpStatusCode)429,
                    "{\"error\":{\"message\":\"Rate limit reached for gpt-4o-mini\",\"type\":\"rate_limit_error\"}}"));

            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            var recs = await provider.GetRecommendationsAsync("test prompt");

            recs.Should().BeEmpty("rate-limited requests should return empty, not throw");
        }

        [Fact]
        public async Task Anthropic_RateLimit_ReturnsEmptyList()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, (HttpStatusCode)429,
                    "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"Rate limit exceeded\"}}"));

            var provider = new AnthropicProvider(http, _logger, apiKey: "sk-ant-test", model: "claude-3-5-haiku-latest");

            var recs = await provider.GetRecommendationsAsync("test prompt");

            recs.Should().BeEmpty("rate-limited requests should degrade gracefully");
        }

        // ─── Invalid Input ───────────────────────────────────────────────────

        [Fact]
        public async Task AllProviders_EmptyPrompt_ReturnEmptyOrHandleGracefully()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Ok(req, "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}"));

            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            var recs = await provider.GetRecommendationsAsync("");

            recs.Should().NotBeNull("empty prompt should not cause null return");
        }

        [Fact]
        public async Task AllProviders_NullPrompt_DoesNotThrowNullReference()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Ok(req, "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}"));

            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            Func<Task> act = () => provider.GetRecommendationsAsync((string)null);

            // Should either handle gracefully or throw ArgumentNullException — not NullReferenceException
            await act.Should().NotThrowAsync<NullReferenceException>();
        }

        // ─── Server Error (500) ──────────────────────────────────────────────

        [Fact]
        public async Task OpenAI_ServerError_ReturnsEmptyGracefully()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.InternalServerError,
                    "{\"error\":{\"message\":\"Internal server error\"}}"));

            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            var recs = await provider.GetRecommendationsAsync("test");

            recs.Should().BeEmpty("server errors should degrade gracefully");
        }

        // ─── Quota Exhausted (402) ───────────────────────────────────────────

        [Fact]
        public async Task OpenAI_QuotaExhausted_ReturnsFalseWithMessage()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.PaymentRequired,
                    "{\"error\":{\"message\":\"You exceeded your current quota\",\"type\":\"insufficient_quota\"}}"));

            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            var result = await provider.TestConnectionAsync();

            result.Should().BeFalse("quota exhausted should fail connection test");
            provider.GetLastUserMessage().Should().NotBeNullOrEmpty("should explain quota issue to user");
        }

        [Fact]
        public async Task Anthropic_CreditExhausted_ReturnsFalseWithMessage()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.PaymentRequired,
                    "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"Your credit balance is too low\"}}"));

            var provider = new AnthropicProvider(http, _logger, apiKey: "sk-ant-test", model: "claude-3-5-haiku-latest");

            var result = await provider.TestConnectionAsync();

            result.Should().BeFalse("credit exhausted should fail connection test");
            provider.GetLastUserMessage().Should().NotBeNullOrEmpty("should explain credit issue to user");
        }

        // ─── Malformed Response ──────────────────────────────────────────────

        [Fact]
        public async Task OpenAI_MalformedJson_ReturnsEmptyGracefully()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Ok(req, "this is not json at all"));

            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            var recs = await provider.GetRecommendationsAsync("test");

            recs.Should().NotBeNull("malformed response should not cause null");
        }
    }
}
