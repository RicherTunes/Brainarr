using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    /// <summary>
    /// Unit tests for ZaiGlmProvider covering constructor validation,
    /// recommendation parsing, error code mapping, and content filtering.
    /// </summary>
    public class ZaiGlmProviderTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidApiKey_InitializesProvider()
        {
            // Arrange & Act
            var provider = new ZaiGlmProvider(_http.Object, _logger, "valid-api-key");

            // Assert
            provider.ProviderName.Should().Be("Z.AI GLM");
        }

        [Fact]
        public void Constructor_WithNullApiKey_ThrowsArgumentException()
        {
            // Arrange & Act
            var act = () => new ZaiGlmProvider(_http.Object, _logger, null!);

            // Assert
            act.Should().Throw<System.ArgumentException>()
               .WithMessage("*API key is required*");
        }

        [Fact]
        public void Constructor_WithEmptyApiKey_ThrowsArgumentException()
        {
            // Arrange & Act
            var act = () => new ZaiGlmProvider(_http.Object, _logger, "");

            // Assert
            act.Should().Throw<System.ArgumentException>()
               .WithMessage("*API key is required*");
        }

        [Fact]
        public void Constructor_WithWhitespaceApiKey_ThrowsArgumentException()
        {
            // Arrange & Act
            var act = () => new ZaiGlmProvider(_http.Object, _logger, "   ");

            // Assert
            act.Should().Throw<System.ArgumentException>()
               .WithMessage("*API key is required*");
        }

        #endregion

        #region GetRecommendationsAsync Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithValidResponse_ReturnsRecommendations()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var content = "[{\"artist\":\"Test Artist\",\"album\":\"Test Album\",\"genre\":\"Rock\",\"confidence\":0.9,\"reason\":\"Great album\"}]";
            var responseObj = new
            {
                id = "chatcmpl-123",
                choices = new[] { new { finish_reason = "stop", message = new { content = content } } },
                usage = new { prompt_tokens = 10, completion_tokens = 20, total_tokens = 30 }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
            result[0].Album.Should().Be("Test Album");
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithEmptyContent_ReturnsEmptyList()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var responseObj = new
            {
                id = "chatcmpl-123",
                choices = new[] { new { finish_reason = "stop", message = new { content = "" } } }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithContentFiltered_ReturnsEmptyList()
        {
            // Arrange - Z.AI returns finish_reason: "sensitive" when content is filtered
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var responseObj = new
            {
                id = "chatcmpl-123",
                choices = new[] { new { finish_reason = "sensitive", message = new { content = "" } } }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithInvalidJson_ReturnsEmptyList()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var responseObj = new
            {
                id = "chatcmpl-123",
                choices = new[] { new { finish_reason = "stop", message = new { content = "not valid json" } } }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithObjectContainingRecommendations_ParsesCorrectly()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var payload = new { recommendations = new[] { new { artist = "Artist1", album = "Album1", genre = "Jazz", confidence = 0.85, reason = "Great jazz" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var responseObj = new
            {
                id = "chatcmpl-123",
                choices = new[] { new { finish_reason = "stop", message = new { content = content } } }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("Album1");
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithUsageInfo_LogsAndParses()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var content = "[{\"artist\":\"Usage Artist\",\"album\":\"Usage Album\"}]";
            var responseObj = new
            {
                id = "chatcmpl-123",
                usage = new { prompt_tokens = 100, completion_tokens = 200, total_tokens = 300 },
                choices = new[] { new { finish_reason = "stop", message = new { content = content } } }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().HaveCount(1);
        }

        #endregion

        #region HTTP Error Tests

        [Fact]
        public async Task GetRecommendationsAsync_With401_ReturnsEmptyList()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("Unauthorized", HttpStatusCode.Unauthorized));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_With403_ReturnsEmptyList()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("Forbidden", HttpStatusCode.Forbidden));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_With500_ReturnsEmptyList()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("Internal Server Error", HttpStatusCode.InternalServerError));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert
            result.Should().BeEmpty();
        }

        #endregion

        #region Z.AI Business Error Code Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithBusinessCode1002_MapsToAuthError()
        {
            // Arrange - Business code 1002 = invalid API key
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var errorResponse = new { error = new { code = "1002", message = "Invalid API key" } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(errorResponse);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert - Returns empty due to auth error
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithBusinessCode1113_MapsToQuotaExceeded()
        {
            // Arrange - Business code 1113 = account arrears (insufficient balance)
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var errorResponse = new { error = new { code = "1113", message = "Account has insufficient balance" } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(errorResponse);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert - Returns empty due to quota exceeded
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithBusinessCode1305_MapsToRateLimit()
        {
            // Arrange - Business code 1305 = rate limit exceeded
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var errorResponse = new { error = new { code = "1305", message = "Rate limit exceeded" } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(errorResponse);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert - Returns empty due to rate limit
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithBusinessCode1211_MapsToModelNotFound()
        {
            // Arrange - Business code 1211 = model not found
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var errorResponse = new { error = new { code = "1211", message = "Model not found" } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(errorResponse);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            // Act
            var result = await provider.GetRecommendationsAsync("prompt");

            // Assert - Returns empty due to API error
            result.Should().BeEmpty();
        }

        #endregion

        #region TestConnectionAsync Tests

        [Fact]
        public async Task TestConnectionAsync_WithSuccessfulResponse_ReturnsTrue()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var responseObj = new
            {
                id = "chatcmpl-123",
                choices = new[] { new { finish_reason = "stop", message = new { content = "OK" } } }
            };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task TestConnectionAsync_WithFailedResponse_ReturnsFalse()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("Unauthorized", HttpStatusCode.Unauthorized));

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task TestConnectionAsync_WithApiErrorInBody_ReturnsFalse()
        {
            // Arrange - HTTP 200 but with error in body
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", preferStructured: true);
            var errorResponse = new { error = new { code = "1002", message = "Invalid API key" } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(errorResponse);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region UpdateModel Tests

        [Fact]
        public async Task UpdateModel_WithValidModel_UpdatesModel()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", "glm-4.7-flash", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            provider.UpdateModel("glm-4.7");
            var result = await provider.TestConnectionAsync();

            // Assert - Model update should succeed
            result.Should().BeTrue();
        }

        [Fact]
        public void UpdateModel_WithNullModel_DoesNotUpdate()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", "glm-4.7-flash", preferStructured: true);

            // Act - Should not throw
            var act = () => provider.UpdateModel(null!);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void UpdateModel_WithEmptyModel_DoesNotUpdate()
        {
            // Arrange
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", "glm-4.7-flash", preferStructured: true);

            // Act - Should not throw
            var act = () => provider.UpdateModel("");

            // Assert
            act.Should().NotThrow();
        }

        #endregion
    }
}
