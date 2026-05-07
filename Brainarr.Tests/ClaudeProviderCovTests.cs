using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for ClaudeCodeSubscriptionProvider paths not covered by ClaudeCodeSubscriptionProviderTests.
    /// Tests focus on: 402 credit errors, HttpException handling, GetLearnMoreUrl on auth errors.
    /// </summary>
    public class ClaudeProviderCovTests : IDisposable
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Logger _logger;
        private readonly string _tempDir;
        private readonly string _credentialsPath;

        public ClaudeProviderCovTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
            _tempDir = Path.Combine(Path.GetTempPath(), $"brainarr_claude_cov_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _credentialsPath = Path.Combine(_tempDir, ".credentials.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        private void CreateValidCredentials()
        {
            var futureExpiry = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();
            var json = $@"{{
                ""claudeAiOauth"": {{
                    ""accessToken"": ""test-token-cov"",
                    ""expiresAt"": {futureExpiry},
                    ""refreshToken"": ""refresh-cov"",
                    ""subscriptionType"": ""max""
                }}
            }}";
            File.WriteAllText(_credentialsPath, json);
        }

        #region TryCaptureHint - 402 Credit Exhausted

        // Source line 259: else if (status == 402 || content.IndexOf("credit"...)
        // Proof: grep -n "402\|credit" Brainarr.Plugin/Services/Providers/ClaudeCodeSubscriptionProvider.cs
        //   259:                else if (status == 402 || content.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0)
        //   261:                    _lastUserMessage = "Claude subscription credits exhausted. Check your subscription status.";
        [Fact]
        public async Task GetRecommendationsAsync_With402Status_SetsCreditExhaustedMessage()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.PaymentRequired));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty("because 402 indicates payment/credit issue");
            provider.GetLastUserMessage().Should().Contain("credits", "because 402 status should set credit exhausted message");
        }

        // Source line 259: content.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0
        [Fact]
        public async Task GetRecommendationsAsync_WithCreditInContent_SetsCreditExhaustedMessage()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"credit balance depleted\"}}",
                    HttpStatusCode.BadRequest));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty("because the request failed");
            provider.GetLastUserMessage().Should().Contain("credits", "because content contains 'credit'");
        }

        #endregion

        #region TryCaptureHint - 401 with LearnMoreUrl

        // Source line 256-258: 401 or "authentication_error" sets message and LearnMoreUrl
        // Proof: grep -n "401\|authentication_error\|LearnMoreUrl" Brainarr.Plugin/Services/Providers/ClaudeCodeSubscriptionProvider.cs
        //   254:                if (status == 401 || content.IndexOf("authentication_error", StringComparison.OrdinalIgnoreCase) >= 0)
        //   256:                    _lastUserMessage = "Claude Code authentication failed. Your subscription token may have expired. Run 'claude login' to refresh.";
        //   257:                    _lastUserLearnMoreUrl = "https://docs.anthropic.com/claude/reference/getting-started-with-the-api";
        [Fact]
        public async Task GetRecommendationsAsync_With401Status_SetsLearnMoreUrl()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.Unauthorized));

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert
            provider.GetLastUserMessage().Should().Contain("authentication", "because 401 indicates auth failure");
            provider.GetLearnMoreUrl().Should().Be("https://docs.anthropic.com/claude/reference/getting-started-with-the-api",
                "because 401 should set the LearnMore URL");
        }

        // Source line 254: content.IndexOf("authentication_error", StringComparison.OrdinalIgnoreCase) >= 0
        [Fact]
        public async Task GetRecommendationsAsync_WithAuthenticationErrorInContent_SetsLearnMoreUrl()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"type\": \"authentication_error\"}}",
                    HttpStatusCode.InternalServerError));

            // Act
            await provider.GetRecommendationsAsync("test prompt");

            // Assert
            provider.GetLastUserMessage().Should().Contain("authentication", "because content contains 'authentication_error'");
            provider.GetLearnMoreUrl().Should().Be("https://docs.anthropic.com/claude/reference/getting-started-with-the-api",
                "because authentication_error in content should set the LearnMore URL");
        }

        #endregion

        #region TestConnectionAsync - HttpException Handling

        // Source line 226-229: catch (Exception ex) { if (ex is HttpException httpEx) TryCaptureHint(...) }
        // Proof: grep -n "HttpException" Brainarr.Plugin/Services/Providers/ClaudeCodeSubscriptionProvider.cs
        //   226:                if (ex is HttpException httpEx)
        [Fact]
        public async Task TestConnectionAsync_WithHttpException_ReturnsFalse()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            var request = new HttpRequest("http://test.local");
            var response = Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.InternalServerError);
            var httpException = new HttpException(response);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(httpException);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because HttpException was thrown");
        }

        // Source line 227-229: TryCaptureHint(httpEx.Response?.Content, (int)(httpEx.Response?.StatusCode ?? 0))
        // Note: TryCaptureHint is called with httpEx.Response?.Content and status code from HttpException
        [Fact]
        public async Task TestConnectionAsync_WithHttpException401_ReturnsFalse()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            var response = Helpers.HttpResponseFactory.CreateResponse("authentication_error", HttpStatusCode.Unauthorized);
            var httpException = new HttpException(response);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(httpException);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because HttpException was thrown");
        }

        #endregion

        #region TestConnectionAsync - 402 Credit Errors

        // Source line 259-261: 402 status sets credit exhausted message
        [Fact]
        public async Task TestConnectionAsync_With402Status_SetsCreditExhaustedMessage()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.PaymentRequired));

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because 402 indicates credit issue");
            provider.GetLastUserMessage().Should().Contain("credits", "because 402 should set credit exhausted message");
        }

        // Source line 259: content.IndexOf("credit"...)
        [Fact]
        public async Task TestConnectionAsync_WithCreditInContent_SetsCreditExhaustedMessage()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"insufficient credit\"}}",
                    HttpStatusCode.BadRequest));

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse("because the request failed");
            provider.GetLastUserMessage().Should().Contain("credits", "because content contains 'credit'");
        }

        #endregion

        #region TestConnectionAsync - 401 with LearnMoreUrl

        // Source line 254-257: 401 sets LearnMoreUrl
        [Fact]
        public async Task TestConnectionAsync_With401Status_SetsLearnMoreUrl()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.Unauthorized));

            // Act
            await provider.TestConnectionAsync();

            // Assert
            provider.GetLearnMoreUrl().Should().Be("https://docs.anthropic.com/claude/reference/getting-started-with-the-api",
                "because 401 should set the LearnMore URL");
        }

        // Source line 254: "authentication_error" in content also sets LearnMoreUrl
        [Fact]
        public async Task TestConnectionAsync_WithAuthenticationErrorInContent_SetsLearnMoreUrl()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"type\": \"authentication_error\"}}",
                    HttpStatusCode.BadRequest));

            // Act
            await provider.TestConnectionAsync();

            // Assert
            provider.GetLearnMoreUrl().Should().Be("https://docs.anthropic.com/claude/reference/getting-started-with-the-api",
                "because authentication_error in content should set the LearnMore URL");
        }

        #endregion

        #region GetRecommendationsAsync - Cancellation Token Overload

        // Source line 68-69: GetRecommendationsAsync(string) calls GetRecommendationsAsync(string, CancellationToken)
        // Proof: grep -n "GetRecommendationsAsync" Brainarr.Plugin/Services/Providers/ClaudeCodeSubscriptionProvider.cs
        //   66:        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        //   68:            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
        //   69:            return await GetRecommendationsAsync(prompt, cts.Token);
        [Fact]
        public async Task GetRecommendationsAsync_WithoutCancellationToken_ReturnsResults()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            var recommendations = @"[{ ""artist"": ""Test Artist"", ""album"": ""Test Album"", ""genre"": ""Rock"", ""confidence"": 0.9, ""reason"": ""Test"" }]";
            var responseObj = new { content = new[] { new { type = "text", text = recommendations } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().HaveCount(1, "because one recommendation was returned");
            result[0].Artist.Should().Be("Test Artist", "because that is the artist in the response");
        }

        #endregion

        #region TestConnectionAsync - Cancellation Token Overload

        // Source line 170-173: TestConnectionAsync() calls TestConnectionAsync(CancellationToken)
        // Proof: grep -n "TestConnectionAsync" Brainarr.Plugin/Services/Providers/ClaudeCodeSubscriptionProvider.cs
        //   170:        public async Task<bool> TestConnectionAsync()
        //   172:            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout));
        //   173:            return await TestConnectionAsync(cts.Token);
        [Fact]
        public async Task TestConnectionAsync_WithoutCancellationToken_ReturnsTrue()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            var responseObj = new { content = new[] { new { type = "text", text = "OK" } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeTrue("because the connection test succeeded");
        }

        #endregion

        #region TryCaptureHint - Null Body Handling

        // Source line 248-252: TryCaptureHint handles null body with body ?? string.Empty
        // Proof: grep -n "TryCaptureHint\|body.*string.Empty" Brainarr.Plugin/Services/Providers/ClaudeCodeSubscriptionProvider.cs
        //   246:        private void TryCaptureHint(string? body, int status)
        //   252:                var content = body ?? string.Empty;
        [Fact]
        public async Task GetRecommendationsAsync_WithNullContentInError_HandlesGracefully()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            // Create response that will return null content for error status
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("", HttpStatusCode.InternalServerError));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty("because the request failed");
            // Should not throw - TryCaptureHint handles null body
        }

        #endregion

        #region ProviderName Property

        // Source line 34: public string ProviderName => "Claude Code (Subscription)";
        // Proof: grep -n "ProviderName" Brainarr.Plugin/Services/Providers/ClaudeCodeSubscriptionProvider.cs
        //   34:        public string ProviderName => "Claude Code (Subscription)";
        [Fact]
        public void ProviderName_ReturnsClaudeCodeSubscription()
        {
            // Arrange
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_httpClient.Object, _logger, _credentialsPath);

            // Act
            var name = provider.ProviderName;

            // Assert
            name.Should().Be("Claude Code (Subscription)", "because this is the Claude Code subscription provider");
        }

        #endregion
    }
}
