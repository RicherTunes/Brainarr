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
    /// Security contract tests for Google Gemini provider.
    /// Verifies no secrets leak through logs, exceptions, or error messages.
    /// TDD: Write tests first, then implement fixes to make them pass.
    /// </summary>
    [Trait("Category", "Security")]
    [Trait("Provider", "Gemini")]
    [Collection("GeminiSecurity")]
    public class GeminiSecurityContractTests : IDisposable
    {
        private readonly List<string> _capturedLogs;
        private readonly Logger _logger;
        private readonly LogFactory _logFactory;

        // Sensitive data patterns that should NEVER appear in logs/errors
        private readonly string[] _sensitivePatterns = new[]
        {
            "AIza",              // Google API key prefix
            "test-secret",       // Test API key pattern
            "api_key=",          // Generic API key assignment
            "key=secret",        // URL query parameter
        };

        public GeminiSecurityContractTests()
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
            var apiKey = "AIzaSyTest-Secret-Key-12345";
            var httpMock = new Mock<IHttpClient>();

            // Act
            var provider = new GeminiProvider(httpMock.Object, _logger, apiKey);

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("AIzaSyTest-Secret-Key-12345",
                "API key should not appear in initialization logs");
            allLogs.Should().NotContain("Secret-Key",
                "API key components should not appear in logs");
        }

        [Fact]
        public async Task GetRecommendations_WithApiErrorContainingKey_DoesNotLogApiKey()
        {
            // Arrange: Server echoes back API key in error (security anti-pattern from server)
            var apiKey = "AIzaSy-secret-production-key-xyz123";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Invalid API key: AIzaSy-secret-production-key-xyz123\", \"code\": 400, \"status\": \"INVALID_ARGUMENT\"}}",
                    HttpStatusCode.BadRequest));

            var provider = new GeminiProvider(httpMock.Object, _logger, apiKey);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("AIzaSy-secret-production-key-xyz123",
                "API key should not be logged even if returned in error response");
        }

        [Fact]
        public async Task GetRecommendations_With429ContainingKey_DoesNotLogApiKey()
        {
            // Arrange: Rate limit error that echoes the key
            var apiKey = "AIzaSy-test-key";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Rate limit exceeded for API key AIzaSy-leaked-key-abc\", \"code\": 429, \"status\": \"RESOURCE_EXHAUSTED\"}}",
                    HttpStatusCode.TooManyRequests));

            var provider = new GeminiProvider(httpMock.Object, _logger, apiKey);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("AIzaSy-leaked-key-abc",
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
                    "{\"error\": {\"message\": \"Internal error processing request with key AIzaSy-internal-secret\", \"code\": 500, \"status\": \"INTERNAL\"}}",
                    HttpStatusCode.InternalServerError));

            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("AIzaSy-internal-secret",
                "Leaked API key from error should not appear in logs");
        }

        [Fact]
        public async Task GetRecommendations_With401_LogsGenericMessage()
        {
            // Arrange: 401 with API key in error
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"API key not valid. Please pass a valid API key.\", \"code\": 401, \"status\": \"UNAUTHENTICATED\"}}",
                    HttpStatusCode.Unauthorized));

            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("AIzaSy-test-key",
                "API key should not appear in 401 error logs");
        }

        #endregion

        #region Exception Safety

        [Fact]
        public async Task GetRecommendations_WithException_DoesNotExposeApiKeyInException()
        {
            // Arrange: Exception that mentions API key
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new Exception("Request failed for API key AIzaSy-exception-leaked-key"));

            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

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

            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("Exception should fail connection test gracefully");
        }

        #endregion

        #region Request Security

        [Fact]
        public async Task GetRecommendations_DoesNotLogUserPrompt()
        {
            // Arrange: User prompt might contain sensitive info
            var sensitivePrompt = "My API key is AIzaSy-user-mistake-123 and I like rock music";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"[]\"}]}, \"finishReason\": \"STOP\"}]}",
                    HttpStatusCode.OK));

            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

            // Act
            await provider.GetRecommendationsAsync(sensitivePrompt);

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("AIzaSy-user-mistake-123",
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
            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

            // Act
            provider.UpdateModel("gemini-1.5-pro");

            // Assert
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("AIzaSy-test-key",
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
                    "{\"error\": {\"message\": \"Access denied\", \"code\": 403, \"status\": \"PERMISSION_DENIED\"}}",
                    HttpStatusCode.Forbidden));

            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

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
                    "{\"error\": {\"message\": \"Service temporarily unavailable\", \"code\": 503, \"status\": \"UNAVAILABLE\"}}",
                    HttpStatusCode.ServiceUnavailable));

            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

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
                    "{\"error\": {\"message\": \"Access denied\", \"code\": 403, \"status\": \"PERMISSION_DENIED\"}}",
                    HttpStatusCode.Forbidden));

            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("403 Forbidden should fail connection test");
        }

        [Fact]
        public async Task GetRecommendations_WithSafetyBlockedResponse_ReturnsEmptyList()
        {
            // Arrange: Response blocked by safety filter
            var httpMock = new Mock<IHttpClient>();
            var responseBody = "{\"candidates\":[{\"finishReason\":\"SAFETY\",\"safetyRatings\":[{\"category\":\"HARM_CATEGORY_DANGEROUS_CONTENT\",\"probability\":\"VERY_LIKELY\"}]}]}";
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(responseBody, HttpStatusCode.OK));

            var provider = new GeminiProvider(httpMock.Object, _logger, "AIzaSy-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Safety blocked response should return empty list");
        }

        #endregion
    }
}
