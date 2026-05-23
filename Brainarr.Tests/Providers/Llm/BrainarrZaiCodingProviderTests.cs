using System;
using System.Linq;
using System.Net;
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
    /// pipeline maps Z.AI's 429/1113 ("insufficient balance") response to QuotaExceeded
    /// rather than RateLimited.
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
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(SampleAnthropicResponse()));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            captured!.Url.ToString().Should().Be("https://api.z.ai/api/anthropic/v1/messages");
        }

        [Fact]
        public async Task CompleteAsync_SendsBearerAuth_NotXApiKey()
        {
            // Z.AI docs map ANTHROPIC_AUTH_TOKEN → Authorization: Bearer (not x-api-key).
            // Mirroring Claude Code's behavior keeps us inside the Coding Plan UA gate.
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "secret-xyz", "GLM_5_1");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(SampleAnthropicResponse()));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured!.Headers["Authorization"].ToString().Should().Be("Bearer secret-xyz");
            captured.Headers.Any(h => string.Equals(h.Key, "x-api-key", StringComparison.OrdinalIgnoreCase))
                .Should().BeFalse("Z.AI Coding endpoint authenticates via Authorization: Bearer, not x-api-key");
        }

        [Fact]
        public async Task CompleteAsync_SendsClaudeCodeIdentityHeaders()
        {
            // Z.AI's Coding-Plan UA gate admits requests that look like Claude Code / OpenCode.
            // If these headers regress, GLM-5.x access disappears with a misleading 4xx — pin them.
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(SampleAnthropicResponse()));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured!.Headers["User-Agent"].ToString().Should().StartWith("claude-cli/");
            captured.Headers["x-app"].ToString().Should().Be("cli");
            captured.Headers["anthropic-version"].ToString().Should().Be("2023-06-01");
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
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", canonical);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(SampleAnthropicResponse()));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain($"\"model\":\"{expectedRaw}\"");
        }

        [Fact]
        public async Task CompleteAsync_WithSystemPrompt_PutsSystemAtTopLevel_NotInMessages()
        {
            // Anthropic Messages format differs from OpenAI here — `system` is a top-level
            // field, NOT a {role:"system"} entry inside messages. Z.AI's Anthropic proxy
            // rejects the OpenAI shape with a 400; pin the wire shape.
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(SampleAnthropicResponse()));

            await provider.CompleteAsync(new LlmRequest
            {
                Prompt = "user msg",
                SystemPrompt = "you are a music recommender",
            });

            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"system\":\"you are a music recommender\"");
            body.Should().NotContain("\"role\":\"system\"",
                "Anthropic Messages API takes the system prompt as a top-level field, not a message entry");
            body.Should().Contain("\"role\":\"user\"");
            body.Should().Contain("user msg");
        }

        [Fact]
        public async Task CompleteAsync_ParsesAnthropicContentBlocks()
        {
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
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
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(responseJson));

            var result = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            result.Content.Should().Be("hello world");
            result.FinishReason.Should().Be("end_turn");
            result.Usage!.InputTokens.Should().Be(11);
            result.Usage.OutputTokens.Should().Be(22);
        }

        [Fact]
        public async Task CompleteAsync_RequestBody_AlwaysIncludesMaxTokens()
        {
            // Anthropic Messages API requires max_tokens — omitting it returns 400. Pin it.
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(SampleAnthropicResponse()));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"max_tokens\"");
        }

        [Fact]
        public void UpdateModel_NormalizesThroughModelIdMapper()
        {
            // Should accept both canonical (GLM_5_1) and already-raw (glm-5.1) forms.
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            provider.UpdateModel("glm-4.7-flash");
            // Indirect check: a subsequent request must serialize the raw id we set.
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(SampleAnthropicResponse()));

            provider.CompleteAsync(new LlmRequest { Prompt = "hi" }).GetAwaiter().GetResult();

            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"model\":\"glm-4.7-flash\"");
        }

        [Fact]
        public void StreamAsync_NotYetSupported()
        {
            // Same gap as BrainarrClaudeCodeSubscriptionProvider — Anthropic SSE not yet decoded by common.
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            provider.StreamAsync(new LlmRequest { Prompt = "hi" }).Should().BeNull();
        }

        [Fact]
        public async Task CompleteAsync_DefaultTemperature_Matches_ZaiGlm_At_0_7()
        {
            // Both Z.AI providers hit the same GLM models — temperature must match for
            // cache-hit parity and consistent output randomness across the two endpoints.
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(SampleAnthropicResponse()));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            var body = System.Text.Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"temperature\":0.7",
                "ZaiCoding and ZaiGlm must use the same default temperature (0.7) for cache/output parity");
        }

        [Fact]
        public void Capabilities_DoesNotAdvertiseToolCalling()
        {
            // ZaiCoding never serializes tools or parses tool-call responses. Advertising
            // ToolCalling causes downstream routing to attempt tool calls that silently fail.
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");

            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.ToolCalling).Should().BeFalse(
                "ToolCalling is not wired — advertising it misleads routing logic");
        }

        [Fact]
        public async Task CompleteAsync_UserAgent_CanBeOverriddenViaEnvVar()
        {
            // When Z.AI tightens their UA gate, users need a self-serve fix path without
            // waiting for a Brainarr release. The env var BRAINARR_ZAI_CODING_USER_AGENT
            // overrides the default claude-cli UA string.
            var provider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(SampleAnthropicResponse()));

            var original = System.Environment.GetEnvironmentVariable("BRAINARR_ZAI_CODING_USER_AGENT");
            try
            {
                System.Environment.SetEnvironmentVariable("BRAINARR_ZAI_CODING_USER_AGENT", "custom-agent/1.0");
                // Need a fresh provider to pick up env var (read at construction)
                var customProvider = new BrainarrZaiCodingProvider(_http.Object, _logger, "k", "GLM_5_1");
                await customProvider.CompleteAsync(new LlmRequest { Prompt = "hi" });

                captured!.Headers["User-Agent"].ToString().Should().Be("custom-agent/1.0");
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("BRAINARR_ZAI_CODING_USER_AGENT", original);
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
            // body is >600 chars — Truncate(500) would produce invalid JSON

            var ex = BrainarrZaiGlmProvider.MapZaiHttpError(429, body, retryAfter: null, inner: null);

            ex.ErrorCode.Should().Be(LlmErrorCode.QuotaExceeded,
                "the mapper must parse the full body before truncating for the inner exception message");
        }
    }
}
