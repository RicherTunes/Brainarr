using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.Streaming.Decoders;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Unit coverage for <see cref="BrainarrAnthropicProvider"/>.
    /// </summary>
    public class BrainarrAnthropicProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrAnthropicProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");

            provider.ProviderId.Should().Be("anthropic");
            provider.DisplayName.Should().Be("Anthropic");
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.ExtendedThinking).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            // Phase 5b: Streaming flag now set — common ships AnthropicStreamDecoder.
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.Streaming).Should().BeTrue();
            provider.Capabilities.MaxContextTokens.Should().Be(200_000);
        }

        [Fact]
        public async Task CompleteAsync_UsesXApiKeyAndVersion_AgainstMessagesEndpoint()
        {
            // Contract guard (COV1, provider-matrix #5): pin Anthropic's auth shape on the
            // actually-exercised CompleteAsync path. x-api-key is CORRECT for sk-ant- API keys —
            // and the OAuth-token providers must NOT regress into x-api-key, so this also asserts
            // NO Authorization header leaks onto the API-key provider.
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-xyz", "claude-3-5-haiku-latest");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"content\":[{\"type\":\"text\",\"text\":\"[]\"}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            captured!.Headers.GetSingleValue("x-api-key").Should().Be("sk-ant-xyz");
            captured.Headers.GetSingleValue("anthropic-version").Should().Be("2023-06-01");
            captured.Headers.GetSingleValue("Authorization").Should().BeNull(
                "the API-key provider authenticates via x-api-key, never Authorization: Bearer");
            captured.Url.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrAnthropicProvider(null!, _logger, "sk-ant-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_EmptyApiKey_Throws()
        {
            Action act = () => new BrainarrAnthropicProvider(_http.Object, _logger, "");
            act.Should().Throw<ArgumentException>().WithParameterName("apiKey");
        }

        [Fact]
        public void Constructor_ThinkingSentinel_ParsesBudget()
        {
            // The legacy AnthropicProvider parsed the `#thinking(tokens=N)` sentinel; the new
            // ILlmProvider provider must preserve that contract for the registry path.
            var provider = new BrainarrAnthropicProvider(
                _http.Object, _logger, "sk-ant-test", "claude-3-5-sonnet-latest#thinking(tokens=8000)");

            // Indirectly verified via successful construction; no public surface to inspect.
            provider.ProviderId.Should().Be("anthropic");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContent()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            var apiObj = new
            {
                id = "msg_1",
                content = new[]
                {
                    new { type = "text", text = "{\"recommendations\":[]}" },
                },
                stop_reason = "end_turn",
                usage = new { input_tokens = 10, output_tokens = 4 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hello" });

            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("end_turn");
            response.Usage.Should().NotBeNull();
            response.Usage!.InputTokens.Should().Be(10);
            response.Usage!.OutputTokens.Should().Be(4);
        }

        [Fact]
        public async Task CompleteAsync_WithThinking_CarriesReasoningContent()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-sonnet-latest#thinking");
            var apiObj = new
            {
                content = new object[]
                {
                    new { type = "thinking", thinking = "considering options" },
                    new { type = "text", text = "[]" },
                },
                stop_reason = "end_turn",
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be("[]");
            response.ReasoningContent.Should().Be("considering options");
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("anthropic");
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<RateLimitException>();
        }

        [Fact]
        public async Task CompleteAsync_503_ThrowsProviderOverloaded()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.ServiceUnavailable));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ProviderOverloaded);
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            var apiObj = new
            {
                content = new[] { new { type = "text", text = "OK" } },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("anthropic");
        }

        [Fact]
        public void StreamAsync_NowReturnsNonNullEnumerable()
        {
            // Tech debt wave 2: StreamingHttpExecutor bridges IHttpClient → System.IO.Stream;
            // common's AnthropicStreamDecoder is wired. End-to-end SSE coverage lives in
            // BrainarrStreamingTests against a fake HttpMessageHandler.
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            var stream = provider.StreamAsync(new LlmRequest { Prompt = "hello" });
            stream.Should().NotBeNull();
        }

        // ---------------------------------------------------------------------
        // Phase 5b: AnthropicStreamDecoder integration tests
        //
        // These tests exercise common's AnthropicStreamDecoder against the SSE wire format
        // that BrainarrAnthropicProvider will emit once the host IHttpClient → Stream bridge
        // lands. They verify the decoder is correctly bundled with the Anthropic provider
        // path and handles all event types Anthropic's Messages API emits.
        // ---------------------------------------------------------------------

        [Fact]
        public async Task AnthropicStreamDecoder_HappyPath_YieldsContentDeltasAndTerminalUsage()
        {
            // Canonical Anthropic SSE sequence: message_start (input usage) →
            // content_block_delta(text_delta)+ → message_delta (final output usage) →
            // message_stop. Decoder must surface text deltas as ContentDelta and a single
            // terminal IsComplete chunk with merged FinalUsage.
            var sse = "event: message_start\n"
                + "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_brn1\",\"usage\":{\"input_tokens\":42}}}\n"
                + "\n"
                + "event: content_block_delta\n"
                + "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"{\\\"recommendations\\\":[\"}}\n"
                + "\n"
                + "event: content_block_delta\n"
                + "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"]}\"}}\n"
                + "\n"
                + "event: message_delta\n"
                + "data: {\"type\":\"message_delta\",\"usage\":{\"output_tokens\":17}}\n"
                + "\n"
                + "event: message_stop\n"
                + "data: {\"type\":\"message_stop\"}\n"
                + "\n";

            var chunks = await DecodeAsync(sse);

            chunks.Length.Should().Be(3);
            chunks[0].ContentDelta.Should().Be("{\"recommendations\":[");
            chunks[0].IsComplete.Should().BeFalse();
            chunks[1].ContentDelta.Should().Be("]}");
            chunks[2].IsComplete.Should().BeTrue();
            chunks[2].FinalUsage.Should().NotBeNull();
            chunks[2].FinalUsage!.InputTokens.Should().Be(42);
            chunks[2].FinalUsage!.OutputTokens.Should().Be(17);
        }

        [Fact]
        public async Task AnthropicStreamDecoder_ThinkingDelta_RoutesToReasoningDelta()
        {
            // Extended-thinking models emit thinking_delta events that surface as
            // ReasoningDelta (not ContentDelta). This is critical for brainarr because
            // BrainarrAnthropicProvider supports the #thinking sentinel — streaming must
            // preserve the same content/reasoning split as CompleteAsync's parser does.
            var sse = "event: message_start\n"
                + "data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":5}}}\n"
                + "\n"
                + "event: content_block_delta\n"
                + "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"weighing options\"}}\n"
                + "\n"
                + "event: content_block_delta\n"
                + "data: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"text_delta\",\"text\":\"final\"}}\n"
                + "\n"
                + "event: message_stop\n"
                + "data: {\"type\":\"message_stop\"}\n"
                + "\n";

            var chunks = await DecodeAsync(sse);

            chunks.Length.Should().Be(3);
            chunks[0].ReasoningDelta.Should().Be("weighing options");
            chunks[0].ContentDelta.Should().BeNull();
            chunks[1].ContentDelta.Should().Be("final");
            chunks[1].ReasoningDelta.Should().BeNull();
            chunks[2].IsComplete.Should().BeTrue();
        }

        [Fact]
        public async Task AnthropicStreamDecoder_MessageDelta_UpdatesFinalOutputTokens()
        {
            // message_delta carries final output_tokens (and may overwrite input_tokens).
            // Verify the decoder folds message_start.input_tokens with message_delta.output_tokens
            // into a single LlmUsage on the terminal chunk.
            var sse = "event: message_start\n"
                + "data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":100,\"output_tokens\":0}}}\n"
                + "\n"
                + "event: content_block_delta\n"
                + "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"x\"}}\n"
                + "\n"
                + "event: message_delta\n"
                + "data: {\"type\":\"message_delta\",\"usage\":{\"output_tokens\":250}}\n"
                + "\n"
                + "event: message_stop\n"
                + "data: {\"type\":\"message_stop\"}\n"
                + "\n";

            var chunks = await DecodeAsync(sse);

            var terminal = chunks.Last();
            terminal.IsComplete.Should().BeTrue();
            terminal.FinalUsage.Should().NotBeNull();
            terminal.FinalUsage!.InputTokens.Should().Be(100);
            terminal.FinalUsage!.OutputTokens.Should().Be(250);
        }

        [Fact]
        public async Task AnthropicStreamDecoder_MessageStop_TerminatesEnumeration()
        {
            // message_stop must terminate the iterator deterministically — even if more
            // bytes follow on the wire (e.g. a half-flushed comment frame), the decoder
            // must not yield additional chunks.
            var sse = "event: content_block_delta\n"
                + "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"a\"}}\n"
                + "\n"
                + "event: message_stop\n"
                + "data: {\"type\":\"message_stop\"}\n"
                + "\n"
                + "event: content_block_delta\n"
                + "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"after-stop\"}}\n"
                + "\n";

            var chunks = await DecodeAsync(sse);

            chunks.Length.Should().Be(2);
            chunks[0].ContentDelta.Should().Be("a");
            chunks[1].IsComplete.Should().BeTrue();
            // No "after-stop" content delta should be present.
            chunks.Should().NotContain(c => c.ContentDelta == "after-stop");
        }

        [Fact]
        public async Task AnthropicStreamDecoder_StreamWithoutMessageStop_StillEmitsTerminalChunk()
        {
            // Half-open streams (network truncation, server crash): decoder must emit a
            // synthetic terminal chunk so brainarr's caller doesn't have to special-case
            // the missing-message_stop scenario.
            var sse = "event: content_block_delta\n"
                + "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"partial\"}}\n"
                + "\n";

            var chunks = await DecodeAsync(sse);

            chunks.Length.Should().Be(2);
            chunks[0].ContentDelta.Should().Be("partial");
            chunks[1].IsComplete.Should().BeTrue();
        }

        [Fact]
        public async Task AnthropicStreamDecoder_MalformedJsonFrame_IsSkippedNotFatal()
        {
            // Robustness: a single malformed data frame (e.g. truncated mid-write) should
            // be skipped rather than throwing — Anthropic occasionally emits ping or comment
            // frames that aren't strict JSON.
            var sse = "event: content_block_delta\n"
                + "data: not-json\n"
                + "\n"
                + "event: content_block_delta\n"
                + "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"ok\"}}\n"
                + "\n"
                + "event: message_stop\n"
                + "data: {\"type\":\"message_stop\"}\n"
                + "\n";

            var chunks = await DecodeAsync(sse);

            chunks.Length.Should().Be(2);
            chunks[0].ContentDelta.Should().Be("ok");
            chunks[1].IsComplete.Should().BeTrue();
        }

        [Fact]
        public void AnthropicStreamDecoder_IsAvailableForAnthropicProvider()
        {
            // Smoke test: the AnthropicStreamDecoder advertised by common identifies itself
            // for the "anthropic" provider id (matching BrainarrAnthropicProvider.ProviderId).
            // This is the gate by which DecoderRegistry will route streams to the correct
            // decoder once the host bridge is wired.
            var decoder = new AnthropicStreamDecoder();
            decoder.DecoderId.Should().Be("anthropic");
            decoder.SupportedProviderIds.Should().Contain("anthropic");
            decoder.CanDecodeForProvider("anthropic", "text/event-stream").Should().BeTrue();
        }

        private static async Task<LlmStreamChunk[]> DecodeAsync(string sse)
        {
            var decoder = new AnthropicStreamDecoder();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
            var list = new List<LlmStreamChunk>();
            await foreach (var chunk in decoder.DecodeAsync(stream))
            {
                list.Add(chunk);
            }

            return list.ToArray();
        }
    }
}
