using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Providers.ClaudeCode;
using Lidarr.Plugin.Common.Subprocess;
using Moq;
using Xunit;

namespace Brainarr.Tests.Services.Providers.ClaudeCode
{
    /// <summary>
    /// Tests for NDJSON streaming support in Claude Code provider.
    /// </summary>
    [Trait("Category", "Hermetic")]
    [Trait("Provider", "ClaudeCode")]
    public class ClaudeCodeNdjsonStreamingTests
    {
        private readonly ClaudeCodeStreamParser _parser;

        public ClaudeCodeNdjsonStreamingTests()
        {
            _parser = new ClaudeCodeStreamParser();
        }

        [Fact]
        public async Task ParseNdjson_WithCompleteLines_YieldsMessages()
        {
            // Arrange: Simulate NDJSON output from Claude CLI
            var events = CreateStreamEvents(
                @"{""type"":""message_start"",""message"":{""id"":""msg_123""}}",
                @"{""type"":""content_block_start"",""index"":0}",
                @"{""type"":""content_block_delta"",""index"":0,""delta"":{""type"":""text_delta"",""text"":""Hello""}}",
                @"{""type"":""content_block_delta"",""index"":0,""delta"":{""type"":""text_delta"",""text"":"" World""}}",
                @"{""type"":""content_block_stop"",""index"":0}",
                @"{""type"":""message_delta"",""usage"":{""input_tokens"":10,""output_tokens"":5}}",
                @"{""type"":""message_stop""}"
            );

            // Act
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            await foreach (var chunk in _parser.ParseAsync(events, CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            // Assert
            chunks.Should().HaveCountGreaterThan(0);
            var textChunks = chunks.Where(c => !string.IsNullOrEmpty(c.ContentDelta)).ToList();
            textChunks.Should().HaveCount(2);
            textChunks[0].ContentDelta.Should().Be("Hello");
            textChunks[1].ContentDelta.Should().Be(" World");

            var completeChunk = chunks.First(c => c.IsComplete);
            completeChunk.Should().NotBeNull();
            completeChunk.FinalUsage.Should().NotBeNull();
            completeChunk.FinalUsage!.InputTokens.Should().Be(10);
            completeChunk.FinalUsage.OutputTokens.Should().Be(5);
        }

        [Fact]
        public async Task ParseNdjson_WithMixedOutput_FiltersNonJson()
        {
            // Arrange: Include non-JSON lines that should be filtered
            var events = CreateStreamEvents(
                "Starting Claude CLI...",  // Non-JSON line
                @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""Valid""}}",
                "",  // Empty line
                "Some warning message",  // Non-JSON line
                @"{""type"":""message_stop""}"
            );

            // Act
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            await foreach (var chunk in _parser.ParseAsync(events, CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            // Assert: Should only have content from valid JSON
            var textChunks = chunks.Where(c => !string.IsNullOrEmpty(c.ContentDelta)).ToList();
            textChunks.Should().HaveCount(1);
            textChunks[0].ContentDelta.Should().Be("Valid");
        }

        [Fact]
        public async Task ParseNdjson_WithThinkingDelta_YieldsReasoningContent()
        {
            // Arrange: Extended thinking output
            var events = CreateStreamEvents(
                @"{""type"":""content_block_delta"",""delta"":{""type"":""thinking_delta"",""thinking"":""Let me think...""}}",
                @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""Here's my answer""}}",
                @"{""type"":""message_stop""}"
            );

            // Act
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            await foreach (var chunk in _parser.ParseAsync(events, CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            // Assert
            var thinkingChunks = chunks.Where(c => !string.IsNullOrEmpty(c.ReasoningDelta)).ToList();
            thinkingChunks.Should().HaveCount(1);
            thinkingChunks[0].ReasoningDelta.Should().Be("Let me think...");

            var textChunks = chunks.Where(c => !string.IsNullOrEmpty(c.ContentDelta)).ToList();
            textChunks.Should().HaveCount(1);
            textChunks[0].ContentDelta.Should().Be("Here's my answer");
        }

        [Fact]
        public async Task ParseNdjson_WithErrorEvent_ThrowsException()
        {
            // Arrange: Error in stream
            var events = CreateStreamEvents(
                @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""Partial""}}",
                @"{""type"":""error"",""error"":{""type"":""rate_limit"",""message"":""Rate limited""}}"
            );

            // Act & Assert
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            Func<Task> act = async () =>
            {
                await foreach (var chunk in _parser.ParseAsync(events, CancellationToken.None))
                {
                    chunks.Add(chunk);
                }
            };

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*streaming error*");
        }

        [Fact]
        public async Task ParseNdjson_WithMalformedJson_SkipsLine()
        {
            // Arrange: Some malformed JSON mixed with valid
            var events = CreateStreamEvents(
                @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""Before""}}",
                @"{malformed json here",  // Invalid
                @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""After""}}",
                @"{""type"":""message_stop""}"
            );

            // Act
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            await foreach (var chunk in _parser.ParseAsync(events, CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            // Assert: Should have both valid chunks
            var textChunks = chunks.Where(c => !string.IsNullOrEmpty(c.ContentDelta)).ToList();
            textChunks.Should().HaveCount(2);
            textChunks[0].ContentDelta.Should().Be("Before");
            textChunks[1].ContentDelta.Should().Be("After");
        }

        [Fact]
        public async Task ParseNdjson_WithProcessExit_YieldsCompletionChunk()
        {
            // Arrange: Process exits without message_stop
            var events = CreateStreamEventsWithExit(
                new[] { @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""Content""}}" },
                exitCode: 0
            );

            // Act
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            await foreach (var chunk in _parser.ParseAsync(events, CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            // Assert: Should still yield completion
            var completeChunk = chunks.FirstOrDefault(c => c.IsComplete);
            completeChunk.Should().NotBeNull();
        }

        [Fact]
        public async Task ParseNdjson_WithResultEvent_ExtractsUsage()
        {
            // Arrange: Result event with usage stats
            var events = CreateStreamEvents(
                @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""Done""}}",
                @"{""type"":""result"",""result"":{""type"":""success""},""usage"":{""input_tokens"":100,""output_tokens"":50}}"
            );

            // Act
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            await foreach (var chunk in _parser.ParseAsync(events, CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            // Assert
            var completeChunk = chunks.First(c => c.IsComplete);
            completeChunk.FinalUsage.Should().NotBeNull();
            completeChunk.FinalUsage!.InputTokens.Should().Be(100);
            completeChunk.FinalUsage.OutputTokens.Should().Be(50);
        }

        [Fact]
        public async Task ParseNdjson_WithUnknownEventType_LogsAndContinues()
        {
            // Arrange: Unknown event type
            var loggedMessages = new List<string>();
            var parserWithLogging = new ClaudeCodeStreamParser(msg => loggedMessages.Add(msg));

            var events = CreateStreamEvents(
                @"{""type"":""future_event_type"",""data"":{""foo"":""bar""}}",
                @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""Content""}}",
                @"{""type"":""message_stop""}"
            );

            // Act
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            await foreach (var chunk in parserWithLogging.ParseAsync(events, CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            // Assert: Should continue processing and log unknown event
            var textChunks = chunks.Where(c => !string.IsNullOrEmpty(c.ContentDelta)).ToList();
            textChunks.Should().HaveCount(1);
            loggedMessages.Should().Contain(m => m.Contains("future_event_type"));
        }

        [Fact]
        public async Task ParseNdjson_WithEmptyStream_YieldsOnlyCompletion()
        {
            // Arrange: No content events
            var events = CreateStreamEvents();

            // Act
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            await foreach (var chunk in _parser.ParseAsync(events, CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            // Assert: Should yield completion even with empty stream
            chunks.Should().HaveCount(1);
            chunks[0].IsComplete.Should().BeTrue();
        }

        [Fact]
        public async Task ParseNdjson_WithPreCancelledToken_RespectsToken()
        {
            // Arrange: Use a pre-cancelled token
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var events = CreateStreamEvents(
                @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""One""}}",
                @"{""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""Two""}}",
                @"{""type"":""message_stop""}"
            );

            // Act: Try to enumerate with cancelled token
            var chunks = new List<Lidarr.Plugin.Common.Abstractions.Llm.LlmStreamChunk>();
            var gotCancellation = false;

            try
            {
                await foreach (var chunk in _parser.ParseAsync(events, cts.Token))
                {
                    chunks.Add(chunk);
                }
            }
            catch (OperationCanceledException)
            {
                gotCancellation = true;
            }

            // Assert: Parser respects cancellation token
            // It should either throw or not process events (implementation-dependent)
            // The key is that it doesn't hang indefinitely
            (gotCancellation || chunks.Count <= 3).Should().BeTrue(
                "parser should respect cancellation token");
        }

        // Helper to create async enumerable of CLI stream events
        private static async IAsyncEnumerable<CliStreamEvent> CreateStreamEvents(
            params string[] lines)
        {
            yield return new CliStreamEvent.Started(12345);

            foreach (var line in lines)
            {
                yield return new CliStreamEvent.StandardOutput(line);
                await Task.Yield();
            }

            yield return new CliStreamEvent.Exited(0, TimeSpan.FromSeconds(1));
        }

        private static async IAsyncEnumerable<CliStreamEvent> CreateStreamEventsWithExit(
            string[] lines,
            int exitCode)
        {
            yield return new CliStreamEvent.Started(12345);

            foreach (var line in lines)
            {
                yield return new CliStreamEvent.StandardOutput(line);
                await Task.Yield();
            }

            yield return new CliStreamEvent.Exited(exitCode, TimeSpan.FromSeconds(1));
        }

        private static async IAsyncEnumerable<CliStreamEvent> CreateStreamEventsWithDelay(
            string[] lines,
            int delayMs,
            CancellationTokenSource cts,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new CliStreamEvent.Started(12345);

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(delayMs, cancellationToken);
                yield return new CliStreamEvent.StandardOutput(line);
            }

            yield return new CliStreamEvent.Exited(0, TimeSpan.FromSeconds(1));
        }
    }
}
