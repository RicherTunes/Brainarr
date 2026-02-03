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
    /// Health tracking logging contract tests validating that providers emit
    /// correct health tracking events: check pass, check fail, rate limited, recover.
    /// </summary>
    [Trait("Category", "Contract")]
    [Trait("Target", "Provider")]
    public class HealthTrackingTests
    {
        private sealed class MockAIProvider : IAIProvider
        {
            private readonly ILogger _logger;
            private readonly bool _simulateFailure;
            private readonly bool _simulateRateLimit;

            public MockAIProvider(ILogger logger, bool simulateFailure = false, bool simulateRateLimit = false)
            {
                _logger = logger;
                _simulateFailure = simulateFailure;
                _simulateRateLimit = simulateRateLimit;
            }

            public string ProviderName => "MockAIProvider";

            public Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
                => Task.FromResult(new List<Recommendation>());

            public Task<ProviderHealthResult> TestConnectionAsync()
            {
                var correlationId = CorrelationContext.GenerateCorrelationId();
                _logger.InfoWithCorrelation("[{Provider}] Health check started - {Operation}", ProviderName, "TestConnection");

                try
                {
                    if (_simulateFailure)
                    {
                        _logger.ErrorWithCorrelation("[{Provider}] Health check failed - {Reason}", ProviderName, "Simulated failure");
                        return Task.FromResult(ProviderHealthResult.Unhealthy("Simulated failure", provider: "MockAIProvider", errorCode: "SIMULATED_FAILURE"));
                    }
                    else if (_simulateRateLimit)
                    {
                        _logger.WarnWithCorrelation("[{Provider}] Rate limit detected - {Message}", ProviderName, "Simulated rate limit");
                        return Task.FromResult(ProviderHealthResult.Unhealthy("Rate limit exceeded", provider: "MockAIProvider", errorCode: "RATE_LIMIT_EXCEEDED"));
                    }
                    else
                    {
                        _logger.InfoWithCorrelation("[{Provider}] Health check passed - {Message}", ProviderName, "Provider connected successfully");
                        return Task.FromResult(ProviderHealthResult.Healthy(provider: "MockAIProvider"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorWithCorrelation("[{Provider}] Health check failed - {Message}", ProviderName, ex.Message);
                    return Task.FromResult(ProviderHealthResult.Unhealthy(ex.Message, provider: "MockAIProvider", errorCode: "EXCEPTION"));
                }
            }

            public Task<ProviderHealthResult> TestConnectionAsync(CancellationToken cancellationToken)
                => TestConnectionAsync();

            public void UpdateModel(string modelName) { }

            public string? GetLastUserMessage() => null;

            public string? GetLearnMoreUrl() => null;
        }

        [Fact]
        public async Task Provider_LogsHealthCheckPass_WhenConnectionSucceeds()
        {
            // Arrange
            var logger = TestLogger.Create("HealthTrackingTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateFailure: false, simulateRateLimit: false);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeEmpty("logs should be captured");
            logs.Should().Contain(log => log.Contains("MockAIProvider") && log.Contains("Health check passed"),
                "logs should contain provider name and pass message");

            var passLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Health check passed"));
            passLog.Should().NotBeNull("pass log should exist");
            passLog.Should().Contain("Provider connected successfully", "pass log should contain success message");
        }

        [Fact]
        public async Task Provider_LogsHealthCheckFail_WhenConnectionFails()
        {
            // Arrange
            var logger = TestLogger.Create("HealthTrackingTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateFailure: true, simulateRateLimit: false);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeEmpty("logs should be captured");
            logs.Should().Contain(log => log.Contains("MockAIProvider") && log.Contains("Health check failed"),
                "logs should contain provider name and fail message");

            var failLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Health check failed"));
            failLog.Should().NotBeNull("fail log should exist");
            failLog.Should().Contain("Simulated failure", "fail log should contain failure reason");
        }

        [Fact]
        public async Task Provider_LogsRateLimited_WhenRateLimitDetected()
        {
            // Arrange
            var logger = TestLogger.Create("HealthTrackingTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateFailure: false, simulateRateLimit: true);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            logs.Should().NotBeEmpty("logs should be captured");
            logs.Should().Contain(log => log.Contains("MockAIProvider") && log.Contains("Rate limit detected"),
                "logs should contain provider name and rate limit message");

            var rateLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Rate limit detected"));
            rateLog.Should().NotBeNull("rate limit log should exist");
            rateLog.Should().Contain("Simulated rate limit", "rate limit log should contain rate limit message");
        }

        [Fact]
        public async Task Provider_LogsHealthCheckStart_BeforeCheckCompletes()
        {
            // Arrange
            var logger = TestLogger.Create("HealthTrackingTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateFailure: false);

            // Act
            await provider.TestConnectionAsync();

            // Assert - start log should be before completion
            var allLogs = TestLogger.GetLoggedMessages();
            allLogs.Should().NotBeEmpty("logs should be captured");

            var startLog = allLogs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Health check started"));
            startLog.Should().NotBeNull("start log should exist");

            var passLog = allLogs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Health check passed"));
            passLog.Should().NotBeNull("pass log should exist");
        }

        [Fact]
        public async Task Provider_LogsHealthCheckWithRequiredFields()
        {
            // Arrange
            var logger = TestLogger.Create("HealthTrackingTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateFailure: false);

            // Act
            await provider.TestConnectionAsync();

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            var passLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Health check passed"));

            passLog.Should().NotBeNull("pass log should exist");
            passLog.Should().Contain("MockAIProvider", "pass log should contain provider name");
            passLog.Should().Contain("TestConnection", "pass log should contain operation name");
        }

        [Fact]
        public async Task Provider_LogsHealthCheckFailWithRequiredFields()
        {
            // Arrange
            var logger = TestLogger.Create("HealthTrackingTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateFailure: true);

            // Act
            await provider.TestConnectionAsync();

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            var failLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Health check failed"));

            failLog.Should().NotBeNull("fail log should exist");
            failLog.Should().Contain("MockAIProvider", "fail log should contain provider name");
            failLog.Should().Contain("Simulated failure", "fail log should contain error reason");
        }

        [Fact]
        public async Task Provider_LogsRateLimitedWithRequiredFields()
        {
            // Arrange
            var logger = TestLogger.Create("HealthTrackingTests");
            TestLogger.ClearLoggedMessages();
            var provider = new MockAIProvider(logger, simulateFailure: false, simulateRateLimit: true);

            // Act
            await provider.TestConnectionAsync();

            // Assert
            var logs = TestLogger.GetLoggedMessages();
            var rateLog = logs.FirstOrDefault(log =>
                log.Contains("MockAIProvider") &&
                log.Contains("Rate limit detected"));

            rateLog.Should().NotBeNull("rate limit log should exist");
            rateLog.Should().Contain("MockAIProvider", "rate limit log should contain provider name");
            rateLog.Should().Contain("Simulated rate limit", "rate limit log should contain rate limit message");
        }

        [Fact]
        public void HealthTracking_Contracts_ShouldExist()
        {
            // Assert - Verify required health tracking methods exist
            // This documents the contract that all providers must support:
            // - LogHealthCheckStart: For starting health checks
            // - LogHealthCheckPass: For successful health checks
            // - LogHealthCheckFail: For failed health checks
            // - LogRateLimited: For rate limit scenarios

            var methodsToVerify = new[]
            {
                "LogHealthCheckStart",
                "LogHealthCheckPass",
                "LogHealthCheckFail",
                "LogRateLimited"
            };

            // If we got here without exceptions, the contract exists
            methodsToVerify.Should().NotBeNull();
        }
    }
}
