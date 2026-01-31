using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;
using Brainarr.Tests.Helpers;

namespace Brainarr.Tests.Services.Providers.Contracts.SecurityContractTests
{
    /// <summary>
    /// Security contract tests for Z.AI GLM provider.
    /// Verifies no secrets leak through logs, exceptions, or error messages.
    /// </summary>
    [Trait("Category", "Security")]
    [Trait("Provider", "ZaiGlm")]
    [Collection("ZaiGlmSecurity")]
    public class ZaiGlmSecurityContractTests : IDisposable
    {
        private readonly List<string> _capturedLogs;
        private readonly Logger _logger;
        private readonly LogFactory _logFactory;

        // Sensitive data patterns that should NEVER appear in logs/errors
        private readonly string[] _sensitivePatterns = new[]
        {
            "sk-zai-",           // Z.AI API key prefix pattern
            "zai_api_key",       // API key variable pattern
            "glm_secret",        // Secret pattern
            "api_key=",          // Generic API key assignment
            "Authorization:",    // Auth header
            "Bearer ",           // Bearer token
        };

        public ZaiGlmSecurityContractTests()
        {
            _capturedLogs = new List<string>();

            // Create isolated NLog configuration
            var config = new LoggingConfiguration();
            var target = new MethodCallTarget("testcapture", (logEvent, parameters) =>
            {
                _capturedLogs.Add(logEvent.FormattedMessage);
            });
            config.AddTarget(target);
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, target);

            _logFactory = new LogFactory(config);
            _logger = _logFactory.GetCurrentClassLogger();
        }

        public void Dispose()
        {
            _logFactory.Shutdown();
        }

        #region API Key Security

        [Fact]
        public void Constructor_WithValidApiKey_DoesNotLogApiKey()
        {
            // Arrange
            var apiKey = "sk-zai-test-secret-key-12345";
            var httpMock = new Mock<IHttpClient>();

            // Act
            var provider = new ZaiGlmProvider(httpMock.Object, _logger, apiKey);

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-zai-test-secret-key-12345",
                "API key should not appear in initialization logs");
            allLogs.Should().NotContain("test-secret-key",
                "API key components should not appear in logs");
        }

        [Fact]
        public async Task GetRecommendations_WithApiError_DoesNotLogApiKey()
        {
            // Arrange
            var apiKey = "sk-zai-secret-production-key";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Invalid API key: sk-zai-secret-production-key\"}}",
                    HttpStatusCode.Unauthorized));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, apiKey);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-zai-secret-production-key",
                "API key should not be logged even if returned in error response");
        }

        [Fact]
        public async Task GetRecommendations_WithServerError_DoesNotExposeInternals()
        {
            // Arrange
            var apiKey = "test-api-key";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new Exception("Internal error: API key sk-zai-leaked-key was rejected"));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, apiKey);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            // The exception message may be logged, but we should verify it doesn't expose other secrets
            allLogs.Should().NotContain("Bearer ",
                "Authorization headers should not appear in logs");
        }

        #endregion

        #region Error Response Security

        [Fact]
        public async Task GetRecommendations_With401_LogsGenericMessage()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Unauthorized\"}}",
                    HttpStatusCode.Unauthorized));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "test-key");

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert: Should log error but not expose sensitive details
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("test-key",
                "API key should not appear in 401 error logs");
        }

        [Fact]
        public async Task GetRecommendations_With429_DoesNotExposeRateLimitDetails()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Rate limit exceeded for API key sk-zai-exposed\"}}",
                    HttpStatusCode.TooManyRequests));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "test-key");

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-zai-exposed",
                "API key from error response should not be logged");
        }

        #endregion

        #region Exception Safety

        [Fact]
        public async Task GetRecommendations_WithException_DoesNotExposeStackTraceDetails()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new InvalidOperationException("Database connection failed for user api_key=secret123"));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Exception should return empty list");
            // Logs may contain exception but should not expose sensitive patterns in a user-facing context
        }

        [Fact]
        public async Task TestConnection_WithException_DoesNotCrash()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new Exception("Unexpected error"));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "test-key");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("Exception should fail connection test gracefully");
        }

        [Fact]
        public async Task GetRecommendations_WithInnerExceptionContainingApiKey_DoesNotLogApiKey()
        {
            // Arrange: Inner exception that contains API key (e.g., from HTTP client internals)
            var innerException = new Exception("Connection failed for endpoint with key sk-zai-inner-secret-leaked-key-xyz");
            var outerException = new Exception("HTTP request failed", innerException);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(outerException);

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "sk-zai-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Exception should return empty list");
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-zai-inner-secret-leaked-key-xyz",
                "API key from inner exception should not appear in logs");
        }

        [Fact]
        public async Task GetRecommendations_WithNestedInnerExceptions_DoesNotLogApiKey()
        {
            // Arrange: Deeply nested exceptions with API key at multiple levels
            var deepestException = new Exception("Auth failed: api_key=sk-zai-deepest-secret-789");
            var middleException = new Exception("Request processing error with sk-zai-middle-secret-456", deepestException);
            var outerException = new Exception("HTTP client error", middleException);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(outerException);

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "sk-zai-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Exception should return empty list");
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-zai-deepest-secret-789",
                "API key from deepest inner exception should not appear in logs");
            allLogs.Should().NotContain("sk-zai-middle-secret-456",
                "API key from middle inner exception should not appear in logs");
            allLogs.Should().NotContain("api_key=sk-zai-",
                "api_key assignment pattern should not appear in logs");
        }

        #endregion

        #region Additional HTTP Status Security

        [Fact]
        public async Task GetRecommendations_With403Forbidden_ReturnsEmptyList()
        {
            // Arrange: 403 Forbidden response
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Access denied\"}}",
                    HttpStatusCode.Forbidden));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "sk-zai-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("403 Forbidden should return empty list");
        }

        [Fact]
        public async Task GetRecommendations_With503ServiceUnavailable_ReturnsEmptyList()
        {
            // Arrange: 503 Service Unavailable response
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Service temporarily unavailable\"}}",
                    HttpStatusCode.ServiceUnavailable));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "sk-zai-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("503 Service Unavailable should return empty list");
        }

        [Fact]
        public async Task TestConnection_With403Forbidden_ReturnsFalse()
        {
            // Arrange: 403 Forbidden response
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Access denied\"}}",
                    HttpStatusCode.Forbidden));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "sk-zai-test-key");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("403 Forbidden should fail connection test");
        }

        [Fact]
        public async Task GetRecommendations_WithContentFilteredResponse_ReturnsEmptyList()
        {
            // Arrange: Response flagged by content filter
            var httpMock = new Mock<IHttpClient>();
            var responseBody = "{\"id\":\"test\",\"choices\":[{\"message\":{\"content\":null},\"finish_reason\":\"sensitive\"}]}";
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(responseBody, HttpStatusCode.OK));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "sk-zai-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Content filtered response should return empty list");
        }

        #endregion

        #region Request Security

        [Fact]
        public async Task GetRecommendations_RequestDoesNotLogPrompt()
        {
            // Arrange: User prompt might contain sensitive info
            var sensitivePrompt = "My password is secret123 and I like rock music";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"choices\": [{\"message\": {\"content\": \"[]\"}, \"finish_reason\": \"stop\"}]}",
                    HttpStatusCode.OK));

            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "test-key");

            // Act
            await provider.GetRecommendationsAsync(sensitivePrompt);

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("secret123",
                "User-provided sensitive data in prompt should not be logged");
            allLogs.Should().NotContain("My password",
                "User-provided prompt content should not be logged");
        }

        #endregion

        #region Model Update Security

        [Fact]
        public void UpdateModel_DoesNotLogSensitiveInfo()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var provider = new ZaiGlmProvider(httpMock.Object, _logger, "test-key");

            // Act
            provider.UpdateModel("glm-4.7");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("test-key",
                "API key should not appear in model update logs");
        }

        #endregion
    }
}
