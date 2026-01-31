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

        #region Extended Negative Path Tests (Edge Cases)

        [Fact]
        public async Task GetRecommendations_With400BadRequest_ReturnsEmptyList()
        {
            // Arrange: 400 Bad Request (malformed request)
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Bad request: invalid parameter\"}}",
                    HttpStatusCode.BadRequest));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("400 Bad Request should return empty list");
        }

        [Fact]
        public async Task GetRecommendations_With502BadGateway_ReturnsEmptyList()
        {
            // Arrange: 502 Bad Gateway (upstream error)
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Bad gateway\"}}",
                    HttpStatusCode.BadGateway));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("502 Bad Gateway should return empty list");
        }

        [Fact]
        public async Task GetRecommendations_With504GatewayTimeout_ReturnsEmptyList()
        {
            // Arrange: 504 Gateway Timeout
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Gateway timeout\"}}",
                    HttpStatusCode.GatewayTimeout));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("504 Gateway Timeout should return empty list");
        }

        [Fact]
        public async Task GetRecommendations_WithNonNumericErrorCode_ReturnsEmptyList()
        {
            // Arrange: Error code that's a string instead of number (malformed API response)
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"code\": \"not_a_number\", \"message\": \"Invalid error format\"}}",
                    HttpStatusCode.OK));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Non-numeric error code should be handled gracefully");
        }

        [Fact]
        public async Task GetRecommendations_WithMissingMessage_ReturnsEmptyList()
        {
            // Arrange: choices[0].message is missing entirely
            var responseObj = new
            {
                id = "test",
                choices = new[] { new { finish_reason = "stop" } }, // no message property
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
            result.Should().BeEmpty("Missing message should return empty list");
        }

        [Fact]
        public async Task GetRecommendations_WithMissingFinishReason_StillParsesContent()
        {
            // Arrange: finish_reason is missing but content is valid
            var content = "[{\"artist\":\"Radiohead\",\"album\":\"OK Computer\",\"genre\":\"Rock\",\"confidence\":0.9}]";
            var responseObj = new
            {
                id = "test",
                choices = new[] { new { message = new { content = content } } }, // no finish_reason
                usage = new { prompt_tokens = 100, completion_tokens = 50, total_tokens = 150 }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert: Should still parse content even without finish_reason
            result.Should().NotBeEmpty("Valid content should be parsed regardless of finish_reason");
        }

        [Fact]
        public async Task GetRecommendations_WithEmptyChoicesArray_ReturnsEmptyList()
        {
            // Arrange: choices is an empty array
            var responseObj = new
            {
                id = "test",
                choices = new object[] { }, // empty array
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
            result.Should().BeEmpty("Empty choices array should return empty list");
        }

        [Fact]
        public async Task GetRecommendations_WithFinishReasonLength_ReturnsEmptyList()
        {
            // Arrange: finish_reason is "length" indicating incomplete generation
            var responseObj = new
            {
                id = "test",
                choices = new[] { new { finish_reason = "length", message = new { content = "[{\"artist\":\"Incomplete" } } },
                usage = new { prompt_tokens = 100, completion_tokens = 4096, total_tokens = 4196 }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert: Incomplete (truncated) response should be handled gracefully
            result.Should().BeEmpty("Truncated response (finish_reason=length) should return empty list");
        }

        [Fact]
        public async Task GetRecommendations_WithDnsResolutionFailure_ReturnsEmptyList()
        {
            // Arrange: DNS resolution failure
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException(
                    "No such host is known",
                    new System.Net.Sockets.SocketException(11001)));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("DNS resolution failure should return empty list");
        }

        [Fact]
        public async Task GetRecommendations_WithConnectionRefused_ReturnsEmptyList()
        {
            // Arrange: Connection refused error
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException(
                    "Connection refused",
                    new System.Net.Sockets.SocketException(10061)));

            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty("Connection refused should return empty list");
        }

        #endregion
    }
}
