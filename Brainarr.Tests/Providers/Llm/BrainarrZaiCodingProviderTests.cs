using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Unit coverage for <see cref="BrainarrZaiCodingProvider"/>. Asserts the wire shape matches
    /// what Claude Code / OpenCode send (Anthropic Messages format, Bearer auth, Claude-CLI
    /// User-Agent + x-app: cli identification headers) and that the shared MapZaiHttpError
    /// pipeline maps Z.AI's 429/1113 ("insufficient balance") response to QuotaExceeded.
    ///
    /// <para>
    /// The provider dispatches via a RAW <see cref="System.Net.Http.HttpClient"/> rather than
    /// Lidarr's <c>IHttpClient</c>: Z.AI's Coding-Plan gate requires a custom User-Agent, and
    /// Lidarr's IHttpClient throws "User-Agent other than Lidarr not allowed." on any UA override
    /// (which previously crash-blocked the connection Test and made the provider unsavable). These
    /// tests inject a fake <see cref="HttpMessageHandler"/> via the internal test-seam constructor,
    /// so they verify the headers/body that actually reach the socket.
    /// </para>
    /// </summary>
    public class BrainarrZaiCodingProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrZaiCodingProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        /// <summary>Fake handler capturing the outbound request (raw-client path).</summary>
        private sealed class CapturingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _responseBody;

            public Uri? Uri { get; private set; }
            public string Body { get; private set; } = string.Empty;
            public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

            public CapturingHandler(HttpStatusCode status, string responseBody)
            {
                _status = status;
                _responseBody = responseBody;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Uri = request.RequestUri;
                foreach (var h in request.Headers)
                {
                    Headers[h.Key] = string.Join(",", h.Value);
                }
                if (request.Content != null)
                {
                    Body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                return new HttpResponseMessage(_status) { Content = new StringContent(_responseBody) };
            }
        }

        private BrainarrZaiCodingProvider CreateProvider(CapturingHandler handler, string apiKey = "k", string? model = "GLM_5_1")
            => new BrainarrZaiCodingProvider(_http.Object, _logger, apiKey, model, authCircuit: null, testHandler: handler);

        private static CapturingHandler OkHandler() =>
            new(HttpStatusCode.OK, SampleAnthropicResponse());

        [Fact]
        public void Identity_MatchesExpectedProviderMetadata()
        {
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            provider.ProviderId.Should().Be("zaicoding");
            provider.DisplayName.Should().Be("Z.AI Coding Subscription");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeFalse(
                "Coding endpoint speaks Anthropic Messages format, not OpenAI Chat Completions");
            provider.Capabilities.MaxContextTokens.Should().Be(200_000);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_RejectsEmptyApiKey(string? key)
        {
            Action act = () => new BrainarrZaiCodingProvider(_http.Object, _logger, key!, "GLM_5_1");
            act.Should().Throw<ArgumentException>().WithMessage("*Coding Plan API key*");
        }

        [Fact]
        public async Task CompleteAsync_HitsAnthropicCompatibleEndpoint()
        {
            var handler = OkHandler();
            var provider = CreateProvider(handler);

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.Uri.Should().NotBeNull();
            handler.Uri!.ToString().Should().Be("https://api.z.ai/api/anthropic/v1/messages");
        }

        [Fact]
        public async Task CompleteAsync_SendsBearerAuth_NotXApiKey()
        {
            // Z.AI docs map ANTHROPIC_AUTH_TOKEN → Authorization: Bearer (not x-api-key).
            var handler = OkHandler();
            var provider = CreateProvider(handler, apiKey: "secret-xyz");

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.Headers["Authorization"].Should().Be("Bearer secret-xyz");
            handler.Headers.ContainsKey("x-api-key").Should().BeFalse(
                "Z.AI Coding endpoint authenticates via Authorization: Bearer, not x-api-key");
        }

        [Fact]
        public async Task CompleteAsync_SendsClaudeCodeIdentityHeaders_OverTheWire()
        {
            // The fix's core guarantee: the Claude-Code UA gate headers reach the socket.
            // Lidarr's IHttpClient would have thrown "User-Agent other than Lidarr not allowed."
            // If these regress, GLM-5.x access disappears with a misleading 4xx — pin them.
            var handler = OkHandler();
            var provider = CreateProvider(handler);

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.Headers["User-Agent"].Should().StartWith("claude-cli/");
            handler.Headers["x-app"].Should().Be("cli");
            handler.Headers["anthropic-version"].Should().Be("2023-06-01");
        }

        [Theory]
        [InlineData("GLM_5_1", "glm-5.1")]
        [InlineData("GLM_5", "glm-5")]
        [InlineData("GLM_5_Turbo", "glm-5-turbo")]
        [InlineData("GLM_4_7", "glm-4.7")]
        [InlineData("GLM_4_7_Flash", "glm-4.7-flash")]
        [InlineData("GLM_4_7_FlashX", "glm-4.7-flashx")]
        [InlineData("GLM_4_6", "glm-4.6")]
        [InlineData("GLM_4_5", "glm-4.5")]
        [InlineData("GLM_4_5_Air", "glm-4.5-air")]
        [InlineData("GLM_4_5_AirX", "glm-4.5-airx")]
        [InlineData("GLM_4_5_Flash", "glm-4.5-flash")]
        [InlineData("GLM_4_Plus", "glm-4-plus")]
        [InlineData("GLM_4_32B", "glm-4-32b-0414-128k")]
        public async Task CompleteAsync_MapsAllCatalogModelsToRawIds(string canonical, string expectedRaw)
        {
            var handler = OkHandler();
            var provider = CreateProvider(handler, model: canonical);

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.Body.Should().Contain($"\"model\":\"{expectedRaw}\"");
        }

        [Fact]
        public async Task CompleteAsync_WithSystemPrompt_PutsSystemAtTopLevel_NotInMessages()
        {
            var handler = OkHandler();
            var provider = CreateProvider(handler);

            await provider.CompleteAsync(new LlmRequest
            {
                Prompt = "user msg",
                SystemPrompt = "you are a music recommender",
            });

            handler.Body.Should().Contain("\"system\":\"you are a music recommender\"");
            handler.Body.Should().NotContain("\"role\":\"system\"",
                "Anthropic Messages API takes the system prompt as a top-level field, not a message entry");
            handler.Body.Should().Contain("\"role\":\"user\"");
            handler.Body.Should().Contain("user msg");
        }

        [Fact]
        public async Task CompleteAsync_ParsesAnthropicContentBlocks()
        {
            const string responseJson = """
            {
              "id": "msg_abc",
              "content": [
                { "type": "text", "text": "hello world" }
              ],
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 11, "output_tokens": 22 }
            }
            """;
            var handler = new CapturingHandler(HttpStatusCode.OK, responseJson);
            var provider = CreateProvider(handler);

            var result = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            result.Content.Should().Be("hello world");
            result.FinishReason.Should().Be("end_turn");
            result.Usage!.InputTokens.Should().Be(11);
            result.Usage.OutputTokens.Should().Be(22);
        }

        [Fact]
        public async Task CompleteAsync_RequestBody_AlwaysIncludesMaxTokens()
        {
            var handler = OkHandler();
            var provider = CreateProvider(handler);

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.Body.Should().Contain("\"max_tokens\"");
        }

        [Fact]
        public async Task CompleteAsync_NonOk_MapsThroughZaiErrorPipeline()
        {
            // 429 + Z.AI billing code 1113 must surface as QuotaExceeded (not RateLimited), and
            // the raw-client path must route non-2xx through the same MapZaiHttpError pipeline.
            var body = "{\"error\":{\"code\":\"1113\",\"message\":\"Insufficient balance. Please recharge.\"}}";
            var handler = new CapturingHandler((HttpStatusCode)429, body);
            var provider = CreateProvider(handler);

            var act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            var ex = await act.Should().ThrowAsync<LlmProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.QuotaExceeded);
        }

        [Fact]
        public async Task UpdateModel_NormalizesThroughModelIdMapper()
        {
            var handler = OkHandler();
            var provider = CreateProvider(handler);
            provider.UpdateModel("glm-4.7-flash");

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            handler.Body.Should().Contain("\"model\":\"glm-4.7-flash\"");
        }

        [Fact]
        public void StreamAsync_NotYetSupported()
        {
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            provider.StreamAsync(new LlmRequest { Prompt = "hi" }).Should().BeNull();
        }

        [Fact]
        public async Task CompleteAsync_OmitsTemperature()
        {
            // Z.AI's Coding-Plan (Anthropic-format) endpoint rejects requests that carry `temperature`
            // with [1210][Invalid API parameter] — Claude Code (which this endpoint emulates) never
            // sends it. Regression guard for the live-confirmed fix: the body must NOT include it.
            var handler = OkHandler();
            var provider = CreateProvider(handler);

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi", Temperature = 0.7f });

            handler.Body.Should().NotContain("temperature",
                "sending temperature to the Z.AI Coding endpoint returns [1210][Invalid API parameter]");
        }

        [Fact]
        public void Capabilities_DoesNotAdvertiseToolCalling()
        {
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");

            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.ToolCalling).Should().BeFalse(
                "ToolCalling is not wired — advertising it misleads routing logic");
        }

        [Fact]
        public async Task CheckHealthAsync_Healthy_When200()
        {
            var handler = OkHandler();
            var provider = CreateProvider(handler);

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            handler.Headers["User-Agent"].Should().StartWith("claude-cli/");
        }

        [Fact]
        public async Task CompleteAsync_UserAgent_CanBeOverriddenViaEnvVar()
        {
            // When Z.AI tightens their UA gate, users need a self-serve fix path. The env var
            // BRAINARR_ZAI_CODING_USER_AGENT overrides the default claude-cli UA string.
            var original = Environment.GetEnvironmentVariable("BRAINARR_ZAI_CODING_USER_AGENT");
            try
            {
                Environment.SetEnvironmentVariable("BRAINARR_ZAI_CODING_USER_AGENT", "custom-agent/1.0");
                var handler = OkHandler();
                // Construct AFTER setting the env var (UA is read at construction).
                var provider = CreateProvider(handler);

                await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

                handler.Headers["User-Agent"].Should().Be("custom-agent/1.0");
            }
            finally
            {
                Environment.SetEnvironmentVariable("BRAINARR_ZAI_CODING_USER_AGENT", original);
            }
        }

        private static string SampleAnthropicResponse() =>
            "{\"id\":\"msg_x\",\"content\":[{\"type\":\"text\",\"text\":\"OK\"}],\"stop_reason\":\"end_turn\"}";
    }

    /// <summary>
    /// Coverage for the shared <see cref="BrainarrZaiGlmProvider.MapZaiHttpError"/> path.
    /// Used by both Z.AI providers — when Z.AI returns HTTP 429 with body error code 1113
    /// ("Insufficient balance or no resource package"), we want QuotaExceeded so the user
    /// sees a "top up your account" hint instead of a misleading "wait and retry" hint.
    /// </summary>
    public class ZaiHttpErrorMappingTests
    {
        [Fact]
        public void Code1113_MapsToQuotaExceeded_NotRateLimited()
        {
            var body = "{\"error\":{\"code\":\"1113\",\"message\":\"Insufficient balance or no resource package. Please recharge.\"}}";

            var ex = BrainarrZaiGlmProvider.MapZaiHttpError(429, body, retryAfter: null, inner: null);

            ex.ErrorCode.Should().Be(LlmErrorCode.QuotaExceeded);
            ex.Message.Should().Contain("balance");
        }

        [Fact]
        public void Code1115_AlsoMapsToQuotaExceeded()
        {
            // 1115 = "no available channels" — also account-state, treat as quota.
            var body = "{\"error\":{\"code\":\"1115\",\"message\":\"No available channels.\"}}";

            var ex = BrainarrZaiGlmProvider.MapZaiHttpError(429, body, retryAfter: null, inner: null);

            ex.ErrorCode.Should().Be(LlmErrorCode.QuotaExceeded);
        }

        [Fact]
        public void Code429_WithoutBillingCode_StaysRateLimited()
        {
            // Genuine rate limit body (no Z.AI billing code) should fall through to the
            // default RateLimited mapping. Don't over-broaden the quota mapping.
            var body = "{\"error\":{\"code\":\"1301\",\"message\":\"Rate limit exceeded\"}}";

            var ex = BrainarrZaiGlmProvider.MapZaiHttpError(429, body, retryAfter: null, inner: null);

            ex.ErrorCode.Should().Be(LlmErrorCode.RateLimited);
        }

        [Fact]
        public void Code429_WithEmptyBody_FallsBackToRateLimited()
        {
            var ex = BrainarrZaiGlmProvider.MapZaiHttpError(429, null, retryAfter: null, inner: null);
            ex.ErrorCode.Should().Be(LlmErrorCode.RateLimited);
        }

        [Fact]
        public void Code429_WithGarbledBody_DoesNotThrow()
        {
            // Defensive: malformed JSON in the body shouldn't crash the mapper.
            var ex = BrainarrZaiGlmProvider.MapZaiHttpError(429, "not valid json {", retryAfter: null, inner: null);
            ex.ErrorCode.Should().Be(LlmErrorCode.RateLimited);
        }

        [Fact]
        public void Code401_AlwaysAuthenticationFailed_RegardlessOfBody()
        {
            // 1113 in body but 401 status — still an auth failure. Only 429+1113 means quota.
            var body = "{\"error\":{\"code\":\"1113\",\"message\":\"...\"}}";

            var ex = BrainarrZaiGlmProvider.MapZaiHttpError(401, body, retryAfter: null, inner: null);

            ex.ErrorCode.Should().Be(LlmErrorCode.AuthenticationFailed);
        }

        [Fact]
        public void NumericCode_IsHandled_NotOnlyStringCode()
        {
            // Future-proof: Z.AI currently returns code as JSON string but may switch to number.
            var body = "{\"error\":{\"code\":1113,\"message\":\"Insufficient balance\"}}";

            var ex = BrainarrZaiGlmProvider.MapZaiHttpError(429, body, retryAfter: null, inner: null);

            ex.ErrorCode.Should().Be(LlmErrorCode.QuotaExceeded);
        }

        [Fact]
        public void Code1113_InLongBody_StillDetected_EvenIfTruncationWouldBreakJson()
        {
            // Simulate a body where the 1113 error is valid JSON but surrounded by enough
            // padding that a naive Truncate(500) would chop the closing brace and break
            // JsonConvert.DeserializeObject. The mapper must parse the FULL body, not
            // a pre-truncated snippet.
            var padding = new string('x', 600);
            var body = "{\"error\":{\"code\":\"1113\",\"message\":\"" + padding + "\"}}";

            var ex = BrainarrZaiGlmProvider.MapZaiHttpError(429, body, retryAfter: null, inner: null);

            ex.ErrorCode.Should().Be(LlmErrorCode.QuotaExceeded,
                "the mapper must parse the full body before truncating for the inner exception message");
        }
    }
}
