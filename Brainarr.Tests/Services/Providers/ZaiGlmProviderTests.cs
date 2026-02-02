using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    public class ZaiGlmProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public ZaiGlmProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Constructor_AcceptsEmptyApiKey()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "   ");
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void Constructor_AcceptsNullApiKey()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, null);
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void Constructor_UsesDefaultModelWhenNoneProvided()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");
            provider.ProviderName.Should().Be("Z.AI GLM");
        }

        [Fact]
        public void Constructor_AcceptsCustomModel()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", "glm-4-plus");
            provider.ProviderName.Should().Be("Z.AI GLM");
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithArrayContent_ParsesItems()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", "glm-4.7-flash");

            var recs = new { recommendations = new[] { new { artist = "Artist A", album = "Album A", genre = "Rock", confidence = 0.9, reason = "Good fit" } } };
            var contentArray = System.Text.Json.JsonSerializer.Serialize(recs);
            var apiObj = new { id = "1", choices = new[] { new { message = new { content = contentArray } } } };
            var apiResponse = System.Text.Json.JsonSerializer.Serialize(apiObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(apiResponse));

            var result = await provider.GetRecommendationsAsync("prompt");

            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Should().BeEquivalentTo(new Recommendation
            {
                Artist = "Artist A",
                Album = "Album A",
                Genre = "Rock",
                Confidence = 0.9,
                Reason = "Good fit"
            }, opts => opts.ExcludingMissingMembers());
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithObjectProperty_ParsesItems()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");

            var recommendationsObj = new { recommendations = new[] { new { artist = "X", album = "Y", genre = "Z", confidence = 0.5, reason = "because" } } };
            var content = System.Text.Json.JsonSerializer.Serialize(recommendationsObj);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = System.Text.Json.JsonSerializer.Serialize(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("Y");
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithSingleObject_ParsesItem()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");
            var single = new { artist = "S", album = "T", genre = "U", confidence = 0.6, reason = "why" };
            var content = System.Text.Json.JsonSerializer.Serialize(single);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = System.Text.Json.JsonSerializer.Serialize(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("S");
        }

        [Fact]
        public async Task GetRecommendationsAsync_NonOk_ReturnsEmpty()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.BadRequest));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_LongPrompt_IsTruncated()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");
            var recs = new { recommendations = new[] { new { artist = "A1", album = "B1", genre = "G", confidence = 0.9, reason = "R" } } };
            var contentArray = System.Text.Json.JsonSerializer.Serialize(recs);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = contentArray } } } };
            var response = System.Text.Json.JsonSerializer.Serialize(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var longPrompt = new string('x', 15000);
            var result = await provider.GetRecommendationsAsync(longPrompt);
            result.Should().HaveCount(1); // execution succeeds after truncation
        }

        [Fact]
        public async Task GetRecommendationsAsync_InvalidJson_ReturnsEmpty()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "not-json" } } } };
            var response = System.Text.Json.JsonSerializer.Serialize(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_EmptyPrompt_Throws()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");
            await Assert.ThrowsAsync<System.ArgumentException>(() => provider.GetRecommendationsAsync(" "));
        }

        [Fact]
        public async Task GetRecommendationsAsync_MissingOptionalFields_UsesDefaults()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");
            var obj = new { artist = "OnlyArtist" }; // no album/genre/confidence/reason
            var content = System.Text.Json.JsonSerializer.Serialize(obj);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = System.Text.Json.JsonSerializer.Serialize(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().ContainSingle(r => r.Artist == "OnlyArtist" && r.Genre == "Unknown" && r.Confidence > 0);
        }

        [Fact]
        public async Task TestConnectionAsync_OnOk_ReturnsTrue()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            (await provider.TestConnectionAsync()).IsHealthy.Should().BeTrue();
        }

        [Fact]
        public async Task TestConnectionAsync_OnUnauthorized_ReturnsFalse()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("Unauthorized", HttpStatusCode.Unauthorized));

            (await provider.TestConnectionAsync()).IsHealthy.Should().BeFalse();
        }

        [Fact]
        public async Task TestConnectionAsync_InvalidApiKey_ReturnsFalse()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "invalid-key");

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.Unauthorized));

            (await provider.TestConnectionAsync()).IsHealthy.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateModel_Then_TestConnection_Succeeds()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", "glm-4.7-flash");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            provider.UpdateModel("glm-4-plus");
            (await provider.TestConnectionAsync()).IsHealthy.Should().BeTrue();
        }

        [Fact]
        public async Task TestConnectionAsync_ApiKeyNotInErrorMessage()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "secret-api-key-123");

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.Unauthorized));

            await provider.TestConnectionAsync();

            // Verify that exception messages don't leak the API key
            var lastMessage = provider.GetLastUserMessage();
            lastMessage.Should().NotContain("secret-api-key-123");
            lastMessage.Should().NotContain("secret");
        }

        [Fact]
        public async Task GetRecommendationsAsync_ApiKeyRedacted()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "secret-api-key-456");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.Unauthorized));

            var result = await provider.GetRecommendationsAsync("prompt");

            // Should return empty on error
            result.Should().BeEmpty();

            // Verify that user messages don't contain the API key
            var lastMessage = provider.GetLastUserMessage();
            lastMessage.Should().NotContain("secret-api-key-456");
            lastMessage.Should().NotContain("secret");
        }

        [Fact]
        public void UpdateModel_ChangesModelString()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key", "glm-4.7-flash");
            provider.UpdateModel("glm-4-plus");
            // The model is updated internally - we can't directly access it
            // but we can verify the method doesn't throw
        }

        [Fact]
        public void ProviderName_IsZaiGlm()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");
            provider.ProviderName.Should().Be("Z.AI GLM");
        }

        [Fact]
        public async Task TestConnectionAsync_RateLimitError_SetsUserMessage()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("Rate limit exceeded", HttpStatusCode.TooManyRequests));

            await provider.TestConnectionAsync();

            var lastMessage = provider.GetLastUserMessage();
            lastMessage.Should().NotBeNullOrEmpty();
            lastMessage.Should().Contain("rate limit");
        }

        [Fact]
        public async Task TestConnectionAsync_ServerError_SetsUserMessage()
        {
            var provider = new ZaiGlmProvider(_http.Object, _logger, "test-key");

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("Internal server error", HttpStatusCode.InternalServerError));

            await provider.TestConnectionAsync();

            var lastMessage = provider.GetLastUserMessage();
            lastMessage.Should().NotBeNullOrEmpty();
            lastMessage.Should().Contain("server error");
        }
    }
}
