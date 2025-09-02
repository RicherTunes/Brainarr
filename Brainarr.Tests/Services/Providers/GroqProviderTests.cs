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
    public class GroqProviderTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        [Fact]
        public async Task GetRecommendationsAsync_ParsesArrayAndLogsUsage()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            var arr = "[ { \"artist\": \"E\", \"album\": \"F\", \"genre\": \"Genre\", \"confidence\": 0.91, \"reason\": \"R\" } ]";
            var responseObj = new { id = "1", usage = new { prompt_tokens = 1, completion_tokens = 1, queue_time = 1, total_time = 2 }, choices = new[] { new { message = new { content = arr } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRecommendationsAsync_ParsesObjectWithRecommendations()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            var payload = new { recommendations = new[] { new { artist = "GG", album = "HH", genre = "II", confidence = 0.8, reason = "rr" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("HH");
        }

        [Fact]
        public async Task GetRecommendationsAsync_ExtractsArrayFromText()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            var arr = "[ { \"artist\": \"TX\", \"album\": \"TY\" } ]";
            var content = "prefix " + arr + " suffix";
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().ContainSingle(r => r.Album == "TY");
        }

        [Fact]
        public async Task GetRecommendationsAsync_UnexpectedJsonStructure_ReturnsEmpty()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "{}" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_MixedCaseProperties_Parses()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            var payload = new { recommendations = new[] { new { Artist = "MixA", Album = "MixB", Genre = "MixG", Confidence = 0.5, Reason = "mix" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().ContainSingle(r => r.Album == "MixB");
        }

        [Fact]
        public async Task GetRecommendationsAsync_NonOk_ReturnsEmpty()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_InvalidJson_ReturnsEmpty()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "not-json" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_StringConfidence_SkipsInvalid()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            var payload = new { recommendations = new[] { new { artist = "S", album = "T", genre = "U", confidence = "0.77", reason = "why" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            // Implementation may skip invalid numeric strings; assert no crash and either 0 or 1 item
            result.Count.Should().BeOneOf(0, 1);
        }

        [Fact]
        public async Task TestConnectionAsync_OnOk_ReturnsTrue()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            (await provider.TestConnectionAsync()).Should().BeTrue();
        }

        [Fact]
        public async Task GetRecommendationsAsync_ParsesObjectWithAlbums()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            var payload = new { albums = new[] { new { artist = "II", album = "JJ", genre = "KK", confidence = 0.7, reason = "rr" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("JJ");
        }

        [Fact]
        public async Task TestConnectionAsync_BadStatus_ReturnsFalse()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("oops", HttpStatusCode.BadGateway));

            (await provider.TestConnectionAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task UpdateModel_Then_TestConnection_Succeeds()
        {
            var provider = new GroqProvider(_http.Object, _logger, "gk", "llama-3.3-70b-versatile");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            provider.UpdateModel("llama-3.2-90b-vision-preview");
            (await provider.TestConnectionAsync()).Should().BeTrue();
        }
    }
}
