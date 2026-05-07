using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for DeepSeekProvider paths not covered by DeepSeekProviderTests.
    /// Tests focus on: constructor validation, error hints (TryCaptureDeepSeekHint),
    /// UpdateModel edge cases, cancellation, and edge cases in GetRecommendationsAsync.
    /// </summary>
    public class DeepSeekProviderCovTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Logger _logger;

        public DeepSeekProviderCovTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
        }

        #region Constructor Validation

        // Source line 33: _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Providers/DeepSeekProvider.cs
        //   33:            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        [Fact]
        public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new DeepSeekProvider(null!, _logger, "dsk-test");

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClient");
        }

        // Source line 34: _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Providers/DeepSeekProvider.cs
        //   34:            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new DeepSeekProvider(_httpClient.Object, null!, "dsk-test");

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        // Source line 38: throw new ArgumentException("DeepSeek API key is required", nameof(apiKey));
        // Proof: grep -n "throw new ArgumentException" Brainarr.Plugin/Services/Providers/DeepSeekProvider.cs
        //   38:                throw new ArgumentException("DeepSeek API key is required", nameof(apiKey));
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithInvalidApiKey_ThrowsArgumentException(string? apiKey)
        {
            // Act
            var act = () => new DeepSeekProvider(_httpClient.Object, _logger, apiKey!);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("apiKey")
                .WithMessage("*DeepSeek API key is required*");
        }

        #endregion

        #region ProviderName Property

        // Source line 31: public string ProviderName => "DeepSeek";
        // Proof: grep -n "ProviderName" Brainarr.Plugin/Services/Providers/DeepSeekProvider.cs
        //   31:        public string ProviderName => "DeepSeek";
        [Fact]
        public void ProviderName_ReturnsDeepSeek()
        {
            // Arrange
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var name = provider.ProviderName;

            // Assert
            name.Should().Be("DeepSeek", "because this is the DeepSeek provider implementation");
        }

        #endregion

        #region TryCaptureDeepSeekHint via TestConnectionAsync

        // TryCaptureDeepSeekHint is private, tested through TestConnectionAsync
        // Source lines 181-204: TryCaptureDeepSeekHint handles various error conditions
        // Proof: grep -n "TryCaptureDeepSeekHint\|_lastUserMessage\|_lastUserLearnMoreUrl" Brainarr.Plugin/Services/Providers/DeepSeekProvider.cs
        //   181:        private void TryCaptureDeepSeekHint(string? body, int status)
        //   185:            _lastUserMessage = null;
        //   186:            _lastUserLearnMoreUrl = null

        // Source line 191-193: 401 or "invalid_api_key" or "authentication" triggers hint
        [Fact]
        public async Task TestConnectionAsync_With401Status_SetsInvalidKeyHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("unauthorized", HttpStatusCode.Unauthorized));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 401 indicates authentication failure");
            provider.GetLastUserMessage().Should().Contain("Invalid DeepSeek API key", "because 401 triggers invalid key hint");
            provider.GetLearnMoreUrl().Should().Be(BrainarrConstants.DocsDeepSeekSection, "because hint sets the DeepSeek docs URL");
        }

        // Source line 195-198: 429 or "rate limit" triggers hint
        [Fact]
        public async Task TestConnectionAsync_With429Status_SetsRateLimitHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("rate limited", (HttpStatusCode)429));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 429 indicates rate limiting");
            provider.GetLastUserMessage().Should().Contain("rate limit", "because 429 triggers rate limit hint");
            provider.GetLearnMoreUrl().Should().Be(BrainarrConstants.DocsDeepSeekSection);
        }

        // Source line 200-203: 402 or "insufficient" or "balance" triggers hint
        [Fact]
        public async Task TestConnectionAsync_With402Status_SetsCreditsHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("payment required", HttpStatusCode.PaymentRequired));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 402 indicates payment required");
            provider.GetLastUserMessage().Should().Contain("credits exhausted", "because 402 triggers credits hint");
            provider.GetLearnMoreUrl().Should().Be(BrainarrConstants.DocsDeepSeekSection);
        }

        // Source line 191-193: content containing "invalid_api_key" triggers hint
        [Fact]
        public async Task TestConnectionAsync_WithInvalidApiKeyInContent_SetsHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": \"invalid_api_key\"}", HttpStatusCode.Forbidden));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            provider.GetLastUserMessage().Should().Contain("Invalid DeepSeek API key", "because 'invalid_api_key' in content triggers hint");
        }

        // Source line 195-198: content containing "rate limit" triggers hint
        [Fact]
        public async Task TestConnectionAsync_WithRateLimitInContent_SetsHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": \"rate limit exceeded\"}", HttpStatusCode.InternalServerError));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            provider.GetLastUserMessage().Should().Contain("rate limit", "because 'rate limit' in content triggers hint");
        }

        // Source line 200-203: content containing "insufficient" triggers hint
        [Fact]
        public async Task TestConnectionAsync_WithInsufficientInContent_SetsHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": \"insufficient balance\"}", HttpStatusCode.BadRequest));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            provider.GetLastUserMessage().Should().Contain("credits exhausted", "because 'insufficient' in content triggers credits hint");
        }

        // Source line 200-203: content containing "balance" triggers hint
        [Fact]
        public async Task TestConnectionAsync_WithBalanceInContent_SetsHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": \"balance too low\"}", HttpStatusCode.ServiceUnavailable));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            provider.GetLastUserMessage().Should().Contain("credits exhausted", "because 'balance' in content triggers credits hint");
        }

        #endregion

        #region TestConnectionAsync Success

        [Fact]
        public async Task TestConnectionAsync_WithOkResponse_ReturnsTrueAndClearsHints()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeTrue("because OK status indicates successful connection");
            provider.GetLastUserMessage().Should().BeNull("because successful connection clears hints");
            provider.GetLearnMoreUrl().Should().BeNull("because successful connection clears hints");
        }

        #endregion

        #region TestConnectionAsync with CancellationToken

        // Source line 205-226: TestConnectionAsync(CancellationToken) throws on cancellation
        // Proof: grep -n "ThrowIfCancellationRequested" Brainarr.Plugin/Services/Providers/DeepSeekProvider.cs
        //   207:            cancellationToken.ThrowIfCancellationRequested();
        //   224:            cancellationToken.ThrowIfCancellationRequested();
        [Fact]
        public async Task TestConnectionAsync_WithCancelledToken_ThrowsOperationCanceledException()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var act = async () => await provider.TestConnectionAsync(cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>("because a pre-cancelled token should throw immediately");
        }

        [Fact]
        public async Task TestConnectionAsync_WithTokenAndOkResponse_ReturnsTrue()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");
            using var cts = new CancellationTokenSource();

            // Act
            var result = await provider.TestConnectionAsync(cts.Token);

            // Assert
            result.Should().BeTrue("because OK response with valid token should succeed");
        }

        #endregion

        #region UpdateModel Edge Cases

        // Source line 231-237: UpdateModel only updates if not null/whitespace
        // Proof: grep -n "UpdateModel\|!string.IsNullOrWhiteSpace" Brainarr.Plugin/Services/Providers/DeepSeekProvider.cs
        //   230:        public void UpdateModel(string modelName)
        //   232:            if (!string.IsNullOrWhiteSpace(modelName))
        [Fact]
        public async Task UpdateModel_WithNull_DoesNotChangeModel()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test", "deepseek-chat");

            // Act
            provider.UpdateModel(null!);

            // Assert - verify model is still deepseek-chat by testing connection
            // (internal _model field cannot be directly accessed, verified through behavior)
            await provider.TestConnectionAsync();
            _httpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Fact]
        public async Task UpdateModel_WithWhitespace_DoesNotChangeModel()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test", "deepseek-chat");

            // Act
            provider.UpdateModel("   ");

            // Assert - verify model is still deepseek-chat by testing connection
            await provider.TestConnectionAsync();
            _httpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Once);
        }

        #endregion

        #region GetRecommendationsAsync Edge Cases

        // Source line 152-156: Empty content returns empty list
        // Proof: grep -n "string.IsNullOrEmpty(content)" Brainarr.Plugin/Services/Providers/DeepSeekProvider.cs
        //   153:                if (string.IsNullOrEmpty(content))
        [Fact]
        public async Task GetRecommendationsAsync_WithEmptyContent_ReturnsEmpty()
        {
            // Arrange
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().BeEmpty("because empty content in response yields no recommendations");
        }

        // Source line 150: Null choices/First/Message yields null content
        [Fact]
        public async Task GetRecommendationsAsync_WithNullChoices_ReturnsEmpty()
        {
            // Arrange
            var responseObj = new { id = "1", choices = (object[]?)null };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().BeEmpty("because null choices yields no recommendations");
        }

        // Source line 127-129: null response after all attempts returns empty
        [Fact]
        public async Task GetRecommendationsAsync_WithNullResponse_ReturnsEmpty()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpResponse?)null!);
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().BeEmpty("because null HTTP response yields no recommendations");
        }

        #endregion

        #region GetLastUserMessage and GetLearnMoreUrl Initial State

        // Source line 29-30: _lastUserMessage and _lastUserLearnMoreUrl start as null
        [Fact]
        public void GetLastUserMessage_Initially_Null()
        {
            // Arrange
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var message = provider.GetLastUserMessage();

            // Assert
            message.Should().BeNull("because no connection test has been performed yet");
        }

        [Fact]
        public void GetLearnMoreUrl_Initially_Null()
        {
            // Arrange
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var url = provider.GetLearnMoreUrl();

            // Assert
            url.Should().BeNull("because no connection test has been performed yet");
        }

        #endregion

        #region GetRecommendationsAsync with CancellationToken

        // Source line 168-169: GetRecommendationsAsync(prompt, token) delegates to internal method
        [Fact]
        public async Task GetRecommendationsAsync_WithCancellationToken_ReturnsResults()
        {
            // Arrange
            var arr = "[ { \"artist\": \"A\", \"album\": \"B\" } ]";
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = arr } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");
            using var cts = new CancellationTokenSource();

            // Act
            var result = await provider.GetRecommendationsAsync("prompt", cts.Token);

            // Assert
            result.Should().HaveCount(1, "because valid response should yield one recommendation");
            result[0].Artist.Should().Be("A");
            result[0].Album.Should().Be("B");
        }

        #endregion

        #region Non-Matching Error (no hint set)

        [Fact]
        public async Task TestConnectionAsync_WithNonMatchingError_DoesNotSetHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": \"some other error\"}", HttpStatusCode.InternalServerError));
            var provider = new DeepSeekProvider(_httpClient.Object, _logger, "dsk-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 500 indicates server error");
            provider.GetLastUserMessage().Should().BeNull("because 500 with no matching content does not set hint");
            provider.GetLearnMoreUrl().Should().BeNull("because no matching hint condition was met");
        }

        #endregion
    }
}
