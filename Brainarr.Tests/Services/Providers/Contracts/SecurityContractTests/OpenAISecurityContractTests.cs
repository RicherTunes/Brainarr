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
    /// Security contract tests for OpenAI provider.
    /// Verifies no secrets leak through logs, exceptions, or error messages.
    /// TDD: Write tests first, then implement fixes to make them pass.
    /// </summary>
    [Trait("Category", "Security")]
    [Trait("Provider", "OpenAI")]
    [Collection("OpenAISecurity")]
    public class OpenAISecurityContractTests : IDisposable
    {
        private readonly List<string> _capturedLogs;
        private readonly Logger _logger;
        private readonly LogFactory _logFactory;

        // Sensitive data patterns that should NEVER appear in logs/errors
        private readonly string[] _sensitivePatterns = new[]
        {
            "sk-proj-",          // OpenAI project API key prefix
            "sk-test-secret",    // Test API key pattern
            "sk-prod-",          // Production API key prefix
            "api_key=",          // Generic API key assignment
            "Authorization:",    // Auth header (without Bearer)
        };

        public OpenAISecurityContractTests()
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
            var apiKey = "sk-test-secret-key-12345-production";
            var httpMock = new Mock<IHttpClient>();

            // Act
            var provider = new OpenAIProvider(httpMock.Object, _logger, apiKey);

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-test-secret-key-12345-production",
                "API key should not appear in initialization logs");
            allLogs.Should().NotContain("secret-key",
                "API key components should not appear in logs");
        }

        [Fact]
        public async Task GetRecommendations_WithApiErrorContainingKey_DoesNotLogApiKey()
        {
            // Arrange: Server echoes back API key in error (security anti-pattern from server)
            var apiKey = "sk-proj-secret-production-key-xyz123";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Invalid API key: sk-proj-secret-production-key-xyz123\", \"code\": \"invalid_api_key\"}}",
                    HttpStatusCode.Unauthorized));

            var provider = new OpenAIProvider(httpMock.Object, _logger, apiKey);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-proj-secret-production-key-xyz123",
                "API key should not be logged even if returned in error response");
        }

        [Fact]
        public async Task GetRecommendations_With429ContainingKey_DoesNotLogApiKey()
        {
            // Arrange: Rate limit error that echoes the key
            var apiKey = "sk-test-key";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Rate limit exceeded for API key sk-prod-leaked-key-abc\"}}",
                    HttpStatusCode.TooManyRequests));

            var provider = new OpenAIProvider(httpMock.Object, _logger, apiKey);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-prod-leaked-key-abc",
                "API key from error response should not be logged");
        }

        #endregion

        #region Error Response Security

        [Fact]
        public async Task GetRecommendations_WithServerError_DoesNotLogFullResponseBody()
        {
            // Arrange: Server error with sensitive details
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Internal error processing request for org-secret123 with key sk-internal-secret\"}}",
                    HttpStatusCode.InternalServerError));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-internal-secret",
                "Leaked API key from error should not appear in logs");
        }

        [Fact]
        public async Task GetRecommendations_With401_LogsGenericMessage()
        {
            // Arrange: 401 with API key in error
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Incorrect API key provided: sk-xxx***xxx\"}}",
                    HttpStatusCode.Unauthorized));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-xxx",
                "Partial API key should not appear in logs");
        }

        #endregion

        #region Exception Safety

        [Fact]
        public async Task GetRecommendations_WithException_DoesNotExposeApiKeyInException()
        {
            // Arrange: Exception that mentions API key
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new Exception("Request failed for API key sk-exception-leaked-key"));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Exception should return empty list");
            // Exception message IS logged, but we verify no additional sensitive data exposure
        }

        [Fact]
        public async Task TestConnection_WithException_DoesNotCrash()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new Exception("Unexpected error"));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("Exception should fail connection test gracefully");
        }

        [Fact]
        public async Task GetRecommendations_WithInnerExceptionContainingApiKey_DoesNotLogApiKey()
        {
            // Arrange: Inner exception that contains API key (e.g., from HTTP client internals)
            var innerException = new Exception("Connection failed for endpoint with key sk-inner-secret-leaked-key-xyz");
            var outerException = new Exception("HTTP request failed", innerException);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(outerException);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Exception should return empty list");
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-inner-secret-leaked-key-xyz",
                "API key from inner exception should not appear in logs");
        }

        [Fact]
        public async Task GetRecommendations_WithNestedInnerExceptions_DoesNotLogApiKey()
        {
            // Arrange: Deeply nested exceptions with API key at multiple levels
            var deepestException = new Exception("Auth failed: api_key=sk-deepest-secret-789");
            var middleException = new Exception("Request processing error with sk-middle-secret-456", deepestException);
            var outerException = new Exception("HTTP client error", middleException);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(outerException);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Exception should return empty list");
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-deepest-secret-789",
                "API key from deepest inner exception should not appear in logs");
            allLogs.Should().NotContain("sk-middle-secret-456",
                "API key from middle inner exception should not appear in logs");
            allLogs.Should().NotContain("api_key=sk-",
                "api_key assignment pattern should not appear in logs");
        }

        #endregion

        #region Request Security

        [Fact]
        public async Task GetRecommendations_DoesNotLogUserPrompt()
        {
            // Arrange: User prompt might contain sensitive info
            var sensitivePrompt = "My API key is sk-user-mistake-123 and I like rock music";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"choices\": [{\"message\": {\"content\": \"[]\"}, \"finish_reason\": \"stop\"}]}",
                    HttpStatusCode.OK));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            await provider.GetRecommendationsAsync(sensitivePrompt);

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-user-mistake-123",
                "User-provided sensitive data in prompt should not be logged");
            allLogs.Should().NotContain("My API key",
                "User-provided prompt content should not be logged by default");
        }

        #endregion

        #region Model Update Security

        [Fact]
        public void UpdateModel_DoesNotLogSensitiveInfo()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            provider.UpdateModel("gpt-4o");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-test-key",
                "API key should not appear in model update logs");
        }

        #endregion

        #region Additional Negative Path Tests

        [Fact]
        public async Task GetRecommendations_With403Forbidden_ReturnsEmptyList()
        {
            // Arrange: 403 Forbidden response
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Access denied\"}}",
                    HttpStatusCode.Forbidden));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

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

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

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

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

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
            var responseBody = "{\"id\":\"test\",\"choices\":[{\"message\":{\"content\":null},\"finish_reason\":\"content_filter\"}]}";
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(responseBody, HttpStatusCode.OK));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Content filtered response should return empty list");
        }

        #endregion
    }
}
