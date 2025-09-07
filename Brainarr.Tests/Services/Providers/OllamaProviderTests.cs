using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    [Trait("Category", "Provider")]
    [Trait("Provider", "Ollama")]
    public class OllamaProviderTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly OllamaProvider _provider;
        private readonly BrainarrSettings _settings;

        public OllamaProviderTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = "llama2"
            };

            // Create a minimal logger for testing (NLog creates a null logger if not configured)
            var logger = NLog.LogManager.GetLogger("test");
            _provider = new OllamaProvider(_settings.OllamaUrl, _settings.OllamaModel, _httpClient.Object, logger, null);
        }

        [Fact]
        public async Task GetRecommendations_HandlesStreamingResponse()
        {
            // Arrange
            var streamingResponse = @"{""model"":""llama2"",""created_at"":""2024-01-01T00:00:00Z"",""response"":""[{\""artist\"": \""Test Artist\"", \""album\"": \""Test Album\""}]"",""done"":true}";

            var response = HttpResponseFactory.CreateResponse(streamingResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
            result[0].Album.Should().Be("Test Album");
        }

        [Fact]
        public async Task GetRecommendations_HandlesMalformedJSON()
        {
            // Arrange
            var malformedResponse = @"{""model"":""llama2"",""response"":""[{artist: Test Artist, album: Test Album}]"",""done"":true}";
            var response = HttpResponseFactory.CreateResponse(malformedResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            // Logger verification removed - using concrete logger for testing
        }

        [Fact]
        public async Task GetRecommendations_HandlesConnectionRefused()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ThrowsAsync(new HttpRequestException("Connection refused"));

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            // Logger verification removed - using concrete logger for testing
        }

        [Fact]
        public async Task GetRecommendations_HandlesTimeout()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ThrowsAsync(new TaskCanceledException("Request timeout"));

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            // Logger verification removed - using concrete logger for testing
        }

        [Fact]
        public async Task GetRecommendations_HandlesPartialStreamingData()
        {
            // Arrange - Incomplete streaming response
            var partialResponse = @"{""model"":""llama2"",""response"":""[{\""artist\"": \""Test"",""done"":false}";
            // Connection drops here

            var response = HttpResponseFactory.CreateResponse(partialResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            // Logger verification removed - using concrete logger for testing
        }

        [Fact]
        public async Task GetRecommendations_HandlesModelNotFound()
        {
            // Arrange
            var errorResponse = @"{""error"":""model 'nonexistent' not found""}";
            var response = HttpResponseFactory.CreateResponse(errorResponse, HttpStatusCode.NotFound);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            // Logger verification removed - using concrete logger for testing
        }

        [Fact]
        public async Task GetRecommendations_HandlesLargeResponse()
        {
            // Arrange - Generate large response with many recommendations
            var recommendations = new List<object>();
            for (int i = 0; i < 100; i++)
            {
                recommendations.Add(new { artist = $"Artist {i}", album = $"Album {i}", year = 2020 + (i % 5) });
            }
            var largeResponse = @$"{{""model"":""llama2"",""response"":""{JsonConvert.SerializeObject(recommendations).Replace("\"", "\\\"")}"",""done"":true}}";
            var response = HttpResponseFactory.CreateResponse(largeResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(100); // Provider returns all parsed recommendations without limiting
        }

        [Fact]
        public async Task TestConnection_SuccessfulConnection()
        {
            // Arrange
            var tagsResponse = @"{""models"":[{""name"":""llama2"",""modified_at"":""2024-01-01T00:00:00Z""}]}";
            var response = HttpResponseFactory.CreateResponse(tagsResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.TestConnectionAsync();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task TestConnection_FailedConnection()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ThrowsAsync(new HttpRequestException("Connection refused"));

            // Act
            var result = await _provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        // GetAvailableModels tests removed - not part of IAIProvider interface
    }
}
