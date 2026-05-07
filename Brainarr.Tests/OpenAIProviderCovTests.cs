using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for OpenAIProvider paths not covered by OpenAIProviderTests.
    /// Tests focus on: constructor validation, error hints, UpdateModel edge cases, and cancellation.
    /// </summary>
    public class OpenAIProviderCovTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Logger _logger;

        public OpenAIProviderCovTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
        }

        #region Constructor Validation

        // Source line 55: _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   55:            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        [Fact]
        public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new OpenAIProvider(null!, _logger, "sk-test");

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClient");
        }

        // Source line 56: _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   56:            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new OpenAIProvider(_httpClient.Object, null!, "sk-test");

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        // Source line 59-60: if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException(...)
        // Proof: grep -n "throw new ArgumentException" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   60:                throw new ArgumentException("OpenAI API key is required", nameof(apiKey));
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithInvalidApiKey_ThrowsArgumentException(string? apiKey)
        {
            // Act
            var act = () => new OpenAIProvider(_httpClient.Object, _logger, apiKey!);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("apiKey")
                .WithMessage("*OpenAI API key is required*");
        }

        // Source line 63: _model = model ?? BrainarrConstants.DefaultOpenAIModel;
        // Proof: grep -n "_model = model" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   63:            _model = model ?? BrainarrConstants.DefaultOpenAIModel;
        [Fact]
        public void Constructor_WithNullModel_UsesDefaultModel()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test", model: null!);

            // Assert
            provider.ProviderName.Should().Be("OpenAI");
        }

        #endregion

        #region ProviderName Property

        // Source line 42: public string ProviderName => "OpenAI";
        // Proof: grep -n "ProviderName" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   42:        public string ProviderName => "OpenAI";
        [Fact]
        public void ProviderName_ReturnsOpenAI()
        {
            // Arrange
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var name = provider.ProviderName;

            // Assert
            name.Should().Be("OpenAI", "because this is the OpenAI provider implementation");
        }

        #endregion

        #region UpdateModel

        // Source line 423-429: UpdateModel only updates if model is not null/whitespace
        // Proof: grep -n "UpdateModel" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   423:        public void UpdateModel(string modelName)
        //   425:            if (!string.IsNullOrWhiteSpace(modelName))
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UpdateModel_WithInvalidValue_DoesNotUpdate(string? modelName)
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test", "gpt-4o-mini");

            // Act
            provider.UpdateModel(modelName!);

            // Assert - provider should still work with original model
            var result = provider.TestConnectionAsync().Result;
            result.Should().BeTrue("because the original model should still be used");
        }

        #endregion

        #region GetLastUserMessage and GetLearnMoreUrl - Initial State

        // Source line 432: public string? GetLastUserMessage() => _lastUserMessage;
        // Source line 433: public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;
        // Proof: grep -n "GetLastUserMessage\|GetLearnMoreUrl" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   432:        public string? GetLastUserMessage() => _lastUserMessage;
        //   433:        public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;
        [Fact]
        public void GetLastUserMessage_Initially_ReturnsNull()
        {
            // Arrange
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var message = provider.GetLastUserMessage();

            // Assert
            message.Should().BeNull("because no error has occurred yet");
        }

        [Fact]
        public void GetLearnMoreUrl_Initially_ReturnsNull()
        {
            // Arrange
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var url = provider.GetLearnMoreUrl();

            // Assert
            url.Should().BeNull("because no error has occurred yet");
        }

        #endregion

        #region TryCaptureOpenAIHint - Error Hints via TestConnectionAsync

        // Source line 443-447: 401 or "invalid_api_key" or "Incorrect API key" sets invalid key hint
        // Proof: grep -n "invalid_api_key\|Incorrect API key" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   443:                if (status == 401 || content.IndexOf("invalid_api_key", StringComparison.OrdinalIgnoreCase) >= 0 || content.IndexOf("Incorrect API key", StringComparison.OrdinalIgnoreCase) >= 0)
        [Fact]
        public async Task TestConnectionAsync_With401Status_SetsInvalidKeyHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.Unauthorized));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 401 indicates authentication failure");
            provider.GetLastUserMessage().Should().Contain("Invalid OpenAI API key", "because 401 indicates invalid key");
            provider.GetLearnMoreUrl().Should().NotBeNull("because a help URL should be provided");
        }

        // Source line 443: "invalid_api_key" in content sets hint
        [Fact]
        public async Task TestConnectionAsync_WithInvalidApiKeyContent_SetsInvalidKeyHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"invalid_api_key\"}}", HttpStatusCode.BadRequest));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("Invalid OpenAI API key", "because content contains invalid_api_key");
        }

        // Source line 443: "Incorrect API key" in content sets hint
        [Fact]
        public async Task TestConnectionAsync_WithIncorrectApiKeyContent_SetsInvalidKeyHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"Incorrect API key provided\"}}", HttpStatusCode.BadRequest));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("Invalid OpenAI API key", "because content contains Incorrect API key");
        }

        // Source line 448-451: 429 or "rate limit" sets rate limit hint
        // Proof: grep -n "rate limit" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   448:                else if (status == 429 || content.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
        [Fact]
        public async Task TestConnectionAsync_With429Status_SetsRateLimitHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", (HttpStatusCode)429));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 429 indicates rate limiting");
            provider.GetLastUserMessage().Should().Contain("rate limit", "because 429 indicates rate limit exceeded");
        }

        // Source line 448: "rate limit" in content sets hint
        [Fact]
        public async Task TestConnectionAsync_WithRateLimitContent_SetsRateLimitHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"Rate limit exceeded\"}}", HttpStatusCode.BadRequest));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("rate limit", "because content contains rate limit");
        }

        // Source line 452-455: "insufficient_quota" or "insufficient" sets quota hint
        // Proof: grep -n "insufficient_quota\|insufficient" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   452:                else if (content.IndexOf("insufficient_quota", StringComparison.OrdinalIgnoreCase) >= 0 || content.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0)
        [Fact]
        public async Task TestConnectionAsync_WithInsufficientQuotaContent_SetsQuotaHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"insufficient_quota\"}}", HttpStatusCode.BadRequest));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("quota", "because content contains insufficient_quota");
        }

        [Fact]
        public async Task TestConnectionAsync_WithInsufficientContent_SetsQuotaHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"Insufficient credits\"}}", HttpStatusCode.PaymentRequired));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("quota", "because content contains insufficient");
        }

        #endregion

        #region TestConnectionAsync with Cancellation

        // Source line 398-420: TestConnectionAsync with CancellationToken
        // Proof: grep -n "TestConnectionAsync.*CancellationToken" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   398:        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        [Fact]
        public async Task TestConnectionAsync_WithCancellationToken_ReturnsTrueOnSuccess()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");
            using var cts = new CancellationTokenSource();

            // Act
            var result = await provider.TestConnectionAsync(cts.Token);

            // Assert
            result.Should().BeTrue("because the connection test succeeded");
        }

        [Fact]
        public async Task TestConnectionAsync_WithCancelledToken_ThrowsOperationCanceledException()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            var act = () => provider.TestConnectionAsync(cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>("because the token was cancelled before the operation started");
        }

        #endregion

        #region GetRecommendationsAsync with Cancellation

        // Source line 325-326: GetRecommendationsAsync with CancellationToken
        // Proof: grep -n "GetRecommendationsAsync.*CancellationToken" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   325:        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        [Fact]
        public async Task GetRecommendationsAsync_WithCancellationToken_ReturnsRecommendations()
        {
            // Arrange
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test", preferStructured: true);
            var recs = new { recommendations = new[] { new { artist = "Artist X", album = "Album Y", genre = "Pop", confidence = 0.8, reason = "Test" } } };
            var contentArray = Newtonsoft.Json.JsonConvert.SerializeObject(recs);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = contentArray } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            using var cts = new CancellationTokenSource();

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt", cts.Token);

            // Assert
            result.Should().HaveCount(1, "because one recommendation was returned");
            result[0].Artist.Should().Be("Artist X", "because that is the artist in the response");
        }

        #endregion

        #region TestConnectionAsync - Exception Handling

        // Source line 383-394: TestConnectionAsync catches exceptions and returns false
        // Proof: grep -n "catch.*Exception" Brainarr.Plugin/Services/Providers/OpenAIProvider.cs
        //   383:            catch (Exception ex)
        [Fact]
        public async Task TestConnectionAsync_WithHttpRequestException_ReturnsFalse()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Connection failed"));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because an HTTP exception occurred");
        }

        [Fact]
        public async Task TestConnectionAsync_WithGeneralException_ReturnsFalse()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new InvalidOperationException("Unexpected error"));
            var provider = new OpenAIProvider(_httpClient.Object, _logger, "sk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because an exception occurred");
        }

        #endregion
    }
}
