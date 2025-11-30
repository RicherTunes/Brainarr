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

namespace Brainarr.Tests.Services.Providers
{
    public class ClaudeCodeSubscriptionProviderTests : IDisposable
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;
        private readonly string _tempDir;
        private readonly string _credentialsPath;

        public ClaudeCodeSubscriptionProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
            _tempDir = Path.Combine(Path.GetTempPath(), $"brainarr_claude_test_{Guid.NewGuid():N}");
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

        private void CreateValidCredentials(int daysUntilExpiry = 7)
        {
            var futureExpiry = DateTimeOffset.UtcNow.AddDays(daysUntilExpiry).ToUnixTimeMilliseconds();
            var json = $@"{{
                ""claudeAiOauth"": {{
                    ""accessToken"": ""test-token-12345"",
                    ""expiresAt"": {futureExpiry},
                    ""refreshToken"": ""refresh-token-abc"",
                    ""subscriptionType"": ""max""
                }}
            }}";
            File.WriteAllText(_credentialsPath, json);
        }

        #region Constructor Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_WithMissingCredentials_SetsLastUserMessage()
        {
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            provider.GetLastUserMessage().Should().NotBeNullOrEmpty();
            provider.GetLastUserMessage().Should().Contain("not found");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_WithValidCredentials_Succeeds()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            provider.ProviderName.Should().Be("Claude Code (Subscription)");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_WithCustomModel_UsesModel()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath, "claude-3-opus-20240229");

            provider.Should().NotBeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ThrowsOnNullHttpClient()
        {
            var action = () => new ClaudeCodeSubscriptionProvider(null!, _logger, _credentialsPath);
            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ThrowsOnNullLogger()
        {
            var action = () => new ClaudeCodeSubscriptionProvider(_http.Object, null!, _credentialsPath);
            action.Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region GetRecommendationsAsync Tests

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithMissingCredentials_ReturnsEmpty()
        {
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            var result = await provider.GetRecommendationsAsync("recommend albums like Radiohead");

            result.Should().BeEmpty();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithValidResponse_ParsesRecommendations()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            var recommendations = @"[
                { ""artist"": ""Radiohead"", ""album"": ""OK Computer"", ""genre"": ""Alternative Rock"", ""confidence"": 0.95, ""reason"": ""Classic album"" },
                { ""artist"": ""Portishead"", ""album"": ""Dummy"", ""genre"": ""Trip Hop"", ""confidence"": 0.88, ""reason"": ""Similar mood"" }
            ]";
            var responseObj = new { content = new[] { new { type = "text", text = recommendations } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("recommend albums like Radiohead");

            result.Should().HaveCount(2);
            result[0].Artist.Should().Be("Radiohead");
            result[0].Album.Should().Be("OK Computer");
            result[1].Artist.Should().Be("Portishead");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithArtistOnlyPrompt_ParsesArtists()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            var recommendations = @"[
                { ""artist"": ""Massive Attack"", ""genre"": ""Trip Hop"", ""confidence"": 0.92, ""reason"": ""Pioneered the genre"" }
            ]";
            var responseObj = new { content = new[] { new { type = "text", text = recommendations } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("recommend artists similar to Portishead");

            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Massive Attack");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithEmptyResponse_ReturnsEmpty()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            var responseObj = new { content = new[] { new { type = "text", text = "" } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");

            result.Should().BeEmpty();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithErrorStatus_ReturnsEmpty()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.InternalServerError));

            var result = await provider.GetRecommendationsAsync("prompt");

            result.Should().BeEmpty();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_With401Error_SetsUserMessage()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("authentication_error", HttpStatusCode.Unauthorized));

            await provider.GetRecommendationsAsync("prompt");

            provider.GetLastUserMessage().Should().Contain("authentication");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_With429Error_SetsRateLimitMessage()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("rate limit exceeded", HttpStatusCode.TooManyRequests));

            await provider.GetRecommendationsAsync("prompt");

            provider.GetLastUserMessage().Should().Contain("rate limit");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithCancellation_RespectsToken()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ThrowsAsync(new OperationCanceledException());

            var result = await provider.GetRecommendationsAsync("prompt", cts.Token);

            result.Should().BeEmpty();
        }

        #endregion

        #region TestConnectionAsync Tests

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TestConnectionAsync_WithMissingCredentials_ReturnsFalse()
        {
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            var result = await provider.TestConnectionAsync();

            result.Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TestConnectionAsync_WithValidResponse_ReturnsTrue()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            var responseObj = new { content = new[] { new { type = "text", text = "OK" } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.TestConnectionAsync();

            result.Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TestConnectionAsync_WithErrorResponse_ReturnsFalse()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.BadRequest));

            var result = await provider.TestConnectionAsync();

            result.Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TestConnectionAsync_WithException_ReturnsFalse()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ThrowsAsync(new Exception("Network error"));

            var result = await provider.TestConnectionAsync();

            result.Should().BeFalse();
        }

        #endregion

        #region UpdateModel Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void UpdateModel_WithValidModel_Updates()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            var updateAction = () => provider.UpdateModel("claude-3-opus-20240229");

            updateAction.Should().NotThrow();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void UpdateModel_WithEmptyModel_DoesNotUpdate()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            var updateAction = () => provider.UpdateModel("");

            updateAction.Should().NotThrow();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void UpdateModel_WithWhitespaceModel_DoesNotUpdate()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            var updateAction = () => provider.UpdateModel("   ");

            updateAction.Should().NotThrow();
        }

        #endregion

        #region GetLastUserMessage/GetLearnMoreUrl Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void GetLastUserMessage_Initially_ReturnsNullOrCredentialError()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            // With valid credentials, no error message
            provider.GetLastUserMessage().Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetLearnMoreUrl_Initially_ReturnsNull()
        {
            CreateValidCredentials();
            var provider = new ClaudeCodeSubscriptionProvider(_http.Object, _logger, _credentialsPath);

            provider.GetLearnMoreUrl().Should().BeNull();
        }

        #endregion
    }
}
