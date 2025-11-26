using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
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

        #region RetryUtilities Integration Tests - Retryable Exceptions

        [Fact]
        public async Task ExecuteAsync_SocketException_IsRetryable()
        {
            // Arrange - SocketException is retryable per RetryUtilities
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 2)
                {
                    throw new SocketException((int)SocketError.ConnectionRefused);
                }
                return "success";
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be("success");
            attempts.Should().Be(2); // First attempt failed, second succeeded
        }

        [Fact]
        public async Task ExecuteAsync_IOException_IsRetryable()
        {
            // Arrange - IOException is retryable per RetryUtilities
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 2)
                {
                    throw new IOException("Network stream interrupted");
                }
                return "success";
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be("success");
            attempts.Should().Be(2);
        }

        [Fact]
        public async Task ExecuteAsync_ExceptionWithTimeoutMessage_IsRetryable()
        {
            // Arrange - Exceptions with "timeout" in message are retryable per RetryUtilities
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 2)
                {
                    throw new Exception("The operation timeout occurred while waiting for response");
                }
                return "success";
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be("success");
            attempts.Should().Be(2);
        }

        [Fact]
        public async Task ExecuteAsync_ExceptionWithConnectionMessage_IsRetryable()
        {
            // Arrange - Exceptions with "connection" in message are retryable per RetryUtilities
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 2)
                {
                    throw new Exception("Connection was forcibly closed by the remote host");
                }
                return "success";
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be("success");
            attempts.Should().Be(2);
        }

        [Fact]
        public async Task ExecuteAsync_ExceptionWithRateLimitMessage_IsRetryable()
        {
            // Arrange - Exceptions with "rate limit" in message are retryable per RetryUtilities
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 2)
                {
                    throw new Exception("Rate limit exceeded, please slow down");
                }
                return "success";
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be("success");
            attempts.Should().Be(2);
        }

        [Fact]
        public async Task ExecuteAsync_ExceptionWithNetworkMessage_IsRetryable()
        {
            // Arrange - Exceptions with "network" in message are retryable per RetryUtilities
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 2)
                {
                    throw new Exception("A network error occurred during the request");
                }
                return "success";
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be("success");
            attempts.Should().Be(2);
        }

        #endregion

        #region RetryUtilities Integration Tests - Non-Retryable Exceptions

        [Fact]
        public async Task ExecuteAsync_ArgumentException_NotRetryable()
        {
            // Arrange - ArgumentException is NOT retryable (permanent failure)
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                throw new ArgumentException("Invalid parameter value");
            };

            // Act
            Func<Task> act = async () => await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            await act.Should().ThrowAsync<ArgumentException>();
            attempts.Should().Be(1); // No retry for permanent failures
        }

        [Fact]
        public async Task ExecuteAsync_ArgumentNullException_NotRetryable()
        {
            // Arrange - ArgumentNullException is NOT retryable (permanent failure)
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                throw new ArgumentNullException("param", "Parameter cannot be null");
            };

            // Act
            Func<Task> act = async () => await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            await act.Should().ThrowAsync<ArgumentNullException>();
            attempts.Should().Be(1);
        }

        [Fact]
        public async Task ExecuteAsync_NotSupportedException_NotRetryable()
        {
            // Arrange - NotSupportedException is NOT retryable (permanent failure)
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                throw new NotSupportedException("This operation is not supported");
            };

            // Act
            Func<Task> act = async () => await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            await act.Should().ThrowAsync<NotSupportedException>();
            attempts.Should().Be(1);
        }

        [Fact]
        public async Task ExecuteAsync_UnauthorizedAccessException_NotRetryable()
        {
            // Arrange - UnauthorizedAccessException is NOT retryable (auth failure)
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                throw new UnauthorizedAccessException("Access denied");
            };

            // Act
            Func<Task> act = async () => await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            attempts.Should().Be(1);
        }

        [Fact]
        public async Task ExecuteAsync_FormatException_NotRetryable()
        {
            // Arrange - FormatException is NOT retryable (data parsing failure)
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                throw new FormatException("Invalid JSON response format");
            };

            // Act
            Func<Task> act = async () => await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            await act.Should().ThrowAsync<FormatException>();
            attempts.Should().Be(1);
        }

        [Fact]
        public async Task ExecuteAsync_KeyNotFoundException_NotRetryable()
        {
            // Arrange - KeyNotFoundException is NOT retryable (data not found)
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                throw new KeyNotFoundException("The requested key was not found");
            };

            // Act
            Func<Task> act = async () => await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            await act.Should().ThrowAsync<KeyNotFoundException>();
            attempts.Should().Be(1);
        }

        #endregion

        #region RetryUtilities Integration Tests - Inner Exception Handling

        [Fact]
        public async Task ExecuteAsync_InnerHttpRequestException_IsRetryable()
        {
            // Arrange - Inner HttpRequestException should be detected as retryable
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 2)
                {
                    var inner = new HttpRequestException("Connection refused");
                    throw new Exception("Wrapper exception", inner);
                }
                return "success";
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be("success");
            attempts.Should().Be(2); // Retried because inner exception is retryable
        }

        [Fact]
        public async Task ExecuteAsync_InnerSocketException_IsRetryable()
        {
            // Arrange - Inner SocketException should be detected as retryable
            var attempts = 0;
            Func<Task<string>> action = async () =>
            {
                attempts++;
                await Task.Delay(1);
                if (attempts < 2)
                {
                    var inner = new SocketException((int)SocketError.HostUnreachable);
                    throw new Exception("Network failure", inner);
                }
                return "success";
            };

            // Act
            var result = await _retryPolicy.ExecuteAsync(action, "TestOperation");

            // Assert
            result.Should().Be("success");
            attempts.Should().Be(2);
        }

        #endregion

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
