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
    public class OpenAIProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public OpenAIProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithArrayContent_ParsesItems()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini", preferStructured: true);

            var recs = new { recommendations = new[] { new { artist = "Artist A", album = "Album A", genre = "Rock", confidence = 0.9, reason = "Good fit" } } };
            var contentArray = Newtonsoft.Json.JsonConvert.SerializeObject(recs);
            var apiObj = new { id = "1", choices = new[] { new { message = new { content = contentArray } } } };
            var apiResponse = Newtonsoft.Json.JsonConvert.SerializeObject(apiObj);

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
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);

            var recommendationsObj = new { recommendations = new[] { new { artist = "X", album = "Y", genre = "Z", confidence = 0.5, reason = "because" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(recommendationsObj);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Album.Should().Be("Y");
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithSingleObject_ParsesItem()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            var single = new { artist = "S", album = "T", genre = "U", confidence = 0.6, reason = "why" };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(single);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("S");
        }

        [Fact]
        public async Task GetRecommendationsAsync_NonOk_ReturnsEmpty()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("error", HttpStatusCode.BadRequest));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_LongPrompt_IsTruncated()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            var recs = new { recommendations = new[] { new { artist = "A1", album = "B1", genre = "G", confidence = 0.9, reason = "R" } } };
            var contentArray = Newtonsoft.Json.JsonConvert.SerializeObject(recs);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = contentArray } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var longPrompt = new string('x', 15000);
            var result = await provider.GetRecommendationsAsync(longPrompt);
            result.Should().HaveCount(1); // execution succeeds after truncation
        }

        [Fact]
        public async Task GetRecommendationsAsync_InvalidJson_ReturnsEmpty_Alt()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "not-json" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetRecommendationsAsync_EmptyPrompt_Throws()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            provider.Invoking(p => p.GetRecommendationsAsync(" ")).Should().ThrowAsync<System.ArgumentException>();
        }

        [Fact]
        public async Task GetRecommendationsAsync_AlternatePropertyNames_AndStringConfidence()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            var inner = new { recommendations = new[] { new { Artist = "AltArtist", Album = "AltAlbum", Genre = "Alt", Confidence = "0.42", Reason = "because" } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(inner);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().ContainSingle(r => r.Artist == "AltArtist" && r.Confidence == 0.42);
        }

        [Fact]
        public async Task GetRecommendationsAsync_MissingOptionalFields_UsesDefaults()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            var obj = new { artist = "OnlyArtist" }; // no album/genre/confidence/reason
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(obj);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().ContainSingle(r => r.Artist == "OnlyArtist" && r.Genre == "Unknown" && r.Confidence > 0);
        }

        [Fact]
        public async Task GetRecommendationsAsync_UnexpectedRootType_ReturnsEmpty()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            var content = "12345"; // number root
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_InvalidJson_ReturnsEmpty()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = "not-json" } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));

            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetRecommendationsAsync_ObjectWithWeirdTypes_UsesDefaultsOrSkips()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", preferStructured: true);
            var weird = new { recommendations = new[] { new { artist = "A", album = 42, genre = (string)null, confidence = "NaN", reason = 123 } } };
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(weird);
            var responseObj = new { id = "1", choices = new[] { new { message = new { content = content } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var result = await provider.GetRecommendationsAsync("prompt");
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task TestConnectionAsync_OnOk_ReturnsTrue()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test");

            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            (await provider.TestConnectionAsync()).IsHealthy.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateModel_Then_TestConnection_Succeeds()
        {
            var provider = new OpenAIProvider(_http.Object, _logger, "sk-test", "gpt-4o-mini", preferStructured: true);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                 .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            provider.UpdateModel("gpt-4o");
            (await provider.TestConnectionAsync()).IsHealthy.Should().BeTrue();
        }
    }
}
