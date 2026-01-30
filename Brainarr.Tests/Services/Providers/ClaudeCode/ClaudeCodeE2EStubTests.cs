using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Providers.ClaudeCode;
using Lidarr.Plugin.Common.Subprocess;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.ClaudeCode;
using Xunit;

namespace Brainarr.Tests.Services.Providers.ClaudeCode
{
    /// <summary>
    /// PR-hermetic E2E stub tests for Claude Code provider.
    /// Proves the full recommendation flow works with mocked CLI - no real CLI or credentials required.
    /// This is the final gate before Claude Code provider can ship.
    /// </summary>
    [Trait("Category", "E2E")]
    [Trait("Provider", "ClaudeCode")]
    [Collection("ClaudeCodeE2E")]
    public class ClaudeCodeE2EStubTests : IDisposable
    {
        private readonly Mock<ICliRunner> _mockCliRunner;
        private readonly NLog.Logger _logger;
        private readonly string _tempCliPath;

        public ClaudeCodeE2EStubTests()
        {
            _mockCliRunner = new Mock<ICliRunner>();
            _logger = Helpers.TestLogger.CreateNullLogger();
            _tempCliPath = Path.GetTempFileName();
            ClaudeCodeCapabilities.InvalidateCache();
        }

        public void Dispose()
        {
            ClaudeCodeCapabilities.InvalidateCache();
            if (File.Exists(_tempCliPath))
            {
                File.Delete(_tempCliPath);
            }
        }

        #region End-to-End Buffered Flow Tests

        [Fact]
        public async Task E2E_BufferedFlow_ProducesValidRecommendations()
        {
            // Arrange: Full mocked CLI setup with realistic recommendation response
            SetupCapabilities("--output-format json --append-system-prompt --model");

            var recommendations = new[]
            {
                new { artist = "Radiohead", album = "OK Computer", genre = "Alternative Rock", confidence = 0.95, reason = "Innovative and influential" },
                new { artist = "Pink Floyd", album = "The Dark Side of the Moon", genre = "Progressive Rock", confidence = 0.92, reason = "Concept album masterpiece" },
                new { artist = "Daft Punk", album = "Discovery", genre = "Electronic", confidence = 0.88, reason = "Genre-defining electronic album" }
            };

            var recommendationsJson = JsonSerializer.Serialize(recommendations);
            SetupSuccessfulCliResponse(recommendationsJson);

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath, "sonnet");

            // Act: Full recommendation flow
            var results = await adapter.GetRecommendationsAsync("Recommend albums similar to my library with Pink Floyd and electronic music");

            // Assert: Proves end-to-end parsing works
            results.Should().NotBeEmpty();
            results.Should().HaveCount(3);

            // Verify all recommendations have required fields
            results.Should().AllSatisfy(r =>
            {
                r.Artist.Should().NotBeNullOrEmpty();
                r.Album.Should().NotBeNullOrEmpty();
            });

            // Verify specific recommendations parsed correctly
            results[0].Artist.Should().Be("Radiohead");
            results[0].Album.Should().Be("OK Computer");
            results[1].Artist.Should().Be("Pink Floyd");
            results[2].Artist.Should().Be("Daft Punk");

            // No errors should have occurred
            adapter.GetLastUserMessage().Should().BeNull();
        }

        [Fact]
        public async Task E2E_BufferedFlow_ArtistOnlyRecommendations()
        {
            // Arrange: Artist-only mode (no album field)
            SetupCapabilities("--output-format json --append-system-prompt --model");

            var recommendations = new[]
            {
                new { artist = "Massive Attack", genre = "Trip Hop", confidence = 0.94, reason = "Pioneered the genre" },
                new { artist = "Portishead", genre = "Trip Hop", confidence = 0.91, reason = "Atmospheric and dark" },
                new { artist = "Tricky", genre = "Trip Hop", confidence = 0.85, reason = "Experimental approach" }
            };

            var recommendationsJson = JsonSerializer.Serialize(recommendations);
            SetupSuccessfulCliResponse(recommendationsJson);

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act: Use artist-only trigger phrase
            var results = await adapter.GetRecommendationsAsync("Recommend artists similar to Bristol sound");

            // Assert
            results.Should().HaveCount(3);
            results.Should().AllSatisfy(r =>
            {
                r.Artist.Should().NotBeNullOrEmpty();
                // Album may or may not be present in artist-only mode
            });

            results[0].Artist.Should().Be("Massive Attack");
        }

        [Fact]
        public async Task E2E_BufferedFlow_HandlesEmptyRecommendations()
        {
            // Arrange: CLI returns empty array (valid but no recommendations)
            SetupCapabilities("--output-format json --model");
            SetupSuccessfulCliResponse("[]");

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            var results = await adapter.GetRecommendationsAsync("Recommend something very obscure");

            // Assert: Should gracefully handle empty results
            results.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().BeNull(); // No error for valid empty response
        }

        #endregion

        #region End-to-End Streaming Flow Tests

        [Fact]
        public async Task E2E_StreamingFlow_ProducesValidRecommendations()
        {
            // Arrange: Enable streaming capabilities
            SetupCapabilities("--output-format stream-json --append-system-prompt --model");

            var recommendations = new[]
            {
                new { artist = "Boards of Canada", album = "Music Has the Right to Children", genre = "IDM", confidence = 0.93, reason = "Nostalgic electronic" },
                new { artist = "Aphex Twin", album = "Selected Ambient Works 85-92", genre = "Ambient Techno", confidence = 0.89, reason = "Pioneering electronic" }
            };

            var recommendationsJson = JsonSerializer.Serialize(recommendations);
            SetupStreamingCliResponse(recommendationsJson);

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act: Full streaming flow
            var results = await adapter.GetRecommendationsAsync("Recommend ambient electronic albums");

            // Assert
            results.Should().HaveCount(2);
            results[0].Artist.Should().Be("Boards of Canada");
            results[1].Artist.Should().Be("Aphex Twin");
        }

        [Fact]
        public async Task E2E_StreamingFlow_WithThinkingBlocks_ProducesRecommendations()
        {
            // Arrange: Streaming with extended thinking (thinking_delta events)
            SetupCapabilities("--output-format stream-json --model");

            var recommendations = new[]
            {
                new { artist = "Sigur Ros", album = "()", genre = "Post-Rock", confidence = 0.91, reason = "Atmospheric and unique" }
            };

            var recommendationsJson = JsonSerializer.Serialize(recommendations);
            SetupStreamingWithThinkingResponse(recommendationsJson);

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            var results = await adapter.GetRecommendationsAsync("Recommend post-rock albums");

            // Assert: Should still produce results despite thinking blocks
            results.Should().HaveCount(1);
            results[0].Artist.Should().Be("Sigur Ros");
        }

        [Fact]
        public async Task E2E_StreamingFlow_WithUnknownEventTypes_StillProducesRecommendations()
        {
            // Arrange: Future-proofing test - CLI might emit new event types we don't recognize
            // This tests "tooling drift" resilience
            SetupCapabilities("--output-format stream-json --model");

            var recommendations = new[]
            {
                new { artist = "Godspeed You! Black Emperor", album = "Lift Your Skinny Fists", genre = "Post-Rock", confidence = 0.90, reason = "Epic compositions" }
            };

            var recommendationsJson = JsonSerializer.Serialize(recommendations);
            SetupStreamingWithUnknownEventsResponse(recommendationsJson);

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act: Should handle unknown event types gracefully
            var results = await adapter.GetRecommendationsAsync("Recommend post-rock albums");

            // Assert: Should still extract recommendations despite unknown event types
            results.Should().HaveCount(1);
            results[0].Artist.Should().Be("Godspeed You! Black Emperor");

            // No error message should be set - unknown events should be silently skipped
            adapter.GetLastUserMessage().Should().BeNull();
        }

        #endregion

        #region End-to-End Error Recovery Tests

        [Fact]
        public async Task E2E_AuthError_ReturnsEmptyWithUserMessage()
        {
            // Arrange: Authentication failure
            SetupCapabilities("--output-format json --model");
            SetupCliErrorResponse(1, "Error: Not authenticated. Run 'claude login' to sign in.");

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            var results = await adapter.GetRecommendationsAsync("test prompt");

            // Assert
            results.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("Not authenticated");
            adapter.GetLastUserMessage().Should().Contain("claude login");
            adapter.GetLearnMoreUrl().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task E2E_RateLimitError_ReturnsEmptyWithRetryHint()
        {
            // Arrange: Rate limit error
            SetupCapabilities("--output-format json --model");
            SetupCliErrorResponse(1, "Error: rate limit exceeded, please wait 60 seconds");

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            var results = await adapter.GetRecommendationsAsync("test prompt");

            // Assert
            results.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("Rate limited");
        }

        [Fact]
        public async Task E2E_MalformedJson_ReturnsEmptyGracefully()
        {
            // Arrange: CLI returns malformed JSON
            SetupCapabilities("--output-format json --model");
            SetupSuccessfulCliResponse("not valid json at all {[}");

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            var results = await adapter.GetRecommendationsAsync("test prompt");

            // Assert: Should handle gracefully without crashing
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task E2E_ProcessTimeout_ReturnsEmptyWithMessage()
        {
            // Arrange: CLI times out
            SetupCapabilities("--output-format json --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("Process timed out"));

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            var results = await adapter.GetRecommendationsAsync("test prompt");

            // Assert
            results.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("timed out");
        }

        #endregion

        #region Test Connection E2E Tests

        [Fact]
        public async Task E2E_TestConnection_FullSuccessFlow()
        {
            // Arrange: Full successful test connection flow
            SetupVersionCheck("1.0.47");
            SetupCapabilities("--output-format json --model");
            SetupSuccessfulCliResponse("OK");

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            var result = await adapter.TestConnectionAsync();

            // Assert
            result.Should().BeTrue();
            adapter.GetLastUserMessage().Should().BeNull();
        }

        [Fact]
        public async Task E2E_TestConnection_FailsOnVersionCheckError()
        {
            // Arrange: Version check fails
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 1,
                    StandardOutput = "",
                    StandardError = "Unknown command",
                    Duration = TimeSpan.FromMilliseconds(100)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            var result = await adapter.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            adapter.GetLastUserMessage().Should().Contain("version check failed");
        }

        #endregion

        #region Helper Methods

        private void SetupCapabilities(string helpText)
        {
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    It.IsAny<string>(),
                    It.Is<IReadOnlyList<string>>(a => a.Contains("--help")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 0,
                    StandardOutput = helpText,
                    StandardError = "",
                    Duration = TimeSpan.FromMilliseconds(50)
                });
        }

        private void SetupVersionCheck(string version)
        {
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    It.IsAny<string>(),
                    It.Is<IReadOnlyList<string>>(a => a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 0,
                    StandardOutput = $"claude-cli {version}",
                    StandardError = "",
                    Duration = TimeSpan.FromMilliseconds(50)
                });
        }

        private void SetupSuccessfulCliResponse(string resultContent)
        {
            var responseJson = BuildCliResponseJson(resultContent);

            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p") && !a.Contains("--help") && !a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 0,
                    StandardOutput = responseJson,
                    StandardError = "",
                    Duration = TimeSpan.FromSeconds(2)
                });
        }

        private void SetupCliErrorResponse(int exitCode, string stderr)
        {
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p") && !a.Contains("--help") && !a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = exitCode,
                    StandardOutput = "",
                    StandardError = stderr,
                    Duration = TimeSpan.FromMilliseconds(500)
                });
        }

        private void SetupStreamingCliResponse(string content)
        {
            _mockCliRunner
                .Setup(r => r.StreamAsync(
                    _tempCliPath,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(CreateStreamingResponse(content));
        }

        private void SetupStreamingWithThinkingResponse(string content)
        {
            _mockCliRunner
                .Setup(r => r.StreamAsync(
                    _tempCliPath,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(CreateStreamingWithThinkingResponse(content));
        }

        private void SetupStreamingWithUnknownEventsResponse(string content)
        {
            _mockCliRunner
                .Setup(r => r.StreamAsync(
                    _tempCliPath,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(CreateStreamingWithUnknownEventsResponse(content));
        }

        private static string BuildCliResponseJson(string resultContent)
        {
            return JsonSerializer.Serialize(new
            {
                type = "result",
                result = resultContent,
                session_id = "e2e-test-session",
                is_error = false,
                num_turns = 1,
                duration_ms = 2500,
                total_cost_usd = 0.005m,
                usage = new { input_tokens = 500, output_tokens = 200 }
            });
        }

        private static async IAsyncEnumerable<CliStreamEvent> CreateStreamingResponse(
            string content,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new CliStreamEvent.Started(12345);
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""message_start"",""message"":{""id"":""msg_e2e""}}");
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_start"",""index"":0}");

            // Split content into chunks for realistic streaming
            var escaped = content.Replace("\"", "\\\"").Replace("\n", "\\n");
            var chunkSize = 50;
            for (int i = 0; i < escaped.Length; i += chunkSize)
            {
                var chunk = escaped.Substring(i, Math.Min(chunkSize, escaped.Length - i));
                yield return new CliStreamEvent.StandardOutput($@"{{""type"":""content_block_delta"",""delta"":{{""type"":""text_delta"",""text"":""{chunk}""}}}}");
                await Task.Yield();
            }

            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_stop"",""index"":0}");
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""message_stop""}");
            yield return new CliStreamEvent.Exited(0, TimeSpan.FromSeconds(2));
        }

        private static async IAsyncEnumerable<CliStreamEvent> CreateStreamingWithThinkingResponse(
            string content,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new CliStreamEvent.Started(12345);
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""message_start"",""message"":{""id"":""msg_e2e_thinking""}}");

            // Emit thinking block first
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_start"",""index"":0,""content_block"":{""type"":""thinking""}}");
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_delta"",""index"":0,""delta"":{""type"":""thinking_delta"",""thinking"":""Let me think about this...""}}");
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_delta"",""index"":0,""delta"":{""type"":""thinking_delta"",""thinking"":"" Considering the request for post-rock albums.""}}");
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_stop"",""index"":0}");

            await Task.Yield();

            // Then emit actual content
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_start"",""index"":1,""content_block"":{""type"":""text""}}");
            var escaped = content.Replace("\"", "\\\"");
            yield return new CliStreamEvent.StandardOutput($@"{{""type"":""content_block_delta"",""index"":1,""delta"":{{""type"":""text_delta"",""text"":""{escaped}""}}}}");
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_stop"",""index"":1}");

            yield return new CliStreamEvent.StandardOutput(@"{""type"":""message_stop""}");
            yield return new CliStreamEvent.Exited(0, TimeSpan.FromSeconds(3));
        }

        private static async IAsyncEnumerable<CliStreamEvent> CreateStreamingWithUnknownEventsResponse(
            string content,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Simulate future CLI versions emitting unknown event types
            // This tests tooling drift resilience
            yield return new CliStreamEvent.Started(12345);
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""message_start"",""message"":{""id"":""msg_e2e_unknown""}}");

            // Unknown event type that future CLI versions might emit
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""telemetry_ping"",""data"":{""latency_ms"":42}}");

            // Another unknown event type
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""model_info"",""model_version"":""2026.1"",""capabilities"":[""streaming"",""vision""]}");

            await Task.Yield();

            // Standard content block with the actual recommendation
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_start"",""index"":0,""content_block"":{""type"":""text""}}");
            var escaped = content.Replace("\"", "\\\"");
            yield return new CliStreamEvent.StandardOutput($@"{{""type"":""content_block_delta"",""index"":0,""delta"":{{""type"":""text_delta"",""text"":""{escaped}""}}}}");

            // Unknown delta type
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_delta"",""index"":0,""delta"":{""type"":""citation_delta"",""source"":""https://example.com""}}");

            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_stop"",""index"":0}");

            // Unknown event between content blocks
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""usage_update"",""input_tokens"":100,""output_tokens"":50}");

            yield return new CliStreamEvent.StandardOutput(@"{""type"":""message_stop""}");
            yield return new CliStreamEvent.Exited(0, TimeSpan.FromSeconds(2));
        }

        #endregion
    }
}
