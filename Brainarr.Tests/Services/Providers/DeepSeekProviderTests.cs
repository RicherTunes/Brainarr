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
    public class DeepSeekProviderTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        [Fact]
        public async Task GetRecommendationsAsync_ParsesAfterThinkingBlock()
        {
            var provider = new DeepSeekProvider(_http.Object, _logger, "dsk", preferStructured: true);
            var arr = "[ { \"artist\": \"I\", \"album\": \"J\", \"genre\": \"Hip-Hop\", \"confidence\": 0.8, \"reason\": \"R\" } ]";
            var content = "<thinking>reasoning...</thinking>\n" + arr;
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("J");
        }

        [Fact]
        public async Task GetRecommendationsAsync_DirectArray_Parses()
        {
            var provider = new DeepSeekProvider(_http.Object, _logger, "dsk", preferStructured: true);
            var arr = "[ { \"artist\": \"DX\", \"album\": \"DY\" } ]";
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = arr } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().ContainSingle(r => r.Album == "DY");
        }

        [Fact]
        public async Task GetRecommendationsAsync_InvalidJson_ReturnsEmpty()
        {
            var provider = new DeepSeekProvider(_http.Object, _logger, "dsk", preferStructured: true);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "not-json" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithUsage_LogsAndParses()
        {
            var provider = new DeepSeekProvider(_http.Object, _logger, "dsk", preferStructured: true);
            var arr = "[ { \"artist\": \"U1\", \"album\": \"U2\" } ]";
            var responseObj = new { id = "1", usage = new { prompt_tokens = 1, completion_tokens = 2, total_tokens = 3 }, choices = new[] { new { message = new { content = arr } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRecommendationsAsync_RemovesLeadingComments()
        {
            var provider = new DeepSeekProvider(_http.Object, _logger, "dsk", preferStructured: true);
            var arr = "[ { \"artist\": \"PP\", \"album\": \"QQ\", \"genre\": \"RR\", \"confidence\": 0.7, \"reason\": \"rr\" } ]";
            var content = "// explanation\n// more\n" + arr;
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("QQ");
        }

        [Fact]
        public async Task GetRecommendationsAsync_ObjectWithRecommendations_Parses()
        {
            var provider = new DeepSeekProvider(_http.Object, _logger, "dsk", preferStructured: true);
            var payload = new { recommendations = new[] { new { artist = "VV", album = "WW", genre = "XX", confidence = 0.9, reason = "rr" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("WW");
        }

        [Fact]
        public async Task TestConnectionAsync_Error_ReturnsFalse()
        {
            var provider = new DeepSeekProvider(_http.Object, _logger, "dsk", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));

            (await provider.TestConnectionAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task UpdateModel_Then_TestConnection_Succeeds()
        {
            var provider = new DeepSeekProvider(_http.Object, _logger, "dsk", "deepseek-chat", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            provider.UpdateModel("deepseek-reasoner");
            (await provider.TestConnectionAsync()).Should().BeTrue();
            provider.GetLastUserMessage().Should().BeNull("hints must be null after successful connection");
            provider.GetLearnMoreUrl().Should().BeNull();
        }
        [Fact]
        public async Task GetRecommendationsAsync_NonOk_ReturnsEmpty()
        {
            var provider = new DeepSeekProvider(_http.Object, _logger, "dsk", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }
    }
}
