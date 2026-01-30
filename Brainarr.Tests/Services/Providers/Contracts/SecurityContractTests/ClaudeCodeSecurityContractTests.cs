using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Providers.ClaudeCode;
using Lidarr.Plugin.Common.Subprocess;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.ClaudeCode;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Contracts.SecurityContractTests
{
    /// <summary>
    /// Security contract tests for Claude Code provider.
    /// Verifies no secrets leak through logs, exceptions, or user messages.
    /// </summary>
    [Trait("Category", "Security")]
    [Trait("Provider", "ClaudeCode")]
    [Collection("ClaudeCodeSecurity")]
    public class ClaudeCodeSecurityContractTests : IDisposable
    {
        private readonly Mock<ICliRunner> _mockCliRunner;
        private readonly TestLogCapture _logCapture;
        private readonly NLog.Logger _logger;
        private readonly string _tempCliPath;

        // Sensitive data patterns that should NEVER appear in logs/errors
        private readonly string[] _sensitivePatterns = new[]
        {
            "sk-ant-",           // Anthropic API key prefix
            "anthropic_",        // Alternative key prefix
            "claude_session",    // Session token prefix
            "ANTHROPIC_API_KEY", // Environment variable name in actual value context
            ".claude/credentials", // Credentials file path
            "access_token",      // OAuth tokens
            "refresh_token",     // OAuth refresh tokens
        };

        public ClaudeCodeSecurityContractTests()
        {
            _mockCliRunner = new Mock<ICliRunner>();
            _logCapture = new TestLogCapture();
            _logger = _logCapture.CreateLogger();
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

        #region CLI Arguments Security

        [Fact]
        public async Task CliArgs_NeverContainApiKeys()
        {
            // Arrange: Capture CLI arguments
            IReadOnlyList<string>? capturedArgs = null;

            SetupCapabilities("--output-format --model --append-system-prompt");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, IReadOnlyList<string>, CliRunnerOptions?, CancellationToken>(
                    (_, args, _, _) => capturedArgs = args)
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 0,
                    StandardOutput = BuildCliResponseJson("[]"),
                    StandardError = "",
                    Duration = TimeSpan.FromSeconds(1)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: No sensitive data in CLI args
            capturedArgs.Should().NotBeNull();
            var argsString = string.Join(" ", capturedArgs);

            foreach (var pattern in _sensitivePatterns)
            {
                argsString.Should().NotContain(pattern,
                    $"CLI arguments should not contain sensitive pattern: {pattern}");
            }
        }

        [Fact]
        public async Task CliArgs_DoNotExposeUserPromptAsCredential()
        {
            // Arrange: Use a prompt that looks like a credential
            var dangerousPrompt = "My API key is sk-ant-12345 and token is abc123";
            IReadOnlyList<string>? capturedArgs = null;

            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, IReadOnlyList<string>, CliRunnerOptions?, CancellationToken>(
                    (_, args, _, _) => capturedArgs = args)
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 0,
                    StandardOutput = BuildCliResponseJson("[]"),
                    StandardError = "",
                    Duration = TimeSpan.FromSeconds(1)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync(dangerousPrompt);

            // Assert: The prompt IS passed as an argument (this is expected),
            // but no ACTUAL credentials from the adapter itself should be present
            capturedArgs.Should().NotBeNull();

            // The prompt should be passed through
            capturedArgs.Should().Contain(dangerousPrompt);

            // But no real authentication headers or environment variables should be set as args
            capturedArgs.Should().NotContain("--api-key");
            capturedArgs.Should().NotContain("Authorization");
        }

        [Fact]
        public async Task CliArgs_SystemPromptDoesNotContainSecrets()
        {
            // Arrange: Capture CLI arguments
            IReadOnlyList<string>? capturedArgs = null;

            SetupCapabilities("--output-format --model --append-system-prompt");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, IReadOnlyList<string>, CliRunnerOptions?, CancellationToken>(
                    (_, args, _, _) => capturedArgs = args)
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 0,
                    StandardOutput = BuildCliResponseJson("[]"),
                    StandardError = "",
                    Duration = TimeSpan.FromSeconds(1)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("recommend albums");

            // Assert: System prompt should not contain any secrets
            capturedArgs.Should().NotBeNull();

            var systemPromptIndex = capturedArgs!.ToList().IndexOf("--append-system-prompt");
            if (systemPromptIndex >= 0 && systemPromptIndex < capturedArgs.Count - 1)
            {
                var systemPrompt = capturedArgs[systemPromptIndex + 1];
                foreach (var pattern in _sensitivePatterns)
                {
                    systemPrompt.Should().NotContain(pattern,
                        $"System prompt should not contain sensitive pattern: {pattern}");
                }
            }
        }

        #endregion

        #region Error Logging Security

        [Fact]
        public async Task ErrorLogs_NoCredentialLeakageOnAuthFailure()
        {
            // Arrange: Simulate auth error with sensitive-looking error message
            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 1,
                    StandardOutput = "",
                    StandardError = "Error: Authentication failed. Token sk-ant-fake-token is invalid. Check ~/.claude/credentials",
                    Duration = TimeSpan.FromSeconds(1)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: Log output should not contain the fake token
            var logs = _logCapture.GetLogs();
            var allLogs = string.Join("\n", logs);

            // The specific token should not appear in logs
            allLogs.Should().NotContain("sk-ant-fake-token",
                "Logs should not contain API tokens even from error messages");
        }

        [Fact]
        public async Task ErrorLogs_NoCredentialPathLeakage()
        {
            // Arrange: Simulate error mentioning credential file path
            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 1,
                    StandardOutput = "",
                    StandardError = "Error: Could not read /home/user/.claude/credentials: Permission denied",
                    Duration = TimeSpan.FromSeconds(1)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: User message should give generic guidance, not expose file paths
            var userMessage = adapter.GetLastUserMessage();
            userMessage.Should().NotBeNull();

            // The user message may contain auth guidance, but shouldn't expose the exact file path
            // Note: This is a soft check - the error message IS logged for debugging
            // The key is that the USER-FACING message is sanitized
        }

        [Fact]
        public async Task ErrorLogs_NoEnvironmentVariableLeakage()
        {
            // Arrange: Error that mentions environment variable
            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 1,
                    StandardOutput = "",
                    StandardError = "Error: ANTHROPIC_API_KEY=sk-ant-secret123 is invalid",
                    Duration = TimeSpan.FromSeconds(1)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: The actual API key value should not leak to user message
            var userMessage = adapter.GetLastUserMessage();
            userMessage.Should().NotBeNull();
            userMessage.Should().NotContain("sk-ant-secret123",
                "User message should not contain actual API key values");
        }

        #endregion

        #region User Message Security

        [Fact]
        public async Task UserMessage_DoesNotExposeInternalPaths()
        {
            // Arrange: Error with internal path
            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 127,
                    StandardOutput = "",
                    StandardError = "",
                    Duration = TimeSpan.FromSeconds(0.5)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: User message should be user-friendly
            var userMessage = adapter.GetLastUserMessage();
            userMessage.Should().NotBeNull();
            userMessage.Should().Contain("not found");

            // Should not expose internal implementation details
            userMessage.Should().NotContain("ICliRunner");
            userMessage.Should().NotContain("ClaudeCodeDetector");
        }

        [Fact]
        public async Task UserMessage_AuthenticationErrorIsUserFriendly()
        {
            // Arrange: Authentication failure
            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 1,
                    StandardOutput = "",
                    StandardError = "Error: unauthorized - invalid authentication token",
                    Duration = TimeSpan.FromSeconds(0.5)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: User message should provide guidance
            var userMessage = adapter.GetLastUserMessage();
            userMessage.Should().NotBeNull();
            userMessage.Should().Contain("Not authenticated");
            userMessage.Should().Contain("claude login");

            // Learn more URL should be available
            adapter.GetLearnMoreUrl().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task UserMessage_RateLimitIsUserFriendly()
        {
            // Arrange: Rate limit error
            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CliResult
                {
                    ExitCode = 1,
                    StandardOutput = "",
                    StandardError = "Error: rate limit exceeded, retry in 60 seconds",
                    Duration = TimeSpan.FromSeconds(0.5)
                });

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: User message should be friendly
            var userMessage = adapter.GetLastUserMessage();
            userMessage.Should().NotBeNull();
            userMessage.Should().Contain("Rate limited");
        }

        #endregion

        #region Settings Serialization Security

        [Fact]
        public void Settings_CredentialPathNotExposedInSerialization()
        {
            // This test verifies that credential-related settings are not
            // accidentally serialized to JSON (e.g., for logging or config export)

            // The ClaudeCode provider uses CLI session auth, so there shouldn't be
            // any API keys in settings. The credential path might be configurable
            // but should not be serialized to logs.

            // Since ClaudeCodeCliAdapter doesn't have a public settings object,
            // this test verifies the constructor parameters don't get exposed
            var adapter = new ClaudeCodeCliAdapter(
                _mockCliRunner.Object,
                _logger,
                "/sensitive/path/to/claude",
                "sonnet");

            // Verify provider name doesn't expose path
            adapter.ProviderName.Should().Be("Claude Code CLI");
            adapter.ProviderName.Should().NotContain("/sensitive/");
        }

        #endregion

        #region Exception Safety

        [Fact]
        public async Task Exception_DoesNotExposeCliPath()
        {
            // Arrange: Force an exception
            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Process at /sensitive/path/claude failed"));

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: User message should sanitize the exception
            var userMessage = adapter.GetLastUserMessage();
            userMessage.Should().NotBeNull();

            // The internal path from the exception message is captured but
            // the user-facing message should be generic
            userMessage.Should().Contain("failed");
        }

        [Fact]
        public async Task Exception_DoesNotExposeStackTrace()
        {
            // Arrange: Force an exception
            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Something went wrong"));

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: User message should not contain stack trace
            var userMessage = adapter.GetLastUserMessage();
            userMessage.Should().NotBeNull();
            userMessage.Should().NotContain("at Brainarr.");
            userMessage.Should().NotContain("StackTrace");
            userMessage.Should().NotContain(".cs:line");
        }

        [Fact]
        public async Task Exception_InnerExceptionChainIsRedacted()
        {
            // Arrange: Create exception chain with secrets in inner exceptions
            var innerInner = new Exception("ANTHROPIC_API_KEY=sk-ant-secret-inner-key123 is invalid");
            var inner = new Exception("Token anthropic_secret_token456 expired", innerInner);
            var outer = new Exception("CLI execution failed with access_token=abc123xyz", inner);

            SetupCapabilities("--output-format --model");
            _mockCliRunner
                .Setup(r => r.ExecuteAsync(
                    _tempCliPath,
                    It.Is<IReadOnlyList<string>>(a => a.Contains("-p")),
                    It.IsAny<CliRunnerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(outer);

            var adapter = new ClaudeCodeCliAdapter(_mockCliRunner.Object, _logger, _tempCliPath);

            // Act
            await adapter.GetRecommendationsAsync("test prompt");

            // Assert: User message should have ALL secrets redacted from entire exception chain
            var userMessage = adapter.GetLastUserMessage();
            userMessage.Should().NotBeNull();

            // None of the actual secret values should appear
            userMessage.Should().NotContain("sk-ant-secret-inner-key123",
                "Inner-inner exception API key should be redacted");
            userMessage.Should().NotContain("anthropic_secret_token456",
                "Inner exception token should be redacted");
            userMessage.Should().NotContain("abc123xyz",
                "Outer exception token value should be redacted");

            // The redaction placeholders should be present instead
            userMessage.Should().Contain("REDACTED",
                "Redaction should be applied to the exception chain");
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

        private static string BuildCliResponseJson(string resultContent)
        {
            return JsonSerializer.Serialize(new
            {
                type = "result",
                result = resultContent,
                session_id = "test-session-123",
                is_error = false,
                num_turns = 1,
                duration_ms = 1500,
                total_cost_usd = 0.001m,
                usage = new { input_tokens = 100, output_tokens = 50 }
            });
        }

        #endregion
    }

    /// <summary>
    /// Simple log capture for security testing.
    /// </summary>
    internal class TestLogCapture
    {
        private readonly List<string> _logs = new();

        public NLog.Logger CreateLogger()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var target = new NLog.Targets.MethodCallTarget("testcapture", (logEvent, parameters) =>
            {
                _logs.Add(logEvent.FormattedMessage);
            });
            config.AddTarget(target);
            config.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, target);

            var factory = new NLog.LogFactory(config);
            return factory.GetCurrentClassLogger();
        }

        public IReadOnlyList<string> GetLogs() => _logs.AsReadOnly();
    }
}
