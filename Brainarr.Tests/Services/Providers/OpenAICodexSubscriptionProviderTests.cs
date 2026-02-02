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
    public class OpenAICodexSubscriptionProviderTests : IDisposable
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;
        private readonly string _tempDir;
        private readonly string _authPath;

        public OpenAICodexSubscriptionProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
            _tempDir = Path.Combine(Path.GetTempPath(), $"brainarr_codex_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _authPath = Path.Combine(_tempDir, "auth.json");
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
            var futureExpiry = DateTimeOffset.UtcNow.AddDays(daysUntilExpiry).ToUnixTimeSeconds();
            var json = $@"{{
                ""tokens"": {{
                    ""access_token"": ""openai-token-xyz"",
                    ""expires_at"": {futureExpiry},
                    ""refresh_token"": ""openai-refresh-token""
                }}
            }}";
            File.WriteAllText(_authPath, json);
        }

        private void CreateDirectApiKeyCredentials()
        {
            var json = @"{
                ""OPENAI_API_KEY"": ""sk-direct-api-key-12345""
            }";
            File.WriteAllText(_authPath, json);
        }

        #region Constructor Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_WithMissingCredentials_SetsLastUserMessage()
        {
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            provider.GetLastUserMessage().Should().NotBeNullOrEmpty();
            provider.GetLastUserMessage().Should().Contain("not found");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_WithValidCredentials_Succeeds()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            provider.ProviderName.Should().Be("OpenAI Codex (Subscription)");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_WithDirectApiKey_Succeeds()
        {
            CreateDirectApiKeyCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            provider.ProviderName.Should().Be("OpenAI Codex (Subscription)");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_WithCustomModel_UsesModel()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath, "gpt-4o-mini");

            provider.Should().NotBeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ThrowsOnNullHttpClient()
        {
            var action = () => new OpenAICodexSubscriptionProvider(null!, _logger, _authPath);
            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ThrowsOnNullLogger()
        {
            var action = () => new OpenAICodexSubscriptionProvider(_http.Object, null!, _authPath);
            action.Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region GetRecommendationsAsync Tests

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithMissingCredentials_ReturnsEmpty()
        {
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            var result = await provider.GetRecommendationsAsync("recommend albums like Daft Punk");

            result.Should().BeEmpty();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithValidResponse_ParsesRecommendations()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            var recommendations = @"{""recommendations"": [
                { ""artist"": ""Daft Punk"", ""album"": ""Discovery"", ""genre"": ""Electronic"", ""confidence"": 0.95, ""reason"": ""Iconic album"" },
                { ""artist"": ""Justice"", ""album"": ""Cross"", ""genre"": ""Electronic"", ""confidence"": 0.88, ""reason"": ""French house"" }
            ]}";
            var responseObj = new { choices = new[] { new { message = new { content = recommendations } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("recommend albums like Daft Punk");

            result.Should().HaveCount(2);
            result[0].Artist.Should().Be("Daft Punk");
            result[0].Album.Should().Be("Discovery");
            result[1].Artist.Should().Be("Justice");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithArtistOnlyPrompt_ParsesArtists()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            var recommendations = @"[
                { ""artist"": ""Boards of Canada"", ""genre"": ""IDM"", ""confidence"": 0.91, ""reason"": ""Similar electronic style"" }
            ]";
            var responseObj = new { choices = new[] { new { message = new { content = recommendations } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("recommend artists similar to Aphex Twin");

            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Boards of Canada");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithEmptyResponse_ReturnsEmpty()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            var responseObj = new { choices = new[] { new { message = new { content = "" } } } };
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
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

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
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("invalid_api_key", HttpStatusCode.Unauthorized));

            await provider.GetRecommendationsAsync("prompt");

            provider.GetLastUserMessage().Should().Contain("authentication");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_With402Error_SetsQuotaMessage()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("insufficient_quota", HttpStatusCode.PaymentRequired));

            await provider.GetRecommendationsAsync("prompt");

            provider.GetLastUserMessage().Should().Contain("quota");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_With429Error_SetsRateLimitMessage()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("rate_limit_exceeded", HttpStatusCode.TooManyRequests));

            await provider.GetRecommendationsAsync("prompt");

            provider.GetLastUserMessage().Should().Contain("rate limit");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithModelNotFound_SetsModelMessage()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath, "invalid-model");

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("model_not_found", HttpStatusCode.NotFound));

            await provider.GetRecommendationsAsync("prompt");

            provider.GetLastUserMessage().Should().Contain("Model");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetRecommendationsAsync_WithCancellation_RespectsToken()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

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
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            var result = await provider.TestConnectionAsync();

            result.IsHealthy.Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TestConnectionAsync_WithValidResponse_ReturnsTrue()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            var responseObj = new { choices = new[] { new { message = new { content = "OK" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.TestConnectionAsync();

            result.IsHealthy.Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TestConnectionAsync_WithErrorResponse_ReturnsFalse()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.BadRequest));

            var result = await provider.TestConnectionAsync();

            result.IsHealthy.Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TestConnectionAsync_WithException_ReturnsFalse()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ThrowsAsync(new Exception("Network error"));

            var result = await provider.TestConnectionAsync();

            result.IsHealthy.Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task TestConnectionAsync_WithDirectApiKey_ReturnsTrue()
        {
            CreateDirectApiKeyCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            var responseObj = new { choices = new[] { new { message = new { content = "OK" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.TestConnectionAsync();

            result.IsHealthy.Should().BeTrue();
        }

        #endregion

        #region UpdateModel Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void UpdateModel_WithValidModel_Updates()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            var updateAction = () => provider.UpdateModel("gpt-4o-mini");

            updateAction.Should().NotThrow();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void UpdateModel_WithEmptyModel_DoesNotUpdate()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            var updateAction = () => provider.UpdateModel("");

            updateAction.Should().NotThrow();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void UpdateModel_WithWhitespaceModel_DoesNotUpdate()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

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
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            // With valid credentials, no error message
            provider.GetLastUserMessage().Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetLearnMoreUrl_Initially_ReturnsNull()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            provider.GetLearnMoreUrl().Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetLearnMoreUrl_After401Error_ReturnsUrl()
        {
            CreateValidCredentials();
            var provider = new OpenAICodexSubscriptionProvider(_http.Object, _logger, _authPath);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("invalid_api_key", HttpStatusCode.Unauthorized));

            await provider.GetRecommendationsAsync("prompt");

            provider.GetLearnMoreUrl().Should().NotBeNullOrEmpty();
            provider.GetLearnMoreUrl().Should().Contain("openai.com");
        }

        #endregion
    }
}
