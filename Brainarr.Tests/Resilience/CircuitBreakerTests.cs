using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Plugin.Services.Resilience;
using Moq;
using NLog;
using Xunit;

namespace Brainarr.Tests.Resilience
{
    public class CircuitBreakerTests
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly CircuitBreakerOptions _options;
        private readonly CircuitBreaker _circuitBreaker;

        public CircuitBreakerTests()
        {
            _mockLogger = new Mock<ILogger>();
            _options = new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                ResetTimeout = TimeSpan.FromSeconds(5),
                HalfOpenSuccessThreshold = 2
            };
            _circuitBreaker = new CircuitBreaker("TestResource", _options, _mockLogger.Object);
        }

        #region Basic Circuit Breaker Functionality

        [Fact]
        public async Task CircuitBreaker_ShouldStartInClosedState()
        {
            // Assert
            Assert.Equal(CircuitState.Closed, _circuitBreaker.State);
        }

        [Fact]
        public async Task CircuitBreaker_ShouldOpenAfterThresholdFailures()
        {
            // Arrange
            var failingOperation = new Func<Task<string>>(() => 
                throw new HttpRequestException("Server error"));

            // Act
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try
                {
                    await _circuitBreaker.ExecuteAsync(failingOperation);
                }
                catch { }
            }

            // Assert
            Assert.Equal(CircuitState.Open, _circuitBreaker.State);
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(
                () => _circuitBreaker.ExecuteAsync(failingOperation));
        }

        [Fact]
        public async Task CircuitBreaker_ShouldTransitionToHalfOpenAfterTimeout()
        {
            // Arrange - Trip the circuit
            var failingOperation = new Func<Task<string>>(() => 
                throw new HttpRequestException("Server error"));
            
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(failingOperation); }
                catch { }
            }
            
            Assert.Equal(CircuitState.Open, _circuitBreaker.State);

            // Act - Wait for reset timeout
            await Task.Delay(_options.ResetTimeout.Add(TimeSpan.FromMilliseconds(100)));
            
            var successOperation = new Func<Task<string>>(() => Task.FromResult("success"));
            await _circuitBreaker.ExecuteAsync(successOperation);

            // Assert
            Assert.Equal(CircuitState.HalfOpen, _circuitBreaker.State);
        }

        #endregion

        #region HTTP Status Code Handling

        [Theory]
        [InlineData(400, false)] // Bad Request - should not trip
        [InlineData(401, false)] // Unauthorized - should not trip
        [InlineData(403, false)] // Forbidden - should not trip
        [InlineData(404, false)] // Not Found - should not trip
        [InlineData(422, false)] // Unprocessable Entity - should not trip
        [InlineData(429, false)] // Too Many Requests - should not trip (handled by rate limiter)
        [InlineData(500, true)]  // Internal Server Error - should trip
        [InlineData(502, true)]  // Bad Gateway - should trip
        [InlineData(503, true)]  // Service Unavailable - should trip
        [InlineData(504, true)]  // Gateway Timeout - should trip
        public async Task CircuitBreaker_ShouldHandleHttpStatusCodesCorrectly(int statusCode, bool shouldTrip)
        {
            // Arrange
            var httpException = CreateHttpExceptionWithStatusCode((HttpStatusCode)statusCode);
            var failingOperation = new Func<Task<string>>(() => throw httpException);

            // Act
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try
                {
                    await _circuitBreaker.ExecuteAsync(failingOperation);
                }
                catch { }
            }

            // Assert
            if (shouldTrip)
            {
                Assert.Equal(CircuitState.Open, _circuitBreaker.State);
                _mockLogger.Verify(x => x.Warn(It.Is<string>(s => 
                    s.Contains("Server error"))), Times.AtLeastOnce);
            }
            else
            {
                Assert.Equal(CircuitState.Closed, _circuitBreaker.State);
                _mockLogger.Verify(x => x.Debug(It.Is<string>(s => 
                    s.Contains("Ignoring client error"))), Times.AtLeastOnce);
            }
        }

        [Fact]
        public async Task CircuitBreaker_ShouldExtractStatusCodeFromExceptionData()
        {
            // Arrange
            var exception = new Exception("Request failed");
            exception.Data["StatusCode"] = HttpStatusCode.BadRequest;
            
            var failingOperation = new Func<Task<string>>(() => throw exception);

            // Act
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(failingOperation); }
                catch { }
            }

            // Assert - Should not trip for 400 Bad Request
            Assert.Equal(CircuitState.Closed, _circuitBreaker.State);
        }

        [Theory]
        [InlineData("Error 404 Not Found", 404, false)]
        [InlineData("Failed with status (500)", 500, true)]
        [InlineData("Response: 503 Service Unavailable", 503, true)]
        [InlineData("HTTP 401 Unauthorized", 401, false)]
        public async Task CircuitBreaker_ShouldParseStatusCodeFromMessage(string message, int expectedCode, bool shouldTrip)
        {
            // Arrange
            var exception = new Exception(message);
            var failingOperation = new Func<Task<string>>(() => throw exception);

            // Act
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(failingOperation); }
                catch { }
            }

            // Assert
            Assert.Equal(shouldTrip ? CircuitState.Open : CircuitState.Closed, _circuitBreaker.State);
        }

        #endregion

        #region Exception Type Handling

        [Theory]
        [InlineData(typeof(TaskCanceledException), true)]
        [InlineData(typeof(TimeoutException), true)]
        [InlineData(typeof(HttpRequestException), true)]
        [InlineData(typeof(SocketException), true)]
        [InlineData(typeof(System.IO.IOException), true)]
        [InlineData(typeof(ArgumentException), false)]
        [InlineData(typeof(InvalidOperationException), false)]
        [InlineData(typeof(NullReferenceException), false)]
        public async Task CircuitBreaker_ShouldHandleSpecificExceptionTypes(Type exceptionType, bool shouldHandle)
        {
            // Arrange
            var exception = (Exception)Activator.CreateInstance(exceptionType, "Test error");
            var failingOperation = new Func<Task<string>>(() => throw exception);

            // Act
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(failingOperation); }
                catch { }
            }

            // Assert
            Assert.Equal(shouldHandle ? CircuitState.Open : CircuitState.Closed, _circuitBreaker.State);
        }

        [Fact]
        public async Task CircuitBreaker_ShouldHandleAggregateExceptions()
        {
            // Arrange
            var innerExceptions = new List<Exception>
            {
                new HttpRequestException("Connection failed"),
                new TimeoutException("Request timeout"),
                new ArgumentException("Invalid argument") // This one shouldn't trip
            };
            
            var aggregateException = new AggregateException(innerExceptions);
            var failingOperation = new Func<Task<string>>(() => throw aggregateException);

            // Act
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(failingOperation); }
                catch { }
            }

            // Assert - Should trip because of HttpRequestException and TimeoutException
            Assert.Equal(CircuitState.Open, _circuitBreaker.State);
        }

        [Fact]
        public async Task CircuitBreaker_ShouldCheckInnerExceptions()
        {
            // Arrange
            var innerException = new HttpRequestException("Network error");
            var outerException = new InvalidOperationException("Operation failed", innerException);
            var failingOperation = new Func<Task<string>>(() => throw outerException);

            // Act
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(failingOperation); }
                catch { }
            }

            // Assert - Should trip because of inner HttpRequestException
            Assert.Equal(CircuitState.Open, _circuitBreaker.State);
        }

        #endregion

        #region Fallback Functionality

        [Fact]
        public async Task ExecuteWithFallback_ShouldReturnFallbackWhenOpen()
        {
            // Arrange - Trip the circuit
            var failingOperation = new Func<Task<string>>(() => 
                throw new HttpRequestException("Server error"));
            
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(failingOperation); }
                catch { }
            }

            // Act
            var result = await _circuitBreaker.ExecuteWithFallbackAsync(
                failingOperation, 
                "fallback value");

            // Assert
            Assert.Equal("fallback value", result);
            _mockLogger.Verify(x => x.Warn(It.Is<string>(s => 
                s.Contains("using fallback value"))), Times.Once);
        }

        #endregion

        #region Half-Open State Management

        [Fact]
        public async Task HalfOpen_ShouldCloseAfterSuccessThreshold()
        {
            // Arrange - Trip the circuit
            var failCount = 0;
            var operation = new Func<Task<string>>(() =>
            {
                if (failCount++ < _options.FailureThreshold)
                    throw new HttpRequestException("Server error");
                return Task.FromResult("success");
            });

            // Trip circuit
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(operation); }
                catch { }
            }
            
            Assert.Equal(CircuitState.Open, _circuitBreaker.State);

            // Wait for timeout to transition to half-open
            await Task.Delay(_options.ResetTimeout.Add(TimeSpan.FromMilliseconds(100)));

            // Act - Execute successful operations in half-open state
            for (int i = 0; i < _options.HalfOpenSuccessThreshold; i++)
            {
                await _circuitBreaker.ExecuteAsync(operation);
            }

            // Assert
            Assert.Equal(CircuitState.Closed, _circuitBreaker.State);
            _mockLogger.Verify(x => x.Info(It.Is<string>(s => 
                s.Contains("Circuit breaker CLOSED"))), Times.Once);
        }

        [Fact]
        public async Task HalfOpen_ShouldReopenOnFailure()
        {
            // Arrange - Trip the circuit
            var attemptCount = 0;
            var operation = new Func<Task<string>>(() =>
            {
                attemptCount++;
                if (attemptCount <= _options.FailureThreshold || attemptCount == _options.FailureThreshold + 2)
                    throw new HttpRequestException("Server error");
                return Task.FromResult("success");
            });

            // Trip circuit
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(operation); }
                catch { }
            }

            // Wait for timeout
            await Task.Delay(_options.ResetTimeout.Add(TimeSpan.FromMilliseconds(100)));

            // Act - One success then failure in half-open
            await _circuitBreaker.ExecuteAsync(operation); // Success
            
            try { await _circuitBreaker.ExecuteAsync(operation); } // Failure
            catch { }

            // Assert
            Assert.Equal(CircuitState.Open, _circuitBreaker.State);
        }

        #endregion

        #region Statistics and Monitoring

        [Fact]
        public void GetStatistics_ShouldReturnAccurateMetrics()
        {
            // Act
            var stats = _circuitBreaker.GetStatistics();

            // Assert
            Assert.NotNull(stats);
            Assert.Equal("TestResource", stats.ResourceName);
            Assert.Equal(CircuitState.Closed, stats.State);
            Assert.Equal(0, stats.ConsecutiveFailures);
            Assert.Equal(0.0, stats.FailureRate);
        }

        [Fact]
        public async Task CircuitBreaker_ShouldRaiseEvents()
        {
            // Arrange
            var openedEventRaised = false;
            var closedEventRaised = false;
            
            _circuitBreaker.CircuitOpened += (sender, args) => openedEventRaised = true;
            _circuitBreaker.CircuitClosed += (sender, args) => closedEventRaised = true;

            var failCount = 0;
            var operation = new Func<Task<string>>(() =>
            {
                if (failCount++ < _options.FailureThreshold)
                    throw new HttpRequestException("Server error");
                return Task.FromResult("success");
            });

            // Act - Trip circuit
            for (int i = 0; i < _options.FailureThreshold; i++)
            {
                try { await _circuitBreaker.ExecuteAsync(operation); }
                catch { }
            }

            Assert.True(openedEventRaised);

            // Reset and close
            _circuitBreaker.Reset();

            // Assert
            Assert.True(closedEventRaised);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task CircuitBreaker_ShouldHandleConcurrentRequests()
        {
            // Arrange
            var failingOperation = new Func<Task<string>>(() => 
                throw new HttpRequestException("Server error"));
            
            var tasks = new List<Task>();

            // Act - Send concurrent failing requests
            for (int i = 0; i < _options.FailureThreshold * 2; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try { await _circuitBreaker.ExecuteAsync(failingOperation); }
                    catch { }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - Circuit should be open
            Assert.Equal(CircuitState.Open, _circuitBreaker.State);
        }

        [Fact]
        public void Reset_ShouldClearStateAndMetrics()
        {
            // Arrange - Create some failure history
            var operation = new Func<Task<string>>(() => 
                throw new HttpRequestException("Server error"));
            
            for (int i = 0; i < 2; i++)
            {
                try { _circuitBreaker.ExecuteAsync(operation).Wait(); }
                catch { }
            }

            // Act
            _circuitBreaker.Reset();

            // Assert
            Assert.Equal(CircuitState.Closed, _circuitBreaker.State);
            Assert.Equal(0, _circuitBreaker.ConsecutiveFailures);
            Assert.Equal(0.0, _circuitBreaker.FailureRate);
        }

        #endregion

        #region Helper Methods

        private HttpRequestException CreateHttpExceptionWithStatusCode(HttpStatusCode statusCode)
        {
            // In .NET 5+, HttpRequestException has a StatusCode property
            // For testing, we'll simulate this with exception data
            var exception = new HttpRequestException($"Request failed with {statusCode}");
            exception.Data["StatusCode"] = statusCode;
            return exception;
        }

        #endregion
    }
}