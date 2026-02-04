using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    public class PerplexityProviderTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        [Fact]
        public async Task GetRecommendationsAsync_ParsesContentArray()
        {
            var provider = new PerplexityProvider(_http.Object, _logger, "px-key", "llama-3.1-sonar-large-128k-online", preferStructured: true);
            var arr = "[ { \"artist\": \"C\", \"album\": \"D\", \"genre\": \"Alt\", \"confidence\": 0.85, \"reason\": \"R\" } ]";
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = arr } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRecommendationsAsync_RemovesCitationMarkers()
        {
            var provider = new PerplexityProvider(_http.Object, _logger, "px-key", preferStructured: true);
            var arr = "[ { \"artist\": \"AA\", \"album\": \"BB\", \"genre\": \"CC\", \"confidence\": 0.7, \"reason\": \"RR\" } ]";
            var content = "Here are some picks [1] [12] [3]:\n" + arr;
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRecommendationsAsync_ObjectWithRecommendations_Parses()
        {
            var provider = new PerplexityProvider(_http.Object, _logger, "px-key", preferStructured: true);
            var payload = new { recommendations = new[] { new { artist = "PC", album = "PD", genre = "PG", confidence = 0.6, reason = "pr" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("PD");
        }

        [Fact]
        public async Task GetRecommendationsAsync_ItemParseError_IsCaughtAndContinues()
        {
            var provider = new PerplexityProvider(_http.Object, _logger, "px-key", preferStructured: true);
            var content = "[{\"artist\":null,\"album\":123},{\"artist\":\"OK\",\"album\":\"Good\"}]";
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().ContainSingle(r => r.Artist == "OK");
        }

        [Fact]
        public async Task TestConnectionAsync_OnOk_ReturnsTrue()
        {
            var provider = new PerplexityProvider(_http.Object, _logger, "px-key", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            (await provider.TestConnectionAsync()).IsHealthy.Should().BeTrue();
        }

        [Fact]
        public async Task TestConnectionAsync_Error_ReturnsFalse()
        {
            var provider = new PerplexityProvider(_http.Object, _logger, "px-key", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));

            (await provider.TestConnectionAsync()).IsHealthy.Should().BeFalse();
        }

        [Fact]
        public async Task GetRecommendationsAsync_NonOk_ReturnsEmpty()
        {
            var provider = new PerplexityProvider(_http.Object, _logger, "px-key", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.Forbidden));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_NonJsonText_ReturnsEmpty()
        {
            var provider = new PerplexityProvider(_http.Object, _logger, "px-key", preferStructured: true);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "just text without array" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task UpdateModel_Then_TestConnection_Succeeds()
        {
            var provider = new PerplexityProvider(_http.Object, _logger, "px-key", "llama-3.1-sonar-large-128k-online", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            provider.UpdateModel("llama-3.1-sonar-small-128k-online");
            (await provider.TestConnectionAsync()).IsHealthy.Should().BeTrue();
        }

    }
}
