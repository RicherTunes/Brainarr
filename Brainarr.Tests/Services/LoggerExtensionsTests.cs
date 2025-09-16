using System;
using System.Linq;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Logging")]
    public class LoggerExtensionsTests
    {
        private readonly Logger _logger;

        public LoggerExtensionsTests()
        {
            _logger = TestLogger.Create("LoggerExtensionsTests");
            TestLogger.ClearLoggedMessages();
        }

        [Fact]
        public void LoggerExtensions_Should_IncludeCorrelationId_InMessages()
        {
            // Arrange
            var correlation = CorrelationContext.GenerateCorrelationId();

            using (var scope = new CorrelationScope(correlation))
            {
                // Act
                _logger.DebugWithCorrelation("debug message");
                _logger.InfoWithCorrelation("info message {0}", 123);
                _logger.WarnWithCorrelation("warn message");
                _logger.ErrorWithCorrelation(new InvalidOperationException("boom"), "error message");
                _logger.ErrorWithCorrelation("error message with params {0}", 42);
            }

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();

            if (logs.Any())
            {
                var expectedMessages = new[] { "debug message", "info message", "warn message", "error message" };
                foreach (var message in expectedMessages)
                {
                    logs.Should().Contain(line => line.Contains(message, StringComparison.OrdinalIgnoreCase) && line.Contains(correlation));
                }

                logs.Any(l => l.Contains("DEBUG:")).Should().BeTrue();
                logs.Any(l => l.Contains("INFO:")).Should().BeTrue();
                logs.Any(l => l.Contains("WARN:")).Should().BeTrue();
                logs.Any(l => l.Contains("ERROR:")).Should().BeTrue();
            }
        }

        [Theory]
        [InlineData("https://example.com/path?api_key=secret#frag", "https://example.com/path")]
        [InlineData("http://example.com:8080/path/sub", "http://example.com:8080/path/sub")]
        public void UrlSanitizer_Should_RemoveSensitiveParts_AndKeepPath(string input, string expected)
        {
            // Act
            var sanitized = UrlSanitizer.SanitizeUrl(input);

            // Assert
            sanitized.Should().Be(expected);
        }

        [Fact]
        public void UrlSanitizer_Should_Fallback_WhenUrlIsMalformed()
        {
            // Arrange
            var malformed = "http://exa mple.com/path?token=abc#frag";

            // Act
            var sanitized = UrlSanitizer.SanitizeUrl(malformed);

            // Assert
            sanitized.Should().Be("http://exa mple.com/path");
        }
    }
}
