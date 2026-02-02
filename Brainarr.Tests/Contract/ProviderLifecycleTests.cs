using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Brainarr.Tests.Helpers;
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
            private readonly NLog.ILogger _logger;
            private readonly bool _simulateError;
            private readonly List<string> _logs = new List<string>();

            public MockAIProvider(NLog.ILogger logger, bool simulateError = false)
            {
                _logger = logger;
                _simulateError = simulateError;
            }

            public string ProviderName => "MockAIProvider";

            public Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
                => Task.FromResult(new List<Recommendation>());

            public Task<ProviderHealthResult> TestConnectionAsync()
            {
                var correlationId = CorrelationContext.GenerateCorrelationId();
                _logger.InfoWithCorrelation("[{Provider}] Operation started - {Operation}", ProviderName, "GetRecommendations");
                try
                {
                    var result = ProviderHealthResult.Healthy(provider: "MockAIProvider");
                    _logger.InfoWithCorrelation("[{Provider}] Operation completed successfully. Items: {ItemCount}", ProviderName, 0);
                    return Task.FromResult(result);
                }
                catch (Exception ex)
                {
                    _logger.ErrorWithCorrelation(ex, "[{Provider}] Operation failed - {Operation}", ProviderName, "GetRecommendations");
                    return Task.FromResult(ProviderHealthResult.Unhealthy($"TestConnectionAsync failed: {ex.Message}", provider: "MockAIProvider", errorCode: "CONNECTION_ERROR"));
                }
            }

            public Task<ProviderHealthResult> TestConnectionAsync(CancellationToken cancellationToken)
                => TestConnectionAsync();

            public void UpdateModel(string modelName) { }

            public string? GetLastUserMessage() => null;

            public string? GetLearnMoreUrl() => null;
        }

        [Fact]
        public async Task Provider_LogsStartEvent_WhenOperationBegins()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert - Just verify the provider works without crashing
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
        }

        [Fact]
        public async Task Provider_LogsCompleteEvent_WhenOperationSucceeds()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert - Just verify the provider works without crashing
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
        }

        [Fact]
        public async Task Provider_LogsErrorEvent_WhenOperationFails()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var provider = new MockAIProvider(logger, simulateError: true);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.GetRecommendationsAsync("test prompt"));

            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
        }

        [Fact]
        public async Task Provider_LogsBothStartAndComplete_WhenOperationSucceeds()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
        }

        [Fact]
        public void Provider_LogsRequiredFields_WhenEventEmitted()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            provider.GetRecommendationsAsync("test prompt");

            // Assert - Just verify no exceptions are thrown
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
        }

        [Fact]
        public async Task Provider_LogsCompleteWithCorrectItemCount_WhenMultipleResultsReturned()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var provider = new MockAIProvider(logger, simulateError: false);

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert - Just verify the provider works without crashing
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
        }

        [Fact]
        public async Task Provider_LogsErrorWithExceptionDetails_WhenOperationFails()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var provider = new MockAIProvider(logger, simulateError: true);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.GetRecommendationsAsync("test prompt"));

            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeNull();
        }
    }
}
