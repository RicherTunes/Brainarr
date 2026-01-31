using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Targets;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Contracts
{
    /// <summary>
    /// Conformance tests for providers inheriting from HttpChatProviderBase.
    /// These tests verify that all providers follow the contract correctly:
    /// - Header correctness (Authorization format, Content-Type)
    /// - Request body shape (required fields: model, messages, max_tokens, temperature)
    /// - Log redaction (API keys never appear in logs)
    /// - Exception safety (API keys never appear in exception messages)
    /// - Error mapping parity (consistent behavior across 401/403/429/5xx)
    ///
    /// ## When to Add New Providers to This Suite
    ///
    /// When consolidating a provider onto HttpChatProviderBase, add it to
    /// the theory data in each test to ensure conformance.
    ///
    /// ## Tech Debt Note
    ///
    /// If a provider needs a custom Authorization header format (e.g., "X-API-Key"
    /// instead of "Bearer"), update the theory data with the expected format.
    /// </summary>
    [Trait("Category", "Contract")]
    [Trait("Category", "Conformance")]
    public class HttpChatProviderBaseConformanceTests
    {
        /// <summary>
        /// Theory data: Provider type, factory lambda, expected auth header format, API key value
        /// </summary>
        public static IEnumerable<object[]> HttpChatProviders()
        {
            const string testApiKey = "test-api-key-12345";
            var logger = Helpers.TestLogger.CreateNullLogger();

            // Each tuple: (ProviderName, Factory, ExpectedAuthFormat, ApiKey)
            // ExpectedAuthFormat: "Bearer" for standard OAuth, or custom format
            yield return new object[]
            {
                "DeepSeek",
                (Func<Mock<IHttpClient>, Logger, IAIProvider>)((mock, log) =>
                    new DeepSeekProvider(mock.Object, log, testApiKey)),
                "Bearer",
                testApiKey
            };

            yield return new object[]
            {
                "Groq",
                (Func<Mock<IHttpClient>, Logger, IAIProvider>)((mock, log) =>
                    new GroqProvider(mock.Object, log, testApiKey)),
                "Bearer",
                testApiKey
            };

            yield return new object[]
            {
                "Perplexity",
                (Func<Mock<IHttpClient>, Logger, IAIProvider>)((mock, log) =>
                    new PerplexityProvider(mock.Object, log, testApiKey)),
                "Bearer",
                testApiKey
            };

            yield return new object[]
            {
                "OpenRouter",
                (Func<Mock<IHttpClient>, Logger, IAIProvider>)((mock, log) =>
                    new OpenRouterProvider(mock.Object, log, testApiKey)),
                "Bearer",
                testApiKey
            };
        }

        #region Header Correctness Tests

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_SetsAuthorizationHeader_WithCorrectFormat(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string expectedAuthFormat,
            string _)
        {
            // Arrange
            HttpRequest capturedRequest = null;
            var httpMock = new Mock<IHttpClient>();

            // Return a valid response to prevent retries
            var response = Helpers.HttpResponseFactory.CreateResponse(
                "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}",
                HttpStatusCode.OK);

            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => capturedRequest = req)
                .ReturnsAsync(response);

            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            capturedRequest.Should().NotBeNull($"{providerName} should make an HTTP request");
            capturedRequest.Headers.Should().NotBeNull();

            // Verify Authorization header exists and has correct format
            var authHeader = capturedRequest.Headers.GetSingleValue("Authorization");
            authHeader.Should().NotBeNullOrEmpty($"{providerName} should set Authorization header");
            authHeader.Should().StartWith($"{expectedAuthFormat} ",
                $"{providerName} should use {expectedAuthFormat} authorization format");
        }

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_SetsContentTypeHeader_ToApplicationJson(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string __)
        {
            // Arrange
            HttpRequest capturedRequest = null;
            var httpMock = new Mock<IHttpClient>();

            var response = Helpers.HttpResponseFactory.CreateResponse(
                "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}",
                HttpStatusCode.OK);

            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => capturedRequest = req)
                .ReturnsAsync(response);

            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            capturedRequest.Should().NotBeNull();
            var contentType = capturedRequest.Headers.GetSingleValue("Content-Type");
            contentType.Should().Contain("application/json",
                $"{providerName} should set Content-Type to application/json");
        }

        #endregion

        #region Request Body Shape Tests

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_RequestBodyContains_RequiredFields(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string __)
        {
            // Arrange
            string capturedBody = null;
            var httpMock = new Mock<IHttpClient>();

            var response = Helpers.HttpResponseFactory.CreateResponse(
                "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}",
                HttpStatusCode.OK);

            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => capturedBody = req.GetContent())
                .ReturnsAsync(response);

            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            capturedBody.Should().NotBeNullOrEmpty($"{providerName} should send a request body");

            // All OpenAI-compatible providers should have these fields
            capturedBody.Should().Contain("\"model\"",
                $"{providerName} request body should contain model field");
            capturedBody.Should().Contain("\"messages\"",
                $"{providerName} request body should contain messages field");
            capturedBody.Should().Contain("\"max_tokens\"",
                $"{providerName} request body should contain max_tokens field");
            capturedBody.Should().Contain("\"temperature\"",
                $"{providerName} request body should contain temperature field");
        }

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_RequestBodyContains_SystemAndUserMessages(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string __)
        {
            // Arrange
            string capturedBody = null;
            var httpMock = new Mock<IHttpClient>();

            var response = Helpers.HttpResponseFactory.CreateResponse(
                "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}",
                HttpStatusCode.OK);

            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => capturedBody = req.GetContent())
                .ReturnsAsync(response);

            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            capturedBody.Should().NotBeNullOrEmpty();

            // Should have both system and user roles
            capturedBody.Should().Contain("\"role\":\"system\"",
                $"{providerName} should include system message");
            capturedBody.Should().Contain("\"role\":\"user\"",
                $"{providerName} should include user message");
        }

        #endregion

        #region Log Redaction Tests

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_DoesNotLogApiKey_OnError(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string apiKey)
        {
            // Arrange
            var memoryTarget = new MemoryTarget { Name = $"memory-{Guid.NewGuid()}" };
            var config = new NLog.Config.LoggingConfiguration();
            config.AddTarget(memoryTarget);
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, memoryTarget);

            var logFactory = new LogFactory(config);
            var logger = logFactory.GetLogger(providerName);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new Exception("Connection failed"));

            var provider = factory(httpMock, logger);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert - API key should never appear in logs
            foreach (var log in memoryTarget.Logs)
            {
                log.Should().NotContain(apiKey,
                    $"{providerName} should not log the API key. Log entry: {log}");
            }

            logFactory.Shutdown();
        }

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_DoesNotLogApiKey_OnSuccess(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string apiKey)
        {
            // Arrange
            var memoryTarget = new MemoryTarget { Name = $"memory-{Guid.NewGuid()}" };
            var config = new NLog.Config.LoggingConfiguration();
            config.AddTarget(memoryTarget);
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, memoryTarget);

            var logFactory = new LogFactory(config);
            var logger = logFactory.GetLogger(providerName);

            var httpMock = new Mock<IHttpClient>();
            var response = Helpers.HttpResponseFactory.CreateResponse(
                "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}",
                HttpStatusCode.OK);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = factory(httpMock, logger);

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert - API key should never appear in logs
            foreach (var log in memoryTarget.Logs)
            {
                log.Should().NotContain(apiKey,
                    $"{providerName} should not log the API key on success. Log entry: {log}");
            }

            logFactory.Shutdown();
        }

        #endregion

        #region Exception Safety Tests

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_DoesNotExposeApiKey_InExceptions(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string apiKey)
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();

            // Simulate an error that might include request details
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new InvalidOperationException("Request failed with headers"));

            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            Exception caughtException = null;
            try
            {
                await provider.GetRecommendationsAsync("Test prompt");
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert - Even if an exception bubbles up, it shouldn't contain the API key
            // Note: The base class should catch exceptions, so we expect no exception
            // But if one does escape, verify it doesn't leak credentials
            if (caughtException != null)
            {
                var fullExceptionText = caughtException.ToString();
                fullExceptionText.Should().NotContain(apiKey,
                    $"{providerName} exception should not expose API key");
            }
        }

        #endregion

        #region Error Mapping Parity Tests

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_Returns_EmptyList_On401(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string __)
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateUnauthorizedMock();
            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty($"{providerName} should return empty list on 401");
        }

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_Returns_EmptyList_On403(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string __)
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateStatusCodeMock(
                HttpStatusCode.Forbidden,
                "{\"error\": {\"message\": \"Access denied\"}}");
            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty($"{providerName} should return empty list on 403");
        }

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_Returns_EmptyList_On429(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string __)
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateRateLimitMock();
            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty($"{providerName} should return empty list on 429");
        }

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task Provider_Returns_EmptyList_On5xx(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string __)
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateServerErrorMock();
            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty($"{providerName} should return empty list on 5xx");
        }

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task TestConnection_Returns_False_On401(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string __)
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateUnauthorizedMock();
            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse($"{providerName} TestConnection should return false on 401");
        }

        [Theory]
        [MemberData(nameof(HttpChatProviders))]
        public async Task TestConnection_CapturesUserHint_On401(
            string providerName,
            Func<Mock<IHttpClient>, Logger, IAIProvider> factory,
            string _,
            string __)
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateUnauthorizedMock();
            var logger = Helpers.TestLogger.CreateNullLogger();
            var provider = factory(httpMock, logger);

            // Act
            await provider.TestConnectionAsync();

            // Assert
            var userMessage = provider.GetLastUserMessage();
            userMessage.Should().NotBeNullOrEmpty(
                $"{providerName} should capture user hint on 401");
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for HttpRequest to help with testing.
    /// </summary>
    internal static class HttpRequestTestExtensions
    {
        /// <summary>
        /// Get the request body content as a string.
        /// </summary>
        public static string GetContent(this HttpRequest request)
        {
            if (request?.ContentData == null)
                return null;

            return System.Text.Encoding.UTF8.GetString(request.ContentData);
        }
    }
}
