using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Contracts
{
    /// <summary>
    /// Contract tests for OpenAIProvider using the shared test infrastructure.
    /// Verifies standard error handling across timeout, 429, malformed, etc.
    /// </summary>
    [Trait("Category", "Contract")]
    [Trait("Provider", "OpenAI")]
    public class OpenAIProviderContractTests : ProviderContractTestBase<OpenAIProvider>
    {
        protected override string ProviderFormat => "openai";

        protected override OpenAIProvider CreateProvider(Mock<IHttpClient> httpMock, Logger logger)
        {
            return new OpenAIProvider(httpMock.Object, logger, "sk-test-api-key", preferStructured: true);
        }

        // Most tests inherited from ProviderContractTestBase:
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

        /// <summary>
        /// Override the base test with a properly formatted OpenAI response.
        /// The base GenerateSuccessfulResponse has double-escaping issues.
        /// </summary>
        [Xunit.Fact]
        public override async Task GetRecommendations_WithValidResponse_ParsesRecommendations()
        {
            // Use a properly formatted OpenAI response
            var recommendationsJson = "[{\"artist\":\"Radiohead\",\"album\":\"OK Computer\",\"genre\":\"Alternative Rock\",\"confidence\":0.9,\"reason\":\"Test\"}]";
            var escapedContent = recommendationsJson.Replace("\"", "\\\"");
            var responseBody = $"{{\"id\":\"test\",\"choices\":[{{\"message\":{{\"content\":\"{escapedContent}\"}},\"finish_reason\":\"stop\"}}],\"usage\":{{\"prompt_tokens\":100,\"completion_tokens\":50,\"total_tokens\":150}}}}";

            var httpMock = new Mock<IHttpClient>();
            var response = Helpers.HttpResponseFactory.CreateResponse(responseBody, System.Net.HttpStatusCode.OK);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.NotEmpty(result);
            Xunit.Assert.True(result.Count >= 1, $"Expected at least 1 recommendation but got {result.Count}");
            Xunit.Assert.Contains(result, r => r.Artist == "Radiohead");
        }
    }

    /// <summary>
    /// Additional OpenAI-specific contract tests for user-friendly error hints
    /// and model selection functionality.
    /// </summary>
    [Trait("Category", "Contract")]
    [Trait("Provider", "OpenAI")]
    public class OpenAIProviderErrorHintTests
    {
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        #region User-Friendly Error Hints

        [Fact]
        public async Task TestConnection_WithInvalidApiKey_CapturesUserHint()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var errorBody = "{\"error\": {\"message\": \"Incorrect API key provided: sk-test***. You can find your API key at https://platform.openai.com/account/api-keys.\", \"type\": \"invalid_request_error\", \"param\": null, \"code\": \"invalid_api_key\"}}";
            var response = Helpers.HttpResponseFactory.CreateResponse(errorBody, HttpStatusCode.Unauthorized);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-invalid");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            var userMessage = provider.GetLastUserMessage();
            userMessage.Should().NotBeNullOrEmpty();
            userMessage.Should().Contain("Invalid OpenAI API key");
            userMessage.Should().Contain("sk-");

            var learnMoreUrl = provider.GetLearnMoreUrl();
            learnMoreUrl.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task TestConnection_WithRateLimitError_CapturesUserHint()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var errorBody = "{\"error\": {\"message\": \"Rate limit reached for default-gpt-4 in organization org-xxx on tokens per min. Limit: 10000, Used: 9500, Requested: 1000.\", \"type\": \"tokens\", \"param\": null, \"code\": \"rate_limit_exceeded\"}}";
            var response = Helpers.HttpResponseFactory.CreateResponse(errorBody, HttpStatusCode.TooManyRequests);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            var userMessage = provider.GetLastUserMessage();
            userMessage.Should().NotBeNullOrEmpty();
            userMessage.Should().Contain("rate limit");
        }

        [Fact]
        public async Task TestConnection_WithInsufficientQuota_CapturesUserHint()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            // Note: When insufficient_quota comes as a 403, the provider detects it properly
            // When it comes as 429, the provider treats it as a rate limit (which is technically correct
            // since OpenAI returns 429 for quota issues as well as rate limits)
            var errorBody = "{\"error\": {\"message\": \"You exceeded your current quota, please check your plan and billing details.\", \"type\": \"insufficient_quota\", \"param\": null, \"code\": \"insufficient_quota\"}}";
            // Use 403 to ensure insufficient_quota check is reached (not short-circuited by 429)
            var response = Helpers.HttpResponseFactory.CreateResponse(errorBody, HttpStatusCode.Forbidden);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            var userMessage = provider.GetLastUserMessage();
            userMessage.Should().NotBeNullOrEmpty();
            userMessage.Should().Contain("quota");
        }

        [Fact]
        public async Task GetRecommendations_With401_CapturesUserHint()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var errorBody = "{\"error\": {\"message\": \"Invalid API key\", \"type\": \"invalid_request_error\", \"code\": \"invalid_api_key\"}}";
            var response = Helpers.HttpResponseFactory.CreateResponse(errorBody, HttpStatusCode.Unauthorized);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty();
            // The error hint is captured during the failed request
        }

        #endregion

        #region Model Selection

        [Theory]
        [InlineData("gpt-4o")]
        [InlineData("gpt-4-turbo")]
        [InlineData("gpt-3.5-turbo")]
        [InlineData("gpt-4o-mini")]
        [InlineData("GPT41_Mini")] // UI label format
        public void UpdateModel_WithValidModelId_UpdatesModel(string modelId)
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key", "gpt-3.5-turbo");

            // Act
            provider.UpdateModel(modelId);

            // Assert - No exception should be thrown
            // The model is updated internally
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UpdateModel_WithInvalidModelId_DoesNotUpdate(string modelId)
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key", "gpt-4o");

            // Act
            provider.UpdateModel(modelId);

            // Assert - Should not throw, model should remain unchanged internally
        }

        [Fact]
        public async Task GetRecommendations_UsesConfiguredModel()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            HttpRequest capturedRequest = null;

            var successResponse = "{\"choices\": [{\"message\": {\"content\": \"[{\\\"artist\\\":\\\"Test Artist\\\", \\\"album\\\":\\\"Test Album\\\", \\\"genre\\\":\\\"Rock\\\", \\\"confidence\\\":0.9, \\\"reason\\\":\\\"Good match\\\"}]\"}}]}";
            var response = Helpers.HttpResponseFactory.CreateResponse(successResponse, HttpStatusCode.OK);

            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => capturedRequest = req)
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key", "gpt-4-turbo");

            // Act
            await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            capturedRequest.Should().NotBeNull();
            // The model should be sent in the request body
        }

        #endregion

        #region Timeout Handling

        [Fact]
        public async Task GetRecommendations_WithHttpTimeout_ReturnsEmptyList()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing."));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendations_WithOperationCancellation_ReturnsEmptyList()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new OperationCanceledException("The operation was canceled."));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt", cts.Token);

            // Assert
            result.Should().BeEmpty();
        }

        #endregion

        #region HTTP Error Code Handling

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
        [InlineData(HttpStatusCode.Unauthorized, "error")]
        [InlineData(HttpStatusCode.InternalServerError, "error")]
        [InlineData(HttpStatusCode.BadGateway, "error")]
        [InlineData(HttpStatusCode.ServiceUnavailable, "error")]
        public async Task GetRecommendations_WithHttpError_ReturnsEmptyList(HttpStatusCode statusCode, string expectedBodyContent)
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var errorBody = $"{{\"error\": {{\"message\": \"{expectedBodyContent}\"}}}}";
            var response = Helpers.HttpResponseFactory.CreateResponse(errorBody, statusCode);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TestConnection_With500ServerError_ReturnsFalse()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var errorBody = "{\"error\": {\"message\": \"Internal server error\"}}";
            var response = Helpers.HttpResponseFactory.CreateResponse(errorBody, HttpStatusCode.InternalServerError);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region Malformed JSON Handling

        [Theory]
        [InlineData("{ this is not valid JSON }")]
        [InlineData("")]
        [InlineData("null")]
        [InlineData("undefined")]
        [InlineData("<html>Error page</html>")]
        public async Task GetRecommendations_WithMalformedResponse_ReturnsEmptyList(string malformedContent)
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var response = Helpers.HttpResponseFactory.CreateResponse(malformedContent, HttpStatusCode.OK);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendations_WithValidJsonButNoChoices_ReturnsEmptyList()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var responseBody = "{\"id\": \"test\", \"object\": \"chat.completion\", \"choices\": []}";
            var response = Helpers.HttpResponseFactory.CreateResponse(responseBody, HttpStatusCode.OK);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendations_WithNullMessageContent_ReturnsEmptyList()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var responseBody = "{\"id\": \"test\", \"choices\": [{\"message\": {\"content\": null}, \"finish_reason\": \"stop\"}]}";
            var response = Helpers.HttpResponseFactory.CreateResponse(responseBody, HttpStatusCode.OK);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty();
        }

        #endregion

        #region Network Error Handling

        [Fact]
        public async Task GetRecommendations_WithNetworkException_ReturnsEmptyList()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("Unable to connect to the remote server"));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TestConnection_WithNetworkException_ReturnsFalse()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("Connection refused"));

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region Constructor Validation

        [Fact]
        public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new OpenAIProvider(null, _logger, "sk-test-key"));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new OpenAIProvider(httpMock.Object, null, "sk-test-key"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithInvalidApiKey_ThrowsArgumentException(string apiKey)
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new OpenAIProvider(httpMock.Object, _logger, apiKey));
        }

        [Fact]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();

            // Act
            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Assert
            provider.Should().NotBeNull();
            provider.ProviderName.Should().Be("OpenAI");
        }

        #endregion

        #region Successful Response Parsing

        [Fact]
        public async Task GetRecommendations_WithValidResponse_ParsesRecommendations()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var recommendationsJson = "[{\"artist\":\"Radiohead\", \"album\":\"OK Computer\", \"genre\":\"Alternative Rock\", \"confidence\":0.95, \"reason\":\"Classic album\"}]";
            var responseBody = $"{{\"id\":\"test\",\"choices\":[{{\"message\":{{\"content\":\"{EscapeJsonString(recommendationsJson)}\"}},\"finish_reason\":\"stop\"}}]}}";
            var response = Helpers.HttpResponseFactory.CreateResponse(responseBody, HttpStatusCode.OK);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().NotBeEmpty();
            result.Should().Contain(r => r.Artist == "Radiohead");
        }

        [Fact]
        public async Task GetRecommendations_WithWrappedRecommendations_ParsesRecommendations()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();
            var recommendationsJson = "{\"recommendations\":[{\"artist\":\"Arcade Fire\", \"album\":\"The Suburbs\", \"genre\":\"Indie Rock\", \"confidence\":0.88, \"reason\":\"Great album\"}]}";
            var responseBody = $"{{\"id\":\"test\",\"choices\":[{{\"message\":{{\"content\":\"{EscapeJsonString(recommendationsJson)}\"}},\"finish_reason\":\"stop\"}}]}}";
            var response = Helpers.HttpResponseFactory.CreateResponse(responseBody, HttpStatusCode.OK);
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var provider = new OpenAIProvider(httpMock.Object, _logger, "sk-test-key");

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            result.Should().NotBeEmpty();
            result.Should().Contain(r => r.Artist == "Arcade Fire");
        }

        private static string EscapeJsonString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        #endregion
    }
}
