using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;
using Brainarr.Tests.Helpers;

namespace Brainarr.Tests.Services.Providers.Contracts
{
    /// <summary>
    /// Contract tests for ZaiGlmProvider using the shared test infrastructure.
    /// Verifies standard error handling across timeout, 429, malformed, etc.
    /// </summary>
    [Trait("Category", "Contract")]
    [Trait("Provider", "ZaiGlm")]
    public class ZaiGlmProviderContractTests : ProviderContractTestBase<ZaiGlmProvider>
    {
        protected override string ProviderFormat => "zaiglm";

        protected override ZaiGlmProvider CreateProvider(Mock<IHttpClient> httpMock, Logger logger)
        {
            return new ZaiGlmProvider(httpMock.Object, logger, "test-api-key", preferStructured: true);
        }

        /// <summary>
        /// Override with properly serialized JSON content.
        /// The base test helper uses manual string building which double-escapes content.
        /// </summary>
        [Fact]
        public override async Task GetRecommendations_WithValidResponse_ParsesRecommendations()
        {
            // Use proper JSON serialization for the response
            var content = "[{\"artist\":\"Radiohead\",\"album\":\"OK Computer\",\"genre\":\"Alternative Rock\",\"confidence\":0.9,\"reason\":\"Test recommendation\"}]";
            var responseObj = new
            {
                id = "test",
                choices = new[] { new { finish_reason = "stop", message = new { content = content } } },
                usage = new { prompt_tokens = 100, completion_tokens = 50, total_tokens = 150 }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Assert.NotEmpty(result);
            Assert.True(result.Count >= 1, $"Expected at least 1 recommendation but got {result.Count}");
        }

        // All other tests inherited from ProviderContractTestBase:
        // - GetRecommendations_WithTimeout_ReturnsEmptyList
        // - GetRecommendations_WithCancellation_ReturnsEmptyList
        // - GetRecommendations_With429RateLimit_ReturnsEmptyList
        // - GetRecommendations_With401Unauthorized_ReturnsEmptyList
        // - GetRecommendations_With500ServerError_ReturnsEmptyList
        // - GetRecommendations_WithMalformedJson_ReturnsEmptyList
        // - GetRecommendations_WithEmptyResponse_ReturnsEmptyList
        // - GetRecommendations_WithUnexpectedSchema_ReturnsEmptyList
        // - GetRecommendations_WithNullContent_ReturnsEmptyList
        // - GetRecommendations_WithNetworkError_ReturnsEmptyList
        // - TestConnection_With401_ReturnsFalse
        // - TestConnection_WithTimeout_ReturnsFalse

        #region Additional Negative Path Tests

        [Fact]
        public async Task GetRecommendations_With403Forbidden_ReturnsEmptyList()
        {
            // Arrange: 403 Forbidden response (access denied)
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Access denied. Your API key does not have permission.\"}}",
                    HttpStatusCode.Forbidden));

            var provider = CreateProvider(httpMock, Logger);

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

            var provider = CreateProvider(httpMock, Logger);

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

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("403 Forbidden should fail connection test");
        }

        [Fact]
        public async Task GetRecommendations_WithContentFiltered_ReturnsEmptyList()
        {
            // Arrange: Response with content filtered finish_reason
            var responseObj = new
            {
                id = "test",
                choices = new[] { new { finish_reason = "sensitive", message = new { content = "[]" } } },
                usage = new { prompt_tokens = 100, completion_tokens = 50, total_tokens = 150 }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Content filtered response should return empty list");
        }

        [Fact]
        public async Task GetRecommendations_WithErrorInResponseBody_ReturnsEmptyList()
        {
            // Arrange: HTTP 200 but error in response body (Z.AI specific behavior)
            var response = "{\"error\": {\"code\": \"invalid_request\", \"message\": \"Invalid model specified\"}}";

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Error in response body should return empty list");
        }

        #endregion
    }
}
