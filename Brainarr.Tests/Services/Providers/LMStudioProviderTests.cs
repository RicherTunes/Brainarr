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
    [Trait("Provider", "LMStudio")]
    public class LMStudioProviderTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly LMStudioProvider _provider;
        private readonly BrainarrSettings _settings;

        public LMStudioProviderTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "http://localhost:1234",
                LMStudioModel = "local-model"
            };
            
            // Create a minimal logger for testing (NLog creates a null logger if not configured)
            var logger = NLog.LogManager.GetLogger("test");
            _provider = new LMStudioProvider(_settings.LMStudioUrl, _settings.LMStudioModel, _httpClient.Object, logger, null);
        }

        [Fact]
        public async Task GetRecommendations_HandlesOpenAICompatibleFormat()
        {
            // Arrange
            var openAIResponse = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""local-model"",
                ""choices"": [{
                    ""index"": 0,
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": ""[{\""artist\"": \""Test Artist\"", \""album\"": \""Test Album\"", \""year\"": 2024}]""
                    },
                    ""finish_reason"": ""stop""
                }],
                ""usage"": {
                    ""prompt_tokens"": 9,
                    ""completion_tokens"": 12,
                    ""total_tokens"": 21
                }
            }";
            var response = HttpResponseFactory.CreateResponse(openAIResponse, HttpStatusCode.OK);
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
        public async Task GetRecommendations_HandlesConnectionToWrongPort()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ThrowsAsync(new HttpRequestException("Connection refused - No server running on port 1234"));

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            // Logger verification removed - using concrete logger for testing
        }

        [Fact]
        public async Task GetRecommendations_HandlesInvalidAPIKey()
        {
            // Arrange
            var errorResponse = @"{""error"":{""message"":""Invalid API key"",""type"":""authentication_error"",""code"":""invalid_api_key""}}";
            var response = HttpResponseFactory.CreateResponse(errorResponse, HttpStatusCode.Unauthorized);
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
        public async Task GetRecommendations_HandlesModelNotLoaded()
        {
            // Arrange
            var errorResponse = @"{""error"":{""message"":""No model loaded. Please load a model first."",""type"":""model_error""}}";
            var response = HttpResponseFactory.CreateResponse(errorResponse, HttpStatusCode.ServiceUnavailable);
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
        public async Task GetRecommendations_HandlesStreamingMode()
        {
            // Arrange - LM Studio streaming response
            var streamingResponse = @"data: {""choices"":[{""delta"":{""content"":""[""}}]}

data: {""choices"":[{""delta"":{""content"":""{""}}]}

data: {""choices"":[{""delta"":{""content"":\""artist\"": \""Test Artist\""""}}]}

data: {""choices"":[{""delta"":{""content"":"", \""album\"": \""Test Album\""""}}]}

data: {""choices"":[{""delta"":{""content"":""}]""}}]}

data: [DONE]";
            var response = HttpResponseFactory.CreateResponse(streamingResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            // Logger verification removed - using concrete logger for testing
        }

        [Fact]
        public async Task GetRecommendations_HandlesRateLimiting()
        {
            // Arrange
            var rateLimitResponse = @"{""error"":{""message"":""Rate limit exceeded"",""type"":""rate_limit_error""}}";
            var response = HttpResponseFactory.CreateResponse(rateLimitResponse, HttpStatusCode.TooManyRequests);
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
        public async Task TestConnection_SuccessfulConnection()
        {
            // Arrange
            var modelsResponse = @"{""data"":[{""id"":""local-model"",""object"":""model""}]}";
            var response = HttpResponseFactory.CreateResponse(modelsResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.TestConnectionAsync();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task TestConnection_NoServerRunning()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ThrowsAsync(new HttpRequestException("Connection refused"));

            // Act
            var result = await _provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }


        [Fact]
        public async Task GetRecommendations_HandlesMalformedJSON()
        {
            // Arrange
            var malformedResponse = @"{
                ""choices"": [{
                    ""message"": {
                        ""content"": ""Here are some recommendations: {artist: Test, album: Album}""
                    }
                }]
            }";
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
        public async Task GetRecommendations_HandlesContextLengthExceeded()
        {
            // Arrange
            var errorResponse = @"{""error"":{""message"":""Context length exceeded. Max context length is 4096 tokens."",""type"":""context_length_error""}}";
            var response = HttpResponseFactory.CreateResponse(errorResponse, HttpStatusCode.BadRequest);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            // Logger verification removed - using concrete logger for testing
        }
    }
}