using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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
    /// End-to-end streaming coverage for the IHttpClient → Stream bridge introduced in
    /// tech-debt wave 2 (<see cref="StreamingHttpExecutor"/>). Uses a fake
    /// <see cref="HttpMessageHandler"/> to emit SSE frames, so no network access is
    /// required.
    /// </summary>
    public class BrainarrStreamingTests
    {
        private readonly Logger _logger;
        private readonly Mock<IHttpClient> _http;

        public BrainarrStreamingTests()
        {
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
            _http = new Mock<IHttpClient>();
        }

        // ----- helpers ------------------------------------------------------

        private static StreamingHttpExecutor MakeExecutor(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
            => new(new FakeHandler(handler));

        private static StreamingHttpExecutor MakeExecutor(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => new(new FakeHandler(handler));

        private static HttpResponseMessage SseResponse(IEnumerable<string> frames)
        {
            // Each frame is already a complete SSE event including the trailing blank line.
            var bytes = Encoding.UTF8.GetBytes(string.Concat(frames));
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        private static IEnumerable<string> OpenAiFrames(params string[] deltas)
        {
            foreach (var d in deltas)
            {
                var json = "{\"choices\":[{\"delta\":{\"content\":\"" + d.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}}]}";
                yield return $"data: {json}\n\n";
            }
            yield return "data: [DONE]\n\n";
        }

        // ----- OpenAI -------------------------------------------------------

        [Fact]
        public async Task OpenAi_Streaming_HappyPath_YieldsContentDeltas()
        {
            var executor = MakeExecutor((req, _) =>
            {
                req.Method.Should().Be(HttpMethod.Post);
                req.RequestUri!.AbsoluteUri.Should().Be("https://api.openai.com/v1/chat/completions");
                req.Headers.Authorization!.Scheme.Should().Be("Bearer");
                return SseResponse(OpenAiFrames("Hello", " ", "world"));
            });
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini", executor);

            var deltas = new List<string>();
            await foreach (var c in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!)
            {
                if (c.ContentDelta != null) deltas.Add(c.ContentDelta);
            }

            string.Concat(deltas).Should().Be("Hello world");
        }

        [Fact]
        public async Task OpenAi_Streaming_HttpError_ThrowsLlmProviderException()
        {
            var executor = MakeExecutor((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"bad key\"}"),
            });
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini", executor);

            Func<Task> act = async () =>
            {
                await foreach (var _ in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!) { }
            };

            await act.Should().ThrowAsync<LlmProviderException>()
                .Where(e => e.ProviderId == "openai");
        }

        [Fact]
        public async Task OpenAi_Streaming_429_PropagatesRateLimitWithRetryAfter()
        {
            var executor = MakeExecutor((_, _) =>
            {
                var resp = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("rate limited"),
                };
                resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return resp;
            });
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini", executor);

            Func<Task> act = async () =>
            {
                await foreach (var _ in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!) { }
            };

            var ex = await act.Should().ThrowAsync<RateLimitException>();
            ex.Which.RetryAfter.Should().NotBeNull();
            ex.Which.RetryAfter!.Value.TotalSeconds.Should().BeApproximately(7, 1);
        }

        [Fact]
        public async Task OpenAi_Streaming_PreCancelledToken_ThrowsImmediately()
        {
            // A cancellation token that is already cancelled when StreamAsync starts must
            // propagate without producing chunks. This guarantees the bridge does not
            // swallow cancellation requests.
            var executor = MakeExecutor((_, _) => SseResponse(OpenAiFrames("ignored")));
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini", executor);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            int seen = 0;
            Func<Task> act = async () =>
            {
                await foreach (var c in provider.StreamAsync(new LlmRequest { Prompt = "hi" }, cts.Token)!)
                {
                    seen++;
                }
            };

            await act.Should().ThrowAsync<OperationCanceledException>();
            seen.Should().Be(0);
        }

        [Fact]
        public async Task OpenAi_Streaming_MalformedJsonFrame_IsSkipped()
        {
            // The OpenAI decoder defensively skips frames that don't deserialize. The
            // streaming pipeline must therefore complete without throwing on a malformed
            // frame mid-stream.
            var frames = new[]
            {
                "data: {\"choices\":[{\"delta\":{\"content\":\"good\"}}]}\n\n",
                "data: {not-json}\n\n",
                "data: {\"choices\":[{\"delta\":{\"content\":\"more\"}}]}\n\n",
                "data: [DONE]\n\n",
            };
            var executor = MakeExecutor((_, _) => SseResponse(frames));
            var provider = new BrainarrOpenAiProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini", executor);

            var deltas = new List<string>();
            await foreach (var c in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!)
            {
                if (c.ContentDelta != null) deltas.Add(c.ContentDelta);
            }

            string.Concat(deltas).Should().Be("goodmore");
        }

        // ----- Anthropic ----------------------------------------------------

        [Fact]
        public async Task Anthropic_Streaming_HappyPath_YieldsContentDeltasAndUsage()
        {
            var frames = new[]
            {
                "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"m1\",\"usage\":{\"input_tokens\":10,\"output_tokens\":0}}}\n\n",
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}\n\n",
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\" world\"}}\n\n",
                "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":5}}\n\n",
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n",
            };
            var executor = MakeExecutor((req, _) =>
            {
                req.RequestUri!.AbsoluteUri.Should().Be("https://api.anthropic.com/v1/messages");
                req.Headers.GetValues("x-api-key").Single().Should().Be("sk-ant-test");
                return SseResponse(frames);
            });

            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest", executor);

            var deltas = new List<string>();
            LlmStreamChunk? terminal = null;
            await foreach (var c in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!)
            {
                if (c.ContentDelta != null) deltas.Add(c.ContentDelta);
                if (c.IsComplete) terminal = c;
            }

            string.Concat(deltas).Should().Be("Hello world");
            terminal.Should().NotBeNull();
            terminal!.FinalUsage.Should().NotBeNull();
            terminal.FinalUsage!.InputTokens.Should().Be(10);
            terminal.FinalUsage!.OutputTokens.Should().Be(5);
        }

        [Fact]
        public async Task Anthropic_Streaming_5xxError_PropagatesProviderException()
        {
            var executor = MakeExecutor((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("overloaded"),
            });
            var provider = new BrainarrAnthropicProvider(_http.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest", executor);

            Func<Task> act = async () =>
            {
                await foreach (var _ in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!) { }
            };

            var ex = await act.Should().ThrowAsync<LlmProviderException>();
            ex.Which.ProviderId.Should().Be("anthropic");
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ProviderOverloaded);
        }

        // ----- OpenRouter ---------------------------------------------------

        [Fact]
        public async Task OpenRouter_Streaming_PassesAttributionHeaders()
        {
            HttpRequestMessage? captured = null;
            var executor = MakeExecutor((req, _) =>
            {
                captured = req;
                return SseResponse(OpenAiFrames("hello"));
            });
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "openai/gpt-4o-mini", executor);

            await foreach (var _ in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!) { }

            captured.Should().NotBeNull();
            captured!.Headers.Contains("HTTP-Referer").Should().BeTrue();
            captured.Headers.Contains("X-Title").Should().BeTrue();
            captured.RequestUri!.AbsoluteUri.Should().Be("https://openrouter.ai/api/v1/chat/completions");
        }

        // ----- DeepSeek / Groq / Perplexity smoke ---------------------------

        [Fact]
        public async Task DeepSeek_Streaming_HappyPath()
        {
            var executor = MakeExecutor((req, _) =>
            {
                req.RequestUri!.AbsoluteUri.Should().Be("https://api.deepseek.com/chat/completions");
                return SseResponse(OpenAiFrames("ds"));
            });
            var provider = new BrainarrDeepSeekProvider(_http.Object, _logger, "sk-ds-test", "deepseek-chat", executor);

            var deltas = new List<string>();
            await foreach (var c in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!)
            {
                if (c.ContentDelta != null) deltas.Add(c.ContentDelta);
            }
            string.Concat(deltas).Should().Be("ds");
        }

        [Fact]
        public async Task Groq_Streaming_HappyPath()
        {
            var executor = MakeExecutor((req, _) =>
            {
                req.RequestUri!.AbsoluteUri.Should().Be("https://api.groq.com/openai/v1/chat/completions");
                return SseResponse(OpenAiFrames("gq"));
            });
            var provider = new BrainarrGroqProvider(_http.Object, _logger, "gsk_test", "llama-3.3-70b-versatile", executor);

            var deltas = new List<string>();
            await foreach (var c in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!)
            {
                if (c.ContentDelta != null) deltas.Add(c.ContentDelta);
            }
            string.Concat(deltas).Should().Be("gq");
        }

        [Fact]
        public async Task Perplexity_Streaming_StripsCitationMarkers()
        {
            // Perplexity's StreamAsyncCore strips inline [N] markers defensively before
            // surfacing chunks, matching the non-streaming CompleteAsync behavior.
            var frames = new[]
            {
                "data: {\"choices\":[{\"delta\":{\"content\":\"hello [1] world\"}}]}\n\n",
                "data: [DONE]\n\n",
            };
            var executor = MakeExecutor((req, _) =>
            {
                req.RequestUri!.AbsoluteUri.Should().Be("https://api.perplexity.ai/chat/completions");
                return SseResponse(frames);
            });
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "Sonar_Pro", executor);

            var deltas = new List<string>();
            await foreach (var c in provider.StreamAsync(new LlmRequest { Prompt = "hi" })!)
            {
                if (c.ContentDelta != null) deltas.Add(c.ContentDelta);
            }

            string.Concat(deltas).Should().Be("hello  world");
        }

        // ----- handler ------------------------------------------------------

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> sync)
                : this((req, ct) => Task.FromResult(sync(req, ct)))
            {
            }

            public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);
        }
    }
}
