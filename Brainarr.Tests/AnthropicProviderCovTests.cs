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
    /// Coverage tests for AnthropicProvider paths not covered by AnthropicProviderTests.
    /// Tests focus on: constructor validation, thinking extension parsing, error hints,
    /// UpdateModel edge cases, and cancellation.
    /// </summary>
    public class AnthropicProviderCovTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Logger _logger;

        public AnthropicProviderCovTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
        }

        #region Constructor Validation

        // Source line 56: _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   56:            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        [Fact]
        public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new AnthropicProvider(null!, _logger, "sk-ant-test");

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClient");
        }

        // Source line 57: _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   57:            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new AnthropicProvider(_httpClient.Object, null!, "sk-ant-test");

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        // Source line 60-61: if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException(...)
        // Proof: grep -n "throw new ArgumentException" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   60:                throw new ArgumentException("Anthropic API key is required", nameof(apiKey));
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithInvalidApiKey_ThrowsArgumentException(string? apiKey)
        {
            // Act
            var act = () => new AnthropicProvider(_httpClient.Object, _logger, apiKey!);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("apiKey")
                .WithMessage("*Anthropic API key is required*");
        }

        #endregion

        #region ProviderName Property

        // Source line 36: public string ProviderName => "Anthropic";
        // Proof: grep -n "ProviderName" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   36:        public string ProviderName => "Anthropic";
        [Fact]
        public void ProviderName_ReturnsAnthropic()
        {
            // Arrange
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var name = provider.ProviderName;

            // Assert
            name.Should().Be("Anthropic", "because this is the Anthropic provider implementation");
        }

        #endregion

        #region Constructor with #thinking Extension

        // Source line 70-93: Constructor parses #thinking extension in model name
        // Proof: grep -n "#thinking" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs | head -3
        //   70:            if (_model.Contains("#thinking", StringComparison.Ordinal))
        [Fact]
        public void Constructor_WithThinkingExtension_SetsThinkingEnabled()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test", "claude-3-5-sonnet-latest#thinking");

            // Assert
            provider.ProviderName.Should().Be("Anthropic");
        }

        // Source line 75-86: Parse #thinking(tokens=8000) syntax
        // Proof: grep -n "tokens=" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   78:                        if (inside.StartsWith("tokens=", StringComparison.OrdinalIgnoreCase))
        [Fact]
        public void Constructor_WithThinkingAndTokensExtension_ParsesBudget()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test", "claude-3-5-sonnet-latest#thinking(tokens=8000)");

            // Assert
            provider.ProviderName.Should().Be("Anthropic");
        }

        // Source line 75-86: Parse #thinking(8000) shorthand syntax
        // Proof: grep -n "int.TryParse.*budget" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   84:                        if (int.TryParse(inside, out var budget) && budget > 0)
        [Fact]
        public void Constructor_WithThinkingAndNumberOnlyExtension_ParsesBudget()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test", "claude-3-5-sonnet-latest#thinking(12000)");

            // Assert
            provider.ProviderName.Should().Be("Anthropic");
        }

        #endregion

        #region UpdateModel with #thinking Extension

        // Source line 371-414: UpdateModel also parses #thinking extension
        // Proof: grep -n "UpdateModel" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   371:        public void UpdateModel(string modelName)
        [Fact]
        public void UpdateModel_WithThinkingExtension_SetsThinkingEnabled()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");

            // Act
            provider.UpdateModel("claude-3-5-sonnet-latest#thinking");

            // Assert
            provider.ProviderName.Should().Be("Anthropic");
        }

        [Fact]
        public void UpdateModel_WithThinkingAndTokensExtension_ParsesBudget()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");

            // Act
            provider.UpdateModel("claude-3-5-sonnet-latest#thinking(tokens=5000)");

            // Assert
            provider.ProviderName.Should().Be("Anthropic");
        }

        // Source line 373: if (!string.IsNullOrWhiteSpace(modelName))
        // Proof: grep -n "if.*string.IsNullOrWhiteSpace" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   373:            if (!string.IsNullOrWhiteSpace(modelName))
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UpdateModel_WithInvalidValue_DoesNotUpdate(string? modelName)
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");

            // Act
            provider.UpdateModel(modelName!);

            // Assert - provider should still work with original model
            var result = provider.TestConnectionAsync().Result;
            result.Should().BeTrue("because the original model should still be used");
        }

        #endregion

        #region GetLastUserMessage and GetLearnMoreUrl - Initial State

        // Source line 417: public string? GetLastUserMessage() => _lastUserMessage;
        // Source line 418: public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;
        // Proof: grep -n "GetLastUserMessage\|GetLearnMoreUrl" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   417:        public string? GetLastUserMessage() => _lastUserMessage;
        //   418:        public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;
        [Fact]
        public void GetLastUserMessage_Initially_ReturnsNull()
        {
            // Arrange
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var message = provider.GetLastUserMessage();

            // Assert
            message.Should().BeNull("because no error has occurred yet");
        }

        [Fact]
        public void GetLearnMoreUrl_Initially_ReturnsNull()
        {
            // Arrange
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var url = provider.GetLearnMoreUrl();

            // Assert
            url.Should().BeNull("because no error has occurred yet");
        }

        #endregion

        #region TryCaptureAnthropicHint - Error Hints via TestConnectionAsync

        // Source line 429-432: 401 or "authentication_error" sets invalid key hint
        // Proof: grep -n "authentication_error" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   430:                if (status == 401 || content.IndexOf("authentication_error", StringComparison.OrdinalIgnoreCase) >= 0)
        [Fact]
        public async Task TestConnectionAsync_With401Status_SetsInvalidKeyHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.Unauthorized));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 401 indicates authentication failure");
            provider.GetLastUserMessage().Should().Contain("Invalid Anthropic API key", "because 401 indicates invalid key");
            provider.GetLearnMoreUrl().Should().NotBeNull("because a help URL should be provided");
        }

        // Source line 430: "authentication_error" in content sets hint
        [Fact]
        public async Task TestConnectionAsync_WithAuthenticationErrorContent_SetsInvalidKeyHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"type\": \"authentication_error\"}}", HttpStatusCode.BadRequest));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("Invalid Anthropic API key", "because content contains authentication_error");
        }

        // Source line 433-435: 402 or "credit" or "insufficient" sets quota hint
        // Proof: grep -n "402.*credit.*insufficient" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   433:                else if (status == 402 || content.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0 || content.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0)
        [Fact]
        public async Task TestConnectionAsync_With402Status_SetsQuotaHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.PaymentRequired));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 402 indicates payment/quota issue");
            provider.GetLastUserMessage().Should().Contain("credits", "because 402 indicates credit/quota issue");
        }

        // Source line 433: "credit" in content sets hint
        [Fact]
        public async Task TestConnectionAsync_WithCreditErrorContent_SetsQuotaHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"credit balance low\"}}", HttpStatusCode.BadRequest));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("credits", "because content contains credit");
        }

        // Source line 433: "insufficient" in content sets hint
        [Fact]
        public async Task TestConnectionAsync_WithInsufficientErrorContent_SetsQuotaHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"insufficient funds\"}}", HttpStatusCode.BadRequest));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("credits", "because content contains insufficient");
        }

        // Source line 437-439: 429 or "rate limit" sets rate limit hint
        // Proof: grep -n "rate limit" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   437:                else if (status == 429 || content.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
        [Fact]
        public async Task TestConnectionAsync_With429Status_SetsRateLimitHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", (HttpStatusCode)429));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 429 indicates rate limiting");
            provider.GetLastUserMessage().Should().Contain("rate limit", "because 429 indicates rate limit exceeded");
        }

        // Source line 437: "rate limit" in content sets hint
        [Fact]
        public async Task TestConnectionAsync_WithRateLimitContent_SetsRateLimitHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"rate limit exceeded\"}}", HttpStatusCode.BadRequest));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("rate limit", "because content contains rate limit");
        }

        // Source line 441-443: "permission" and "model" sets model access hint
        // Proof: grep -n "permission.*model" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   441:                else if (content.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0 && content.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
        [Fact]
        public async Task TestConnectionAsync_WithPermissionAndModelContent_SetsModelAccessHint()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{\"error\": {\"message\": \"permission denied for this model\"}}", HttpStatusCode.Forbidden));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("model access", "because content contains both permission and model");
        }

        #endregion

        #region TestConnectionAsync with Cancellation

        // Source line 322-363: TestConnectionAsync with CancellationToken
        // Proof: grep -n "TestConnectionAsync.*CancellationToken" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   322:        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        [Fact]
        public async Task TestConnectionAsync_WithCancellationToken_ReturnsTrueOnSuccess()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");
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
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            var act = () => provider.TestConnectionAsync(cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>("because the token was cancelled before the operation started");
        }

        #endregion

        #region GetRecommendationsAsync with Cancellation

        // Source line 295-296: GetRecommendationsAsync with CancellationToken
        // Proof: grep -n "GetRecommendationsAsync.*CancellationToken" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   295:        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        [Fact]
        public async Task GetRecommendationsAsync_WithCancellationToken_ReturnsRecommendations()
        {
            // Arrange
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test", "claude-3-5-haiku-latest");
            var arr = "[ { \"artist\": \"Artist X\", \"album\": \"Album Y\", \"genre\": \"Pop\", \"confidence\": 0.8, \"reason\": \"Test\" } ]";
            var responseObj = new { id = "m1", content = new[] { new { type = "text", text = arr } } };
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

        // Source line 347-363: TestConnectionAsync catches exceptions and returns false
        // Proof: grep -n "catch.*Exception" Brainarr.Plugin/Services/Providers/AnthropicProvider.cs
        //   347:            catch (Exception ex)
        [Fact]
        public async Task TestConnectionAsync_WithHttpException_ReturnsFalse()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Connection failed"));
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

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
            var provider = new AnthropicProvider(_httpClient.Object, _logger, "sk-ant-test");

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because an exception occurred");
        }

        #endregion
    }
}
