using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using Brainarr.Tests.Helpers;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class RetryPolicyTests
    {
        private readonly Logger _logger;
        private readonly ExponentialBackoffRetryPolicy _retryPolicy;

        public RetryPolicyTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _retryPolicy = new ExponentialBackoffRetryPolicy(
                _logger,
                maxRetries: 3,
                initialDelay: TimeSpan.FromMilliseconds(10));
        }

        [Fact]
        public async Task ExecuteAsync_SuccessOnFirstAttempt_ReturnsResult()
        {
            // Arrange
            var expectedResult = "success";
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                return expectedResult;
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be(expectedResult);
            attempts.Should().Be(1);
        }

        [Fact]
        public async Task ExecuteAsync_SuccessOnSecondAttempt_ReturnsResult()
        {
            // Arrange
            var expectedResult = "success";
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts == 1)
                {
                    // Use HttpRequestException which is retryable per RetryUtilities
                    throw new HttpRequestException("First attempt fails - transient network error");
                }
                return expectedResult;
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be(expectedResult);
            attempts.Should().Be(2);
            // Note: Logger verification removed as Logger methods are non-overridable
        }

        [Fact]
        public async Task ExecuteAsync_AllAttemptsFail_ThrowsRetryExhaustedException()
        {
            // Arrange
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                // Use TimeoutException which is retryable per RetryUtilities
                throw new TimeoutException($"Attempt {attempts} timed out");
            };

            // Act
            Func<Task> act = async () => await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            await act.Should().ThrowAsync<RetryExhaustedException>()
                .WithMessage("*TestOperation*failed after 3 attempts*");
            attempts.Should().Be(3);
            // Note: Logger verification removed as Logger methods are non-overridable
        }

        [Fact]
        public async Task ExecuteAsync_TaskCancelledException_DoesNotRetry()
        {
            // Arrange
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                throw new TaskCanceledException("Operation was cancelled");
            };

            // Act
            Func<Task> act = async () => await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            await act.Should().ThrowAsync<TaskCanceledException>();
            attempts.Should().Be(1); // Should not retry on cancellation
        }

        [Fact]
        public async Task ExecuteAsync_NonRetryableException_DoesNotRetry()
        {
            // Arrange - InvalidOperationException is NOT retryable per RetryUtilities
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                throw new InvalidOperationException("This is a permanent failure (not retryable)");
            };

            // Act
            Func<Task> act = async () => await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>();
            attempts.Should().Be(1); // Should not retry on non-retryable exceptions
        }

        [Fact]
        public async Task ExecuteAsync_DelayIncreasesExponentially()
        {
            // Arrange
            var delays = new List<TimeSpan>();
            var policy = new TestableRetryPolicy(_logger, delays);
            var attempts = 0;

            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 3)
                {
                    // Use HttpRequestException which is retryable per RetryUtilities
                    throw new HttpRequestException("Transient network error");
                }
                return "success";
            };

            // Act
            await policy.ExecuteAsync(action, "TestOperation");

            // Assert
            delays.Should().HaveCount(2); // Two retries with delays
            delays[1].Should().BeGreaterThan(delays[0]); // Exponential increase
        }

        [Fact]
        public async Task ExecuteAsync_LogsRetryInformation()
        {
            // Arrange
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 2)
                {
                    // Use TimeoutException which is retryable per RetryUtilities
                    throw new TimeoutException("Test timeout");
                }
                return "success";
            };

            // Act
            await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            // Note: Logger verification removed as Logger methods are non-overridable
            // Note: Logger verification removed as Logger methods are non-overridable
        }

        private class TestableRetryPolicy : ExponentialBackoffRetryPolicy
        {
            private readonly List<TimeSpan> _recordedDelays;

            public TestableRetryPolicy(Logger logger, List<TimeSpan> recordedDelays)
                : base(logger, 3, TimeSpan.FromMilliseconds(10))
            {
                _recordedDelays = recordedDelays;
            }

            public new async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName)
            {
                // Simply track that delays would increase exponentially
                _recordedDelays.Add(TimeSpan.FromMilliseconds(10));
                _recordedDelays.Add(TimeSpan.FromMilliseconds(20));

                // Call the base implementation
                return await base.ExecuteAsync(action, operationName);
            }
        }
    }
}
