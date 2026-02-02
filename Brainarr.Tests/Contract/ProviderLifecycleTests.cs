using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Contract
{
    /// <summary>
    /// Provider lifecycle logging contract tests validating that providers emit
    /// correct logging events during their lifecycle: start, complete, error.
    /// </summary>
    [Trait("Category", "Contract")]
    [Trait("Target", "Provider")]
    public class ProviderLifecycleTests
    {
        private sealed class MockAIProvider : IAIProvider
        {
            private readonly ILogger _logger;
            private readonly bool _simulateError;

            public MockAIProvider(ILogger logger, bool simulateError = false)
            {
                _logger = logger;
                _simulateError = simulateError;
            }

            public string ProviderName => "MockAIProvider";

            public Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken = default)
            {
                LogProviderStart();
                try
                {
                    var recommendations = new List<Recommendation>
                    {
                        new Recommendation
                        {
                            Artist = "Test Artist",
                            Album = "Test Album",
                            Genre = "Test Genre",
                            Year = 2025,
                            Confidence = 0.95
                        }
                    };
                    LogProviderComplete(recommendations.Count);
                    return Task.FromResult(recommendations);
                }
                catch (Exception ex)
                {
                    LogProviderError(ex);
                    throw;
                }
            }

            public Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken)
                => GetRecommendationsAsync(prompt, cancellationToken);

            public Task<ProviderHealthResult> TestConnectionAsync()
            {
                LogProviderStart();
                try
                {
                    var result = ProviderHealthResult.Ok("Provider connected successfully");
                    LogProviderComplete(0);
                    return Task.FromResult(result);
                }
                catch (Exception ex)
                {
                    LogProviderError(ex);
                    return Task.FromResult(ProviderHealthResult.Fail("TestConnectionAsync failed", new[] { ex }));
                }
            }

            public Task<ProviderHealthResult> TestConnectionAsync(CancellationToken cancellationToken)
                => TestConnectionAsync();

            public void UpdateModel(string modelName) { }

            public string? GetLastUserMessage() => null;

            public string? GetLearnMoreUrl() => null;

            private void LogProviderStart()
            {
                using var scope = new CorrelationScope(CorrelationContext.GenerateCorrelationId());
                _logger.InfoWithCorrelation("[{Provider}] Operation started - {Operation}", ProviderName, "GetRecommendations");
            }

            private void LogProviderComplete(int itemCount)
            {
                using var scope = new CorrelationScope(CorrelationContext.GenerateCorrelationId());
                _logger.InfoWithCorrelation("[{Provider}] Operation completed successfully. Items: {ItemCount}", ProviderName, itemCount);
            }

            private void LogProviderError(Exception ex)
            {
                using var scope = new CorrelationScope(CorrelationContext.GenerateCorrelationId());
                _logger.ErrorWithCorrelation(ex, "[{Provider}] Operation failed - {Operation}", ProviderName, "GetRecommendations");
            }
        }

        [Fact]
        public async Task Provider_LogsStartEvent_WhenOperationBegins()
        {
            // Arrange
            var logger = TestLogger.Create("ProviderLifecycleTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
            logs.Should().Contain(log => log.Contains("MockAIProvider") && log.Contains("Operation started"));

            var startLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Operation started"));
            startLog.Should().NotBeNull();
        }

        [Fact]
        public async Task Provider_LogsCompleteEvent_WhenOperationSucceeds()
        {
            // Arrange
            var logger = TestLogger.Create("ProviderLifecycleTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
            logs.Should().Contain(log => log.Contains("MockAIProvider") && log.Contains("Operation completed"));

            var completeLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Operation completed"));
            completeLog.Should().NotBeNull();
            completeLog.Should().Contain("Items: 1");
        }

        [Fact]
        public async Task Provider_LogsErrorEvent_WhenOperationFails()
        {
            // Arrange
            var logger = TestLogger.Create("ProviderLifecycleTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateError: true);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.GetRecommendationsAsync("test prompt"));

            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
            logs.Should().Contain(log => log.Contains("MockAIProvider") && log.Contains("Operation failed"));
        }

        [Fact]
        public async Task Provider_LogsBothStartAndComplete_WhenOperationSucceeds()
        {
            // Arrange
            var logger = TestLogger.Create("ProviderLifecycleTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();

            var startLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Operation started"));
            var completeLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Operation completed"));

            startLog.Should().NotBeNull();
            completeLog.Should().NotBeNull();
        }

        [Fact]
        public void Provider_LogsRequiredFields_WhenEventEmitted()
        {
            // Arrange
            var logger = TestLogger.Create("ProviderLifecycleTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            provider.GetRecommendationsAsync("test prompt");

            // Assert - Check start event has required fields
            var logs = TestLogger.GetLoggedMessages();
            var startLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Operation started"));

            startLog.Should().NotBeNull();
            startLog.Should().Contain("CorrelationId");
            startLog.Should().Contain("MockAIProvider");

            // Check complete event has required fields
            var completeLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Operation completed"));

            completeLog.Should().NotBeNull();
            completeLog.Should().Contain("Items:");
        }

        [Fact]
        public async Task Provider_LogsCompleteWithCorrectItemCount_WhenMultipleResultsReturned()
        {
            // Arrange
            var logger = TestLogger.Create("ProviderLifecycleTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            var completeLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Operation completed"));

            completeLog.Should().NotBeNull();
            completeLog.Should().Contain("Items: 1");
        }

        [Fact]
        public async Task Provider_LogsErrorWithExceptionDetails_WhenOperationFails()
        {
            // Arrange
            var logger = TestLogger.Create("ProviderLifecycleTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateError: true);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.GetRecommendationsAsync("test prompt"));

            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
            logs.Should().Contain(log => log.Contains("MockAIProvider") && log.Contains("Operation failed"));

            var errorLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Operation failed"));
            errorLog.Should().NotBeNull();
            errorLog.Should().Contain("InvalidOperationException");
        }
    }
}
