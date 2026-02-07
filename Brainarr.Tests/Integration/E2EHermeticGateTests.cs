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
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Xunit;

namespace Brainarr.Tests.Integration
{
    /// <summary>
    /// M5: Hermetic E2E gate tests verifying the golden path (prompt → provider call → recommendations)
    /// and the auth-fail path (invalid key → graceful error, no crash, sensitive data redacted).
    /// These tests mock HTTP at the transport layer — no external calls.
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Area", "E2E/Hermetic")]
    public class E2EHermeticGateTests
    {
        private readonly Logger _logger = LogManager.CreateNullLogger();
        private readonly TestResilience _exec = new();

        // ─── Golden Path: Prompt → Provider → Recommendations ────────────────

        [Fact]
        public async Task GoldenPath_OpenAI_PromptToRecommendations()
        {
            var responseJson = @"{
                ""id"": ""chatcmpl-test"",
                ""choices"": [{
                    ""message"": {
                        ""content"": ""[{\""artist\"": \""Radiohead\"", \""album\"": \""OK Computer\"", \""genre\"": \""Alternative Rock\"", \""reason\"": \""Innovative album\"", \""confidence\"": 0.95}]""
                    }
                }]
            }";

            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, responseJson));
            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-test-key-for-e2e", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            // Act: Full golden path — prompt in, recommendations out
            var recs = await provider.GetRecommendationsAsync("Based on my library of jazz and rock, recommend 5 albums");

            // Assert
            recs.Should().NotBeNull();
            recs.Should().HaveCountGreaterOrEqualTo(1);
            recs[0].Artist.Should().Be("Radiohead");
            recs[0].Album.Should().Be("OK Computer");
        }

        [Fact]
        public async Task GoldenPath_Anthropic_PromptToRecommendations()
        {
            var responseJson = @"{
                ""id"": ""msg-test"",
                ""type"": ""message"",
                ""content"": [{
                    ""type"": ""text"",
                    ""text"": ""[{\""artist\"": \""Miles Davis\"", \""album\"": \""Kind of Blue\"", \""genre\"": \""Jazz\"", \""reason\"": \""Masterpiece\"", \""confidence\"": 0.98}]""
                }]
            }";

            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, responseJson));
            var provider = new AnthropicProvider(http, _logger, apiKey: "sk-ant-test-key", model: "claude-3-5-haiku-latest");

            var recs = await provider.GetRecommendationsAsync("Recommend jazz albums");

            recs.Should().NotBeNull();
            recs.Should().HaveCountGreaterOrEqualTo(1);
            recs[0].Artist.Should().Be("Miles Davis");
        }

        [Fact]
        public async Task GoldenPath_Gemini_PromptToRecommendations()
        {
            var responseJson = @"{
                ""candidates"": [{
                    ""content"": {
                        ""parts"": [{
                            ""text"": ""[{\""artist\"": \""Bjork\"", \""album\"": \""Homogenic\"", \""genre\"": \""Electronic\"", \""reason\"": \""Innovative\"", \""confidence\"": 0.9}]""
                        }]
                    }
                }]
            }";

            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, responseJson));
            var provider = new GeminiProvider(http, _logger, apiKey: "test-gemini-key", model: "gemini-1.5-flash", httpExec: _exec);

            var recs = await provider.GetRecommendationsAsync("Recommend electronic albums");

            recs.Should().NotBeNull();
            recs.Should().HaveCountGreaterOrEqualTo(1);
            recs[0].Artist.Should().Be("Bjork");
        }

        // ─── Auth-Fail Path: Invalid Key → Graceful Error ───────────────────

        [Fact]
        public async Task AuthFailPath_OpenAI_InvalidKey_GracefulError()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"error\":{\"message\":\"Incorrect API key provided\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}"));

            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-INVALID", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            // TestConnection should return false, not throw
            var connected = await provider.TestConnectionAsync();
            connected.Should().BeFalse();

            // User message should exist and NOT contain the raw API key
            var msg = provider.GetLastUserMessage();
            msg.Should().NotBeNullOrEmpty("should provide guidance");
            msg.Should().NotContain("sk-INVALID", "API key must not leak into user message");
        }

        [Fact]
        public async Task AuthFailPath_Anthropic_InvalidKey_GracefulError()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}"));

            var provider = new AnthropicProvider(http, _logger, apiKey: "sk-ant-INVALID", model: "claude-3-5-haiku-latest");

            var connected = await provider.TestConnectionAsync();
            connected.Should().BeFalse();

            var msg = provider.GetLastUserMessage();
            msg.Should().NotBeNullOrEmpty("should provide guidance");
            msg.Should().NotContain("sk-ant-INVALID", "API key must not leak");
        }

        [Fact]
        public async Task AuthFailPath_GetRecommendations_ReturnsEmptyNotThrows()
        {
            var http = new FakeHttpClient(req =>
                HttpResponseFactory.Error(req, HttpStatusCode.Unauthorized,
                    "{\"error\":{\"message\":\"Unauthorized\"}}"));

            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-BAD", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            // Getting recommendations with invalid auth should return empty, not throw
            var recs = await provider.GetRecommendationsAsync("test prompt");
            recs.Should().NotBeNull();
            recs.Should().BeEmpty("auth failure should degrade to empty, not crash");
        }

        // ─── Connection Test Golden Path ────────────────────────────────────

        [Fact]
        public async Task ConnectionTest_OpenAI_ValidKey_ReturnsTrue()
        {
            var responseJson = @"{
                ""id"": ""chatcmpl-test"",
                ""choices"": [{""message"": {""content"": ""OK""}}]
            }";
            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, responseJson));
            var provider = new OpenAIProvider(http, _logger, apiKey: "sk-valid-key", model: "gpt-4o-mini", preferStructured: false, httpExec: _exec);

            var result = await provider.TestConnectionAsync();

            result.Should().BeTrue();
        }

        [Fact]
        public async Task ConnectionTest_Anthropic_ValidKey_ReturnsTrue()
        {
            var responseJson = @"{
                ""id"": ""msg-test"",
                ""type"": ""message"",
                ""content"": [{""type"": ""text"", ""text"": ""OK""}]
            }";
            var http = new FakeHttpClient(req => HttpResponseFactory.Ok(req, responseJson));
            var provider = new AnthropicProvider(http, _logger, apiKey: "sk-ant-valid", model: "claude-3-5-haiku-latest");

            var result = await provider.TestConnectionAsync();

            result.Should().BeTrue();
        }
    }
}
