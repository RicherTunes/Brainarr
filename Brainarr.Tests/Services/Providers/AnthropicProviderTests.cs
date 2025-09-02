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
    public class AnthropicProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public AnthropicProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithContentArray_ParsesItems()
        {
            var provider = new AnthropicProvider(_http.Object, _logger, "ak-test", "claude-3-5-haiku-latest");

            var arr = "[ { \"artist\": \"Artist B\", \"album\": \"Album B\", \"genre\": \"Indie\", \"confidence\": 0.8, \"reason\": \"Match\" } ]";
            var responseObj = new { id = "m1", content = new[] { new { type = "text", text = arr } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");

            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Artist B");
        }

        [Fact]
        public async Task TestConnectionAsync_OnError_ReturnsFalse()
        {
            var provider = new AnthropicProvider(_http.Object, _logger, "ak-test");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));

            (await provider.TestConnectionAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task GetRecommendationsAsync_EmptyContent_ReturnsEmpty()
        {
            var provider = new AnthropicProvider(_http.Object, _logger, "ak");
            var responseObj = new { id = "m2", content = new[] { new { type = "text", text = "" } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_MultipleBlocks_FirstEmpty_ReturnsEmpty()
        {
            var provider = new AnthropicProvider(_http.Object, _logger, "ak");
            var arr = "[ { \"artist\": \"AX\", \"album\": \"AY\" } ]";
            var responseObj = new { id = "m3", content = new object[] { new { type = "text", text = "" }, new { type = "text", text = arr } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task UpdateModel_Then_TestConnection_Succeeds()
        {
            var provider = new AnthropicProvider(_http.Object, _logger, "ak-test", "claude-3-5-haiku-latest");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            provider.UpdateModel("claude-3-5-sonnet-latest");
            (await provider.TestConnectionAsync()).Should().BeTrue();
        }
        [Fact]
        public async Task GetRecommendationsAsync_NonOk_ReturnsEmpty()
        {
            var provider = new AnthropicProvider(_http.Object, _logger, "ak");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TestConnectionAsync_OnOk_ReturnsTrue()
        {
            var provider = new AnthropicProvider(_http.Object, _logger, "ak", "claude-3-5-haiku-latest");
            var okObj = new { id = "m1", content = new object[] { new { type = "text", text = "OK" } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(okObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.OK));
            (await provider.TestConnectionAsync()).Should().BeTrue();
        }

        [Fact]
        public async Task GetRecommendationsAsync_InvalidJson_ReturnsEmpty()
        {
            var provider = new AnthropicProvider(_http.Object, _logger, "ak");
            var responseObj = new { id = "m4", content = new object[] { new { type = "text", text = "not-json" } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TestConnectionAsync_BadStatus_ReturnsFalse()
        {
            var provider = new AnthropicProvider(_http.Object, _logger, "ak");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));
            (await provider.TestConnectionAsync()).Should().BeFalse();
        }
    }
}
