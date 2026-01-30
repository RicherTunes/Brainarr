using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    /// Hermetic subprocess tests for ClaudeCodeCliAdapter.
    /// Uses mocked ICliRunner to verify CLI execution behavior without requiring actual CLI installation.
    /// </summary>
    [Trait("Category", "Hermetic")]
    [Trait("Provider", "ClaudeCode")]
    [Collection("ClaudeCodeCliAdapter")]  // Prevent parallel execution due to static capabilities cache
    public class ClaudeCodeCliAdapterTests : IDisposable
    {
        private readonly Mock<ICliRunner> _mockCliRunner;
        private readonly NLog.Logger _logger;
        private readonly string _tempCliPath;

        public ClaudeCodeCliAdapterTests()
        {
            _mockCliRunner = new Mock<ICliRunner>();
            _logger = Helpers.TestLogger.CreateNullLogger();

            // Create a temp file to act as the "CLI" for tests that need file existence checks
            _tempCliPath = System.IO.Path.GetTempFileName();

            // Invalidate capability cache to ensure test isolation
            ClaudeCodeCapabilities.InvalidateCache();
        }

        public void Dispose()
        {
            ClaudeCodeCapabilities.InvalidateCache();

            // Clean up temp file
            if (System.IO.File.Exists(_tempCliPath))
            {
                System.IO.File.Delete(_tempCliPath);
            }
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidParameters_Succeeds()
        {
            // Act
            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger);

            // Assert
            adapter.ProviderName.Should().Be("Claude Code CLI");
        }

        [Fact]
        public void Constructor_WithNullCliRunner_ThrowsArgumentNullException()
        {
            // Act
            var action = () => new ClaudeCodeCliAdapter(null!, _logger);

            // Assert
            action.Should().Throw<ArgumentNullException>().WithParameterName("cliRunner");
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act
            var action = () => new ClaudeCodeCliAdapter(_mockCliRunner.Object, null!);

            // Assert
            action.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void Constructor_WithExplicitCliPath_UsesPath()
        {
            // Act
            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, "/custom/path/claude", "opus");

            // Assert
            adapter.Should().NotBeNull();
        }

        #endregion

        #region Execute_WithMockedCli_ReturnsOutput Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithMockedCli_ReturnsRecommendations()
        {
            // Arrange
            SetupCapabilities("--output-format --append-system-prompt --model");
            SetupSuccessfulRecommendationResponse(@"[
                {""artist"": ""Radiohead"", ""album"": ""OK Computer"", ""genre"": ""Rock"", ""confidence"": 0.95, ""reason"": ""Classic""}
            ]");

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums like Pink Floyd");

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Radiohead");
            result[0].Album.Should().Be("OK Computer");
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithArtistOnlyPrompt_ReturnsArtistRecommendations()
        {
            // Arrange
            SetupCapabilities("--output-format --append-system-prompt --model");
            SetupSuccessfulRecommendationResponse(@"[
                {""artist"": ""Massive Attack"", ""genre"": ""Trip Hop"", ""confidence"": 0.92, ""reason"": ""Pioneered the genre""}
            ]");

            var adapter = CreateAdapterWithTempCliPath();

            // Act - "recommend artists" triggers artist-only mode
            var result = await adapter.GetRecommendationsAsync("recommend artists similar to Portishead");

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Massive Attack");
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithStreamingCapability_UsesStreaming()
        {
            // Arrange
            SetupCapabilities("--output-format stream-json --append-system-prompt --model");

            // Setup streaming response for the temp path
            _mockCliRunner
                .Setup(r => r.StreamAsync(
                    _tempCliPath,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(CreateStreamingResponse(@"[{""artist"": ""Sigur Ros"", ""album"": ""()"", ""genre"": ""Post-Rock"", ""confidence"": 0.88, ""reason"": ""Atmospheric""}]"));

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums like Mogwai");

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Sigur Ros");
        }

        #endregion

        #region Execute_WithCliTimeout_ReturnsEmpty Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithTimeout_ReturnsEmptyList()
        {
            // Arrange
            SetupCapabilities("--output-format --model");
            SetupExecutionException(new TimeoutException("CLI execution timed out"));

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums");

            // Assert
            result.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("timed out");
        }

        [Fact]
        public async Task TestConnectionAsync_WithTimeout_ReturnsFalse()
        {
            // Arrange
            SetupCapabilities("--output-format");

            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("Version check timed out"));

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            adapter.GetLastUserMessage().Should().Contain("timed out");
        }

        #endregion

        #region Execute_WithCliCrash_ReturnsEmpty Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithNonZeroExitCode_ReturnsEmpty()
        {
            // Arrange
            SetupCapabilities("--output-format --model");
            SetupErrorResponse(1, "Unexpected error occurred");

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums");

            // Assert
            result.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("exit 1");
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithExitCode127_SetsNotFoundMessage()
        {
            // Arrange
            SetupCapabilities("--output-format --model");
            SetupErrorResponse(127, "command not found");

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums");

            // Assert
            result.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("not found");
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithGeneralException_ReturnsEmpty()
        {
            // Arrange
            SetupCapabilities("--output-format --model");
            SetupExecutionException(new InvalidOperationException("Process crashed"));

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums");

            // Assert
            result.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("Process crashed");
        }

        #endregion

        #region Execute_WithCliStderr_LogsWarning Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithAuthenticationError_SetsAuthMessage()
        {
            // Arrange
            SetupCapabilities("--output-format --model");
            SetupErrorResponse(1, "Error: authentication required. Please run 'claude login'");

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums");

            // Assert
            result.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("Not authenticated");
            adapter.GetLearnMoreUrl().Should().NotBeNull();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithRateLimitError_SetsRateLimitMessage()
        {
            // Arrange
            SetupCapabilities("--output-format --model");
            SetupErrorResponse(1, "Error: rate limit exceeded. Please wait before retrying.");

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums");

            // Assert
            result.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("Rate limited");
        }

        #endregion

        #region TestConnection Tests

        [Fact]
        public async Task TestConnectionAsync_WithCliVersionCheckFailing_ReturnsFalse()
        {
            // Arrange - Use explicit temp path so we control detection
            // Then mock version check to fail
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
                    StandardError = "claude: command failed",
                    Duration = TimeSpan.FromMilliseconds(50)
                });

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.TestConnectionAsync();

            // Assert - Version check fails, adapter should return false with error message
            result.Should().BeFalse();
            adapter.GetLastUserMessage().Should().Contain("version check failed");
        }

        [Fact]
        public async Task TestConnectionAsync_WithVersionFailure_ReturnsFalse()
        {
            // Arrange
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
                    StandardError = "version check failed",
                    Duration = TimeSpan.FromMilliseconds(50)
                });

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            adapter.GetLastUserMessage().Should().Contain("version check failed");
        }

        [Fact]
        public async Task TestConnectionAsync_WithAuthFailure_ReturnsFalse()
        {
            // Arrange
            SetupVersionCheck("1.0.0");
            SetupCapabilities("--output-format --model");
            SetupErrorResponse(1, "Not authorized. Please login first.");

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            adapter.GetLastUserMessage().Should().Contain("Not authenticated");
        }

        [Fact]
        public async Task TestConnectionAsync_WithSuccessfulAuth_ReturnsTrue()
        {
            // Arrange
            SetupVersionCheck("1.0.0");
            SetupCapabilities("--output-format --model");
            SetupSuccessfulRecommendationResponse("OK");

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.TestConnectionAsync();

            // Assert
            result.Should().BeTrue();
            adapter.GetLastUserMessage().Should().BeNull();
        }

        #endregion

        #region Cancellation Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithCancellation_ReturnsEmpty()
        {
            // Arrange
            SetupCapabilities("--output-format --model");

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p") && !a.Contains("--help") && !a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums", cts.Token);

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TestConnectionAsync_WithCancellation_ReturnsFalse()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.TestConnectionAsync(cts.Token);

            // Assert
            result.Should().BeFalse();
            adapter.GetLastUserMessage().Should().Contain("cancelled");
        }

        #endregion

        #region UpdateModel Tests

        [Fact]
        public void UpdateModel_WithValidModel_Updates()
        {
            // Arrange
            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, null, "sonnet");

            // Act
            var action = () => adapter.UpdateModel("opus");

            // Assert
            action.Should().NotThrow();
        }

        [Fact]
        public void UpdateModel_WithEmptyModel_DoesNotUpdate()
        {
            // Arrange
            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger);

            // Act
            var action = () => adapter.UpdateModel("");

            // Assert
            action.Should().NotThrow();
        }

        [Fact]
        public void UpdateModel_WithWhitespaceModel_DoesNotUpdate()
        {
            // Arrange
            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger);

            // Act
            var action = () => adapter.UpdateModel("   ");

            // Assert
            action.Should().NotThrow();
        }

        #endregion

        #region CLI Path Resolution Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithExplicitCliPath_UsesExplicitPath()
        {
            // Arrange - Use explicit path (bypasses detection)
            // Create a temp file to satisfy File.Exists check
            var tempFile = System.IO.Path.GetTempFileName();
            try
            {
                SetupCapabilities("--output-format --model");
                SetupSuccessfulRecommendationResponseForPath(tempFile, @"[{""artist"": ""Test"", ""album"": ""Album"", ""genre"": ""Rock"", ""confidence"": 0.9, ""reason"": ""Test""}]");

                var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, tempFile, "sonnet");

                // Act
                var result = await adapter.GetRecommendationsAsync("test prompt");

                // Assert - Should use explicit path, not call detection
                _mockCliRunner.Verify(r => r.ExecuteAsync(
                    It.Is<string>(c => c == "which" || c == "where"),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()), Times.Never);

                _mockCliRunner.Verify(r => r.ExecuteAsync(
                    tempFile,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                System.IO.File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithCliExecutionFailing_ReturnsEmptyWithErrorMessage()
        {
            // Arrange - Use explicit temp path, set up capabilities and error response
            SetupCapabilities("--output-format --model");
            SetupErrorResponse(1, "claude: execution failed");

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("test prompt");

            // Assert - CLI execution fails, adapter should return empty with error message
            result.Should().BeEmpty();
            adapter.GetLastUserMessage().Should().Contain("exit 1");
        }

        #endregion

        #region Empty Response Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithEmptyResponse_ReturnsEmpty()
        {
            // Arrange
            SetupCapabilities("--output-format --model");

            // Use proper CLI response format with empty result
            var emptyResponseJson = BuildCliResponseJson("");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p") && !a.Contains("--help") && !a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 0,
                    StandardOutput = emptyResponseJson,
                    StandardError = "",
                    Duration = TimeSpan.FromSeconds(1)
                });

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithInvalidJson_ReturnsEmpty()
        {
            // Arrange
            SetupCapabilities("--output-format --model");

            // Use proper CLI response format with non-JSON recommendation text
            var invalidJsonResponseJson = BuildCliResponseJson("This is not valid JSON recommendations");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p") && !a.Contains("--help") && !a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 0,
                    StandardOutput = invalidJsonResponseJson,
                    StandardError = "",
                    Duration = TimeSpan.FromSeconds(1)
                });

            var adapter = CreateAdapterWithTempCliPath();

            // Act
            var result = await adapter.GetRecommendationsAsync("recommend albums");

            // Assert
            result.Should().BeEmpty();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates an adapter with the explicit temp CLI path, bypassing CLI detection.
        /// This allows tests to focus on CLI execution behavior without file existence issues.
        /// </summary>
        private ClaudeCodeCliAdapter CreateAdapterWithTempCliPath(string? model = null)
        {
            return new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath, model);
        }

        private void SetupCliDetected(string cliPath)
        {
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    It.Is<string>(c => c == "which" || c == "where"),
                    It.Is<IReadOnlyList<string>>(a => a.Count == 1 && a[0] == "claude"),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 0,
                    StandardOutput = cliPath + "\n",
                    StandardError = "",
                    Duration = TimeSpan.FromMilliseconds(50)
                });
        }

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

        /// <summary>
        /// Creates a CLI response JSON in the correct Claude Code CLI format.
        /// The CLI uses --output-format json which returns: { type, result, session_id, is_error, usage, ... }
        /// </summary>
        private static string BuildCliResponseJson(string resultContent, bool isError = false)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                type = isError ? "error" : "result",
                result = resultContent,
                session_id = "test-session-123",
                is_error = isError,
                num_turns = 1,
                duration_ms = 1500,
                total_cost_usd = 0.001m,
                usage = new
                {
                    input_tokens = 100,
                    output_tokens = 50
                }
            });
        }

        /// <summary>
        /// Sets up a successful recommendation response mock for the temp CLI path.
        /// </summary>
        private void SetupSuccessfulRecommendationResponse(string recommendationsJson)
        {
            SetupSuccessfulRecommendationResponseForPath(_tempCliPath, recommendationsJson);
        }

        private void SetupSuccessfulRecommendationResponseForPath(string cliPath, string recommendationsJson)
        {
            var responseJson = BuildCliResponseJson(recommendationsJson);

            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    cliPath,
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

        /// <summary>
        /// Sets up a CLI error response mock for the temp CLI path.
        /// </summary>
        private void SetupErrorResponse(int exitCode, string stderr)
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
                    Duration = TimeSpan.FromSeconds(0.5)
                });
        }

        /// <summary>
        /// Sets up a CLI exception mock for the temp CLI path.
        /// </summary>
        private void SetupExecutionException(Exception exception)
        {
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p") && !a.Contains("--help") && !a.Contains("--version")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);
        }

        private static async IAsyncEnumerable<CliStreamEvent> CreateStreamingResponse(
            string content,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new CliStreamEvent.Started(12345);

            // Emit NDJSON content events
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""message_start"",""message"":{""id"":""msg_123""}}");
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_start"",""index"":0}");

            // Split content into chunks for realistic streaming
            var escaped = content.Replace("\"", "\\\"");
            yield return new CliStreamEvent.StandardOutput($@"{{""type"":""content_block_delta"",""delta"":{{""type"":""text_delta"",""text"":""{escaped}""}}}}");

            yield return new CliStreamEvent.StandardOutput(@"{""type"":""content_block_stop"",""index"":0}");
            yield return new CliStreamEvent.StandardOutput(@"{""type"":""message_stop""}");

            yield return new CliStreamEvent.Exited(0, TimeSpan.FromSeconds(1));

            await Task.CompletedTask;
        }

        #endregion
    }
}
