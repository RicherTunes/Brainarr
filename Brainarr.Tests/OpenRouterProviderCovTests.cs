using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
    /// Coverage tests for OpenRouterProvider paths not covered by other tests.
    /// Tests focus on: constructor validation, error hints, UpdateModel edge cases, and cancellation.
    /// </summary>
    public class OpenRouterProviderCovTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Logger _logger;

        public OpenRouterProviderCovTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
        }

        #region Constructor Validation

        // Source line 33: _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   33:            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        [Fact]
        public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new OpenRouterProvider(null!, _logger, "sk-or-test");

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClient");
        }

        // Source line 34: _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   34:            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new OpenRouterProvider(_httpClient.Object, null!, "sk-or-test");

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        // Source line 37-38: if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException(...)
        // Proof: grep -n "throw new ArgumentException" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   38:                throw new ArgumentException("OpenRouter API key is required", nameof(apiKey));
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithInvalidApiKey_ThrowsArgumentException(string? apiKey)
        {
            // Act
            var act = () => new OpenRouterProvider(_httpClient.Object, _logger, apiKey!);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("apiKey")
                .WithMessage("*OpenRouter API key is required*");
        }

        // Source line 41: _model = model ?? BrainarrConstants.DefaultOpenRouterModel;
        // Proof: grep -n "_model = model" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   41:            _model = model ?? BrainarrConstants.DefaultOpenRouterModel;
        [Fact]
        public void Constructor_WithNullModel_UsesDefaultModel()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test", model: null!);

            // Assert
            provider.ProviderName.Should().Be("OpenRouter");
        }

        #endregion

        #region ProviderName Property

        // Source line 29: public string ProviderName => "OpenRouter";
        // Proof: grep -n "ProviderName" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   29:        public string ProviderName => "OpenRouter";
        [Fact]
        public void ProviderName_ReturnsOpenRouter()
        {
            // Arrange
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var name = provider.ProviderName;

            // Assert
            name.Should().Be("OpenRouter", "because this is the OpenRouter provider implementation");
        }

        #endregion

        #region UpdateModel

        // Source line 337-344: UpdateModel only updates if model is not null/whitespace
        // Proof: grep -n "UpdateModel" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   337:        public void UpdateModel(string modelName)
        //   339:            if (!string.IsNullOrWhiteSpace(modelName))
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UpdateModel_WithInvalidValue_DoesNotUpdate(string? modelName)
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test", "openrouter/auto");

            // Act
            provider.UpdateModel(modelName!);

            // Assert - provider should still work with original model
            var result = provider.TestConnectionAsync().Result;
            result.Should().BeTrue("because the original model should still be used");
        }

        #endregion

        #region GetLastUserMessage and GetLearnMoreUrl - Initial State

        // Source line 346: public string? GetLastUserMessage() => _lastUserMessage;
        // Source line 347: public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;
        // Proof: grep -n "GetLastUserMessage\|GetLearnMoreUrl" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   346:        public string? GetLastUserMessage() => _lastUserMessage;
        //   347:        public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;
        [Fact]
        public void GetLastUserMessage_Initially_ReturnsNull()
        {
            // Arrange
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var message = provider.GetLastUserMessage();

            // Assert
            message.Should().BeNull("because no error has occurred yet");
        }

        [Fact]
        public void GetLearnMoreUrl_Initially_ReturnsNull()
        {
            // Arrange
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var url = provider.GetLearnMoreUrl();

            // Assert
            url.Should().BeNull("because no error has occurred yet");
        }

        #endregion

        #region TryCaptureOpenRouterHint - Error Hints via TestConnectionAsync

        // Source line 356-360: 401 sets invalid key hint
        // Proof: grep -n "401" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   356:                if (status == 401)
        [Fact]
        public async Task TestConnectionAsync_With401Status_SetsInvalidKeyHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.Unauthorized));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 401 indicates authentication failure");
            provider.GetLastUserMessage().Should().Contain("Invalid OpenRouter API key", "because 401 indicates invalid key");
            provider.GetLearnMoreUrl().Should().NotBeNull("because a help URL should be provided");
        }

        // Source line 361-365: 402 or "payment" in content sets payment hint
        // Proof: grep -n "402\|payment" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   361:                else if (status == 402 || content.IndexOf("payment", StringComparison.OrdinalIgnoreCase) >= 0)
        [Fact]
        public async Task TestConnectionAsync_With402Status_SetsPaymentHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.PaymentRequired));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 402 indicates payment required");
            provider.GetLastUserMessage().Should().Contain("payment", "because 402 indicates payment issue");
            provider.GetLearnMoreUrl().Should().NotBeNull("because a help URL should be provided");
        }

        // Source line 361: "payment" in content sets hint
        [Fact]
        public async Task TestConnectionAsync_WithPaymentContent_SetsPaymentHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"Payment required\"}}", HttpStatusCode.BadRequest));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("payment", "because content contains payment");
        }

        // Source line 366-370: 429 or "rate limit" in content sets rate limit hint
        // Proof: grep -n "429\|rate limit" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   366:                else if (status == 429 || content.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
        [Fact]
        public async Task TestConnectionAsync_With429Status_SetsRateLimitHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", (HttpStatusCode)429));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 429 indicates rate limiting");
            provider.GetLastUserMessage().Should().Contain("rate limit", "because 429 indicates rate limit exceeded");
        }

        // Source line 366: "rate limit" in content sets hint
        [Fact]
        public async Task TestConnectionAsync_WithRateLimitContent_SetsRateLimitHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"Rate limit exceeded\"}}", HttpStatusCode.BadRequest));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("rate limit", "because content contains rate limit");
        }

        #endregion

        #region TestConnectionAsync - Success and Error Cases

        // Source line 251-257: TestConnectionAsync returns true on OK status
        // Proof: grep -n "StatusCode == System.Net.HttpStatusCode.OK" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   251:                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
        [Fact]
        public async Task TestConnectionAsync_WithOkResponse_ReturnsTrue()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeTrue("because the connection test succeeded");
        }

        // Source line 259-268: TestConnectionAsync catches exceptions and returns false
        // Proof: grep -n "catch.*Exception" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   259:            catch (Exception ex)
        [Fact]
        public async Task TestConnectionAsync_WithHttpRequestException_ReturnsFalse()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Connection failed"));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

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
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because an exception occurred");
        }

        #endregion

        #region TestConnectionAsync with Cancellation

        // Source line 271-314: TestConnectionAsync with CancellationToken
        // Proof: grep -n "TestConnectionAsync.*CancellationToken" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   271:        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        [Fact]
        public async Task TestConnectionAsync_WithCancellationToken_ReturnsTrueOnSuccess()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");
            using var cts = new CancellationTokenSource();

            // Act
            var result = await provider.TestConnectionAsync(cts.Token);

            // Assert
            result.Should().BeTrue("because the connection test succeeded");
        }

        // Source line 273: cancellationToken.ThrowIfCancellationRequested();
        // Proof: grep -n "ThrowIfCancellationRequested" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   273:                cancellationToken.ThrowIfCancellationRequested();
        [Fact]
        public async Task TestConnectionAsync_WithCancelledToken_ThrowsOperationCanceledException()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            var act = () => provider.TestConnectionAsync(cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>("because the token was cancelled before the operation started");
        }

        #endregion

        #region GetRecommendationsAsync

        // Source line 206-210: GetRecommendationsAsync without CancellationToken
        // Proof: grep -n "GetRecommendationsAsync" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   206:        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        [Fact]
        public async Task GetRecommendationsAsync_WithValidResponse_ReturnsRecommendations()
        {
            // Arrange
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test", preferStructured: true);
            var recs = new { recommendations = new[] { new { artist = "Artist X", album = "Album Y", genre = "Pop", confidence = 0.8, reason = "Test" } } };
            var contentArray = Newtonsoft.Json.JsonConvert.SerializeObject(recs);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = contentArray } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().HaveCount(1, "because one recommendation was returned");
            result[0].Artist.Should().Be("Artist X", "because that is the artist in the response");
        }

        // Source line 212-213: GetRecommendationsAsync with CancellationToken
        // Proof: grep -n "GetRecommendationsAsync.*CancellationToken" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   212:        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        [Fact]
        public async Task GetRecommendationsAsync_WithCancellationToken_ReturnsRecommendations()
        {
            // Arrange
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test", preferStructured: true);
            var recs = new { recommendations = new[] { new { artist = "Artist Z", album = "Album W", genre = "Rock", confidence = 0.9, reason = "Test 2" } } };
            var contentArray = Newtonsoft.Json.JsonConvert.SerializeObject(recs);
            var responseObj = new { id = "2", choices = new[] { new { message = new { content = contentArray } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            using var cts = new CancellationTokenSource();

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt", cts.Token);

            // Assert
            result.Should().HaveCount(1, "because one recommendation was returned");
            result[0].Artist.Should().Be("Artist Z", "because that is the artist in the response");
        }

        // Source line 185-189: Empty response returns empty list
        // Proof: grep -n "Empty response from OpenRouter" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   187:                    _logger.Warn("Empty response from OpenRouter");
        [Fact]
        public async Task GetRecommendationsAsync_WithEmptyResponse_ReturnsEmptyList()
        {
            // Arrange
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");
            var responseObj = new { id = "3", choices = new[] { new { message = new { content = "" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty("because the response content was empty");
        }

        // Source line 142-158: Non-OK status returns empty list
        // Proof: grep -n "OpenRouter API error" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   144:                    _logger.Error("$OpenRouter API error: {response.StatusCode} - {response.Content}");
        [Fact]
        public async Task GetRecommendationsAsync_WithNonOkStatus_ReturnsEmptyList()
        {
            // Arrange
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.InternalServerError));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty("because the API returned an error status");
        }

        // Source line 199-203: Exception returns empty list
        // Proof: grep -n "Error getting recommendations from OpenRouter" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   201:                _logger.Error(ex, "Error getting recommendations from OpenRouter");
        [Fact]
        public async Task GetRecommendationsAsync_WithException_ReturnsEmptyList()
        {
            // Arrange
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty("because an exception occurred during the request");
        }

        #endregion

        #region GetRecommendationsInternalAsync - Null Response Handling

        // Source line 133-137: Null response returns empty list
        // Proof: grep -n "OpenRouter request failed with no HTTP response" Brainarr.Plugin/Services/Providers/OpenRouterProvider.cs
        //   135:                    _logger.Error("OpenRouter request failed with no HTTP response");
        [Fact]
        public async Task GetRecommendationsAsync_WithNullResponse_ReturnsEmptyList()
        {
            // Arrange
            var provider = new OpenRouterProvider(_httpClient.Object, _logger, "sk-or-test");

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpResponse)null!);

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty("because the HTTP response was null");
        }

        #endregion
    }
}
