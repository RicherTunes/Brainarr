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
    public class OpenRouterProviderTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        [Fact]
        public async Task GetRecommendationsAsync_ParsesContentArray()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", "openai/gpt-4o-mini", preferStructured: true);

            var arr = "[ { \"artist\": \"A\", \"album\": \"B\", \"genre\": \"G\", \"confidence\": 0.7, \"reason\": \"R\" } ]";
            var responseObj = new { id = "1", model = "openai/gpt-4o-mini", choices = new[] { new { message = new { content = arr } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("A");
        }

        [Fact]
        public async Task GetRecommendationsAsync_ExtractsArrayFromText()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", preferStructured: true);
            var arr = "[ { \"artist\": \"Z\", \"album\": \"Q\", \"genre\": \"R\", \"confidence\": 0.66, \"reason\": \"ok\" } ]";
            var content = "Here are results:\n" + arr + "\nEnd.";
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("Q");
        }

        [Fact]
        public async Task GetRecommendationsAsync_EmptyContent_ReturnsEmpty()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", preferStructured: true);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_InvalidJson_ReturnsEmpty()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", preferStructured: true);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "not-json" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TestConnectionAsync_ErrorStatus_ReturnsFalse()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("oops", HttpStatusCode.TooManyRequests));

            (await provider.TestConnectionAsync()).IsHealthy.Should().BeFalse();
        }

        [Fact]
        public async Task GetRecommendationsAsync_NonOk_WithErrorObject_ReturnsEmpty()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", preferStructured: true);
            var errorObj = new { error = new { message = "quota", code = "quota_exceeded" } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(errorObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.PaymentRequired));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_NonOk_WithAlternateErrorShape_ReturnsEmpty()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", preferStructured: true);
            var errorObj = new { error = new { code = 403, message = "forbidden" } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(errorObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response, HttpStatusCode.Forbidden));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task UpdateModel_Then_TestConnection_Succeeds()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", "openai/gpt-4o-mini", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            provider.UpdateModel("anthropic/claude-3.5-haiku");
            (await provider.TestConnectionAsync()).Should().BeTrue();
        }

        [Fact]
        public async Task TestConnectionAsync_BadStatus_ReturnsFalse()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.Forbidden));
            (await provider.TestConnectionAsync()).IsHealthy.Should().BeFalse();
        }
        [Fact]
        public async Task GetRecommendationsAsync_ArrayDirect_ReturnsItems()
        {
            var provider = new OpenRouterProvider(_http.Object, _logger, "or-key", preferStructured: true);
            var arr = "[ { \"artist\": \"AR\", \"album\": \"AL\" } ]";
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = arr } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
        }
    }
}
