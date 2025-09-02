using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    public class LMStudioProviderTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        [Fact]
        public async Task GetRecommendationsAsync_ParsesChoicesContentArray()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            var arr = "[ { \"artist\": \"K\", \"album\": \"L\", \"genre\": \"Alt\", \"confidence\": 0.9, \"reason\": \"R\" } ]";
            var responseObj = new { choices = new[] { new { message = new { content = arr } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRecommendationsAsync_TextFallback_ParsesDashSeparatedPairs()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            var content = "Pink Floyd - The Wall\nDaft Punk - Discovery";
            var responseObj = new { choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(2);
            result[0].Artist.Should().Be("Pink Floyd");
        }

        [Fact]
        public async Task TestConnectionAsync_Error_ReturnsFalse()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));

            (await provider.TestConnectionAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task TestConnectionAsync_Ok_ReturnsTrue()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            (await provider.TestConnectionAsync()).Should().BeTrue();
        }

        [Fact]
        public async Task GetRecommendationsAsync_NoJsonNoDash_ReturnsEmpty()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            var content = "narrative response without JSON";
            var responseObj = new { choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_HandlesBOMPrefixedContent()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            var arr = "[ { \"artist\": \"BOM\", \"album\": \"Prefixed\" } ]";
            var contentWithBom = "\uFEFF" + arr;
            var responseObj = new { choices = new[] { new { message = new { content = contentWithBom } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_EmptyMessageContent_ReturnsEmpty()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            var responseObj = new { choices = new[] { new { message = new { content = "" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_MissingContentProperty_ReturnsEmpty()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            var responseObj = new { choices = new[] { new { message = new { } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_TextFallback_EnDashSeparator_Parses()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            var content = "Radiohead – OK Computer"; // en-dash
            var responseObj = new { choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().ContainSingle(r => r.Album == "OK Computer");
        }

        [Fact]
        public async Task GetRecommendationsAsync_TextFallback_Bulleted_ParsesAndTrims()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            var content = "• 1) Pink Floyd - Wish You Were Here";
            var responseObj = new { choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().ContainSingle(r => r.Album == "Wish You Were Here");
        }

        [Fact]
        public async Task GetRecommendationsAsync_OkStatus_InvalidJson_ReturnsEmpty()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            // Simulate 200 OK with non-JSON body to hit JsonException path
            var response = "this is not json";
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }
        [Fact]
        public async Task GetRecommendationsAsync_NonOk_ReturnsEmpty()
        {
            var provider = new LMStudioProvider("http://localhost:1234", "test-model", _http.Object, _logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }
    }
}
