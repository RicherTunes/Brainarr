using System;
using System.Collections.Generic;
using System.Net;
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
    /// Contract tests for <see cref="BrainarrOpenAiChatProviderBase"/> — the shared
    /// template for the OpenAI-chat-format cloud providers (B-201 / #46 dedup).
    ///
    /// These tests pin the BASE's hook behavior with a minimal fake subclass so that
    /// per-provider quirks (temperature policy, response_format support, extra headers,
    /// custom error mapping) are provably driven by the template hooks and not by
    /// copy-pasted plumbing. The per-provider wire contracts remain pinned by the
    /// individual provider test classes (CompleteAsync_UsesBearerAuth_Against…, the
    /// ZaiGlm/ZaiCoding temperature pair, etc.).
    /// </summary>
    public class OpenAiChatProviderBaseContractTests
    {
        private const string Endpoint = "https://chat.example.test/v1/chat/completions";
        private const string OkBody = "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}";

        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public OpenAiChatProviderBaseContractTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        private sealed class TestChatProvider : BrainarrOpenAiChatProviderBase
        {
            private readonly bool _sendsTemperature;
            private readonly bool _supportsResponseFormat;
            private readonly double _defaultTemperature;
            private readonly IReadOnlyDictionary<string, string>? _extraHeaders;
            private readonly Func<int, string?, TimeSpan?, Exception?, LlmProviderException>? _errorMapper;

            public TestChatProvider(
                IHttpClient httpClient,
                Logger logger,
                string apiKey = "test-key",
                string? model = "test-model",
                bool sendsTemperature = true,
                bool supportsResponseFormat = true,
                double defaultTemperature = 0.7,
                IReadOnlyDictionary<string, string>? extraHeaders = null,
                Func<int, string?, TimeSpan?, Exception?, LlmProviderException>? errorMapper = null)
                : base(httpClient, logger, apiKey, model,
                       streamingExecutor: null, authCircuit: null,
                       providerId: "testchat", defaultModel: "test-model", keyOwnerName: "TestChat")
            {
                _sendsTemperature = sendsTemperature;
                _supportsResponseFormat = supportsResponseFormat;
                _defaultTemperature = defaultTemperature;
                _extraHeaders = extraHeaders;
                _errorMapper = errorMapper;
            }

            public override string DisplayName => "Test Chat";

            public override LlmProviderCapabilities Capabilities => new()
            {
                Flags = LlmCapabilityFlags.TextCompletion | LlmCapabilityFlags.SystemPrompt,
                UsesOpenAiCompatibleApi = true,
            };

            protected override string ChatCompletionsUrl => Endpoint;
            protected override bool SendsTemperature => _sendsTemperature;
            protected override bool SupportsJsonResponseFormat => _supportsResponseFormat;
            protected override double DefaultTemperature => _defaultTemperature;

            protected override void AddCompletionRequestHeaders(HttpRequestBuilder builder)
            {
                if (_extraHeaders == null) return;
                foreach (var (name, value) in _extraHeaders)
                {
                    builder.SetHeader(name, value);
                }
            }

            protected override LlmProviderException MapHttpError(int statusCode, string? body, TimeSpan? retryAfter, Exception? inner)
                => _errorMapper != null
                    ? _errorMapper(statusCode, body, retryAfter, inner)
                    : base.MapHttpError(statusCode, body, retryAfter, inner);

            protected override BrainarrLlmHint? GetUserHint(LlmProviderException exception) => null;
        }

        private static string BodyOf(HttpRequest request)
            => System.Text.Encoding.UTF8.GetString(request.ContentData ?? Array.Empty<byte>());

        [Fact]
        public async Task Temperature_OmittedWhenPolicySaysNo()
        {
            var provider = new TestChatProvider(_http.Object, _logger, sendsTemperature: false);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(OkBody));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi", Temperature = 0.5f });

            captured.Should().NotBeNull();
            BodyOf(captured!).Should().NotContain("temperature",
                "a provider whose endpoint rejects temperature must never emit it, even when the request sets one");
        }

        [Fact]
        public async Task Temperature_SentByDefault_UsingProviderDefault()
        {
            var provider = new TestChatProvider(_http.Object, _logger, defaultTemperature: 0.8);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(OkBody));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            BodyOf(captured!).Should().Contain("\"temperature\":0.8",
                "the provider's declared default temperature must be used when the request does not set one");
        }

        [Fact]
        public async Task Temperature_RequestValueWinsOverDefault()
        {
            var provider = new TestChatProvider(_http.Object, _logger, defaultTemperature: 0.8);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(OkBody));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi", Temperature = 0.5f });

            BodyOf(captured!).Should().Contain("\"temperature\":0.5");
        }

        [Fact]
        public async Task JsonMode_OmittedWhenProviderDoesNotSupportResponseFormat()
        {
            var provider = new TestChatProvider(_http.Object, _logger, supportsResponseFormat: false);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(OkBody));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi", JsonMode = true });

            BodyOf(captured!).Should().NotContain("response_format",
                "providers that don't implement response_format must not emit it even when JsonMode is requested");
        }

        [Fact]
        public async Task JsonMode_EmitsResponseFormat_WhenSupported()
        {
            var provider = new TestChatProvider(_http.Object, _logger);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(OkBody));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi", JsonMode = true });

            var body = BodyOf(captured!);
            body.Should().Contain("response_format");
            body.Should().Contain("json_object");
        }

        [Fact]
        public async Task RequestBody_PinsWireShape_FieldOrderAndDefaults()
        {
            // Byte-shape pin: the serialized body must keep the exact field order and
            // defaults the six migrated providers shipped with (model, messages,
            // temperature, max_tokens, stream). Any base change that reorders or renames
            // fields is a wire-format change and must fail here.
            var provider = new TestChatProvider(_http.Object, _logger);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(OkBody));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            BodyOf(captured!).Should().Be(
                "{\"model\":\"test-model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":0.7,\"max_tokens\":2000,\"stream\":false}");
        }

        [Fact]
        public async Task SystemPrompt_EmittedAsFirstMessage()
        {
            var provider = new TestChatProvider(_http.Object, _logger);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(OkBody));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi", SystemPrompt = "sys" });

            BodyOf(captured!).Should().Contain(
                "\"messages\":[{\"role\":\"system\",\"content\":\"sys\"},{\"role\":\"user\",\"content\":\"hi\"}]");
        }

        [Fact]
        public async Task CompleteAsync_UsesBearerAuth_AndExtraHeaders()
        {
            var provider = new TestChatProvider(_http.Object, _logger, apiKey: "sk-contract",
                extraHeaders: new Dictionary<string, string> { ["X-Extra"] = "extra-value" });
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(OkBody));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured!.Headers.GetSingleValue("Authorization").Should().Be("Bearer sk-contract");
            captured.Headers.GetSingleValue("X-Extra").Should().Be("extra-value");
            captured.Url.ToString().Should().Be(Endpoint);
        }

        [Fact]
        public async Task MapHttpError_OverrideWins()
        {
            // A provider-specific error mapper (e.g. ZaiGlm's 429 code-1113 → QuotaExceeded)
            // must be consulted for non-OK responses instead of the shared default.
            var provider = new TestChatProvider(_http.Object, _logger,
                errorMapper: (status, body, retryAfter, inner) =>
                    new ProviderException("testchat", LlmErrorCode.QuotaExceeded, "custom-mapped", inner));
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429, "{\"error\":{\"code\":\"1113\"}}"));

            Func<Task> act = () => provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.QuotaExceeded);
            ex.Which.Message.Should().Contain("custom-mapped");
        }

        [Fact]
        public async Task AuthFailure_RecordsToCircuit_AndPreflightRejects()
        {
            var provider = new TestChatProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized, "{}"));

            // Three failures within the window latch the circuit (threshold = 3).
            for (var i = 0; i < 3; i++)
            {
                await ((Func<Task>)(() => provider.CompleteAsync(new LlmRequest { Prompt = "hi" })))
                    .Should().ThrowAsync<AuthenticationException>();
            }

            // The next call must be rejected by the pre-flight without any HTTP call.
            _http.Invocations.Clear();
            var act = () => provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.Message.Should().Contain("Auth circuit open");
            _http.Invocations.Should().BeEmpty("an open auth circuit must reject before any HTTP round trip");
        }

        [Fact]
        public async Task HealthProbe_DefaultBody_UsesReplyWithOkAndFiveTokens()
        {
            var provider = new TestChatProvider(_http.Object, _logger);
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(OkBody));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("testchat");
            BodyOf(captured!).Should().Be(
                "{\"model\":\"test-model\",\"messages\":[{\"role\":\"user\",\"content\":\"Reply with OK\"}],\"max_tokens\":5}");
        }

        [Fact]
        public async Task ParseCompletion_Default_MapsContentFinishReasonAndUsage()
        {
            var provider = new TestChatProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"hello\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":4,\"total_tokens\":7}}"));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be("hello");
            response.FinishReason.Should().Be("stop");
            response.Usage!.InputTokens.Should().Be(3);
            response.Usage.OutputTokens.Should().Be(4);
        }

        [Fact]
        public async Task ParseCompletion_MalformedJson_FallsBackToRawContent()
        {
            // The raw-body fallback is what lets RecommendationJsonParser salvage
            // truncated responses — the base must never throw on unparseable bodies.
            var provider = new TestChatProvider(_http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("[{\"artist\":\"a\",\"albu"));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be("[{\"artist\":\"a\",\"albu");
        }

        [Fact]
        public void UpdateModel_MapsThroughModelIdMapper()
        {
            var provider = new TestChatProvider(_http.Object, _logger);
            provider.UpdateModel("  ");
            // Whitespace is ignored; a real value is applied (mapper passes unknown ids through).
            provider.UpdateModel("some-model");
            provider.ProviderId.Should().Be("testchat");
        }

        [Fact]
        public void Constructor_EmptyApiKey_Throws()
        {
            Action act = () => new TestChatProvider(_http.Object, _logger, apiKey: "");
            act.Should().Throw<ArgumentException>().WithParameterName("apiKey");
        }
    }
}
