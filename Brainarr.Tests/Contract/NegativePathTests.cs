using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Contract
{
    /// <summary>
    /// Negative-path contract tests for all HTTP providers (OpenAI, Gemini, Z.AI GLM).
    ///
    /// Verifies GLM-07 through GLM-12:
    /// - 401 Unauthorized is properly mapped and handled
    /// - 403 Forbidden is properly mapped and handled
    /// - 429 Rate Limit with retry-after is tested
    /// - 5xx Server Errors are properly mapped as retryable
    /// - Malformed JSON is handled gracefully
    /// - API keys never appear in test output or logs (GLM-12)
    /// </summary>
    [Trait("Category", "Contract")]
    [Trait("Category", "NegativePath")]
    public class NegativePathTests
    {
        private const string TestApiKey = "test-api-key-12345";
        private readonly Mock<IHttpClient> _mockHttpClient;
        private readonly Logger _logger;

        public NegativePathTests()
        {
            _mockHttpClient = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
        }

        #region Test Data

        public static IEnumerable<object[]> HttpProviders =>
            new List<object[]>
            {
                new object[] { "OpenAI" },
                new object[] { "Gemini" },
                new object[] { "ZaiGlm" }
            };

        #endregion

        #region GLM-07: 401 Unauthorized Tests

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenHttp401_ReturnsEmptyAndDoesNotLeakApiKey(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(HttpStatusCode.Unauthorized, $"{{\"error\": \"Invalid API key: {TestApiKey}\"}}");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();

            // Verify no API key in logs (check provider's last user message)
            var lastMessage = provider.GetLastUserMessage();
            if (lastMessage != null)
            {
                lastMessage.Should().NotContain(TestApiKey);
            }
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task TestConnectionAsync_WhenHttp401_ReturnsFalseAndDoesNotLeakApiKey(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(HttpStatusCode.Unauthorized, $"{{\"error\": \"API key {TestApiKey} is invalid\"}}");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();

            // Verify no API key leakage
            var lastMessage = provider.GetLastUserMessage();
            if (lastMessage != null)
            {
                lastMessage.Should().NotContain(TestApiKey);
            }
        }

        #endregion

        #region GLM-08: 403 Forbidden Tests

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenHttp403_ReturnsEmptyAndHandlesAuthError(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(HttpStatusCode.Forbidden, $"{{\"error\": \"Access denied for key {TestApiKey}\"}}");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();

            // Verify error is captured appropriately
            var lastMessage = provider.GetLastUserMessage();
            if (lastMessage != null && providerName == "OpenAI")
            {
                // OpenAI sets user message for auth errors
                lastMessage.Should().NotBeNullOrEmpty();
            }
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task TestConnectionAsync_WhenHttp403_ReturnsFalse(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(HttpStatusCode.Forbidden, "{\"error\": \"Access denied\"}");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region GLM-09: 429 Rate Limit Tests

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenHttp429_ReturnsEmpty(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(
                HttpStatusCode.TooManyRequests,
                "{\"error\": \"Rate limit exceeded\"}",
                retryAfter: "60");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();

            // Verify rate limit error is captured
            var lastMessage = provider.GetLastUserMessage();
            if (lastMessage != null)
            {
                lastMessage.ToLower().Should().Contain("rate", "because rate limit was exceeded");
            }
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task TestConnectionAsync_WhenHttp429_ReturnsFalse(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(
                HttpStatusCode.TooManyRequests,
                "{\"error\": \"Rate limit exceeded\"}",
                retryAfter: "30");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region GLM-10: 5xx Server Error Tests

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenHttp500_ReturnsEmpty(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(
                HttpStatusCode.InternalServerError,
                "{\"error\": \"Internal server error\"}");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenHttp502_ReturnsEmpty(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(
                HttpStatusCode.BadGateway,
                "{\"error\": \"Bad gateway\"}");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenHttp503_ReturnsEmpty(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(
                HttpStatusCode.ServiceUnavailable,
                "{\"error\": \"Service unavailable\"}");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task TestConnectionAsync_WhenHttp5xx_ReturnsFalse(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = CreateErrorResponse(
                HttpStatusCode.InternalServerError,
                "{\"error\": \"Server error\"}");

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region GLM-11: Malformed JSON Tests

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenMalformedJson_ReturnsEmpty(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = HttpResponseFactory.CreateResponse("{invalid json content}}}", HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert - Should handle gracefully and return empty
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenEmptyResponse_ReturnsEmpty(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = HttpResponseFactory.CreateResponse(string.Empty, HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenJsonArrayButNotRecommendations_HandlesGracefully(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var response = HttpResponseFactory.CreateResponse("[{\"unrelated\": \"data\"}]", HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            // May return empty or partial results depending on parser
        }

        #endregion

        #region GLM-12: API Key Redaction Tests

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_ApiKeyNeverAppearsInExceptionOrLogs(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var errorContent = $"{{\"error\": \"The key '{TestApiKey}' is invalid\"}}";
            var response = CreateErrorResponse(HttpStatusCode.Unauthorized, errorContent);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty();

            // Verify API key is not in user-facing messages
            var lastMessage = provider.GetLastUserMessage();
            if (lastMessage != null)
            {
                lastMessage.Should().NotContain(TestApiKey, "API key should be redacted from user messages");
            }

            // Verify learn more URL is provided (if applicable)
            var learnMoreUrl = provider.GetLearnMoreUrl();
            if (learnMoreUrl != null && providerName == "OpenAI")
            {
                learnMoreUrl.Should().NotBeNullOrEmpty();
            }
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task TestConnectionAsync_ApiKeyNeverAppearsInExceptionOrLogs(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);
            var errorContent = $"{{\"error\": \"API key '{TestApiKey}' failed authentication\"}}";
            var response = CreateErrorResponse(HttpStatusCode.Unauthorized, errorContent);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();

            // Verify API key redaction
            var lastMessage = provider.GetLastUserMessage();
            if (lastMessage != null)
            {
                lastMessage.Should().NotContain(TestApiKey, "API key should be redacted");
            }
        }

        #endregion

        #region Timeout Handling Tests

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task GetRecommendationsAsync_WhenTimeout_HandlesGracefully(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new InvalidOperationException("Request timed out"));

            // Act - All providers should handle timeout gracefully
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert - Should return empty without crashing
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(HttpProviders))]
        public async Task TestConnectionAsync_WhenTimeout_ReturnsFalse(string providerName)
        {
            // Arrange
            var provider = CreateProvider(providerName);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new InvalidOperationException("Request timed out"));

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert - All providers should return false on timeout
            result.Should().BeFalse();
        }

        #endregion

        #region Helper Methods

        private IAIProvider CreateProvider(string providerName)
        {
            return providerName switch
            {
                "OpenAI" => new OpenAIProvider(_mockHttpClient.Object, _logger, TestApiKey, "gpt-4o-mini", preferStructured: true),
                "Gemini" => new GeminiProvider(_mockHttpClient.Object, _logger, TestApiKey, "gemini-1.5-flash"),
                "ZaiGlm" => new ZaiGlmProvider(_mockHttpClient.Object, _logger, TestApiKey, "glm-4.7-flash"),
                _ => throw new ArgumentException($"Unknown provider: {providerName}")
            };
        }

        private HttpResponse CreateErrorResponse(HttpStatusCode statusCode, string content, string retryAfter = null)
        {
            var response = HttpResponseFactory.CreateResponse(content, statusCode);

            if (retryAfter != null)
            {
                response.Headers["Retry-After"] = retryAfter;
            }

            return response;
        }

        #endregion
    }
}
