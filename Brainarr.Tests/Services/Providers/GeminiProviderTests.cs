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
    public class GeminiProviderTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        [Fact]
        public async Task GetRecommendationsAsync_ParsesCandidatesText()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi");
            var arr = "[ { \"artist\": \"G\", \"album\": \"H\", \"genre\": \"Pop\", \"confidence\": 0.77, \"reason\": \"R\" } ]";
            var responseObj = new { candidates = new[] { new { content = new { parts = new[] { new { text = arr } } } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("H");
        }

        [Fact]
        public async Task GetRecommendationsAsync_ParsesObjectWithRecommendations()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi");
            var payload = new { recommendations = new[] { new { artist = "MM", album = "NN", genre = "OO", confidence = 0.9, reason = "rr" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var responseObj = new { candidates = new[] { new { content = new { parts = new[] { new { text = content } } } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("NN");
        }

        [Fact]
        public async Task GetRecommendationsAsync_InvalidJson_ReturnsEmpty()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi");
            var responseObj = new { candidates = new[] { new { content = new { parts = new[] { new { text = "not-json" } } } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TestConnectionAsync_BadStatus_ReturnsFalse()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("oops", HttpStatusCode.Forbidden));

            (await provider.TestConnectionAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task UpdateModel_Then_TestConnection_Succeeds()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi", "gemini-1.5-flash");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            provider.UpdateModel("gemini-1.5-pro");
            (await provider.TestConnectionAsync()).Should().BeTrue();
        }

        [Fact]
        public async Task GetRecommendationsAsync_EmptyText_ReturnsEmpty()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi");
            var responseObj = new { candidates = new[] { new { content = new { parts = new[] { new { text = "" } } } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_NonOkStatus_ReturnsEmpty()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_NoCandidates_ReturnsEmpty()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi");
            var responseObj = new { candidates = new object[] { } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_NonOk_WithErrorDetails_ReturnsEmpty()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi");
            var errorObj = new { error = new { code = 403, status = "PERMISSION_DENIED", message = "quota" } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(errorObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.Forbidden));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }
    
        [Fact]
        public async Task GetRecommendationsAsync_NonOk_ReturnsEmpty()
        {
            var provider = new GeminiProvider(_http.Object, _logger, "gapi");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }
    }
}
