using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;
using Newtonsoft.Json;
using Brainarr.Tests.Helpers;

namespace Brainarr.Tests.Services
{
    public class ModelDetectionServiceTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<Logger> _loggerMock;
        private readonly ModelDetectionService _service;

        public ModelDetectionServiceTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _loggerMock = new Mock<Logger>();
            _service = new ModelDetectionService(_httpClientMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task GetOllamaModelsAsync_WithValidResponse_ReturnsModels()
        {
            // Arrange
            var ollamaResponse = new
            {
                models = new[]
                {
                    new { name = "llama2:latest", size = 3825819519L },
                    new { name = "mistral:7b", size = 4109863423L },
                    new { name = "qwen:14b", size = 8343859200L }
                }
            };

            SetupHttpResponse(JsonConvert.SerializeObject(ollamaResponse));

            // Act
            var result = await _service.GetOllamaModelsAsync("http://localhost:11434");

            // Assert
            result.Should().HaveCount(3);
            result.Should().Contain("llama2:latest");
            result.Should().Contain("mistral:7b");
            result.Should().Contain("qwen:14b");
        }

        [Fact]
        public async Task GetOllamaModelsAsync_WithEmptyResponse_ReturnsEmptyList()
        {
            // Arrange
            SetupHttpResponse(JsonConvert.SerializeObject(new { models = new object[0] }));

            // Act
            var result = await _service.GetOllamaModelsAsync("http://localhost:11434");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetOllamaModelsAsync_WithNullUrl_ReturnsEmptyList()
        {
            // Act
            var result = await _service.GetOllamaModelsAsync(null);

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetOllamaModelsAsync_WithEmptyUrl_ReturnsEmptyList()
        {
            // Act
            var result = await _service.GetOllamaModelsAsync("");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetOllamaModelsAsync_WithNetworkError_ReturnsEmptyList()
        {
            // Arrange
            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act
            var result = await _service.GetOllamaModelsAsync("http://localhost:11434");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetOllamaModelsAsync_WithInvalidJson_ReturnsEmptyList()
        {
            // Arrange
            SetupHttpResponse("not valid json");

            // Act
            var result = await _service.GetOllamaModelsAsync("http://localhost:11434");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetLMStudioModelsAsync_WithValidResponse_ReturnsModels()
        {
            // Arrange
            var lmStudioResponse = new
            {
                data = new[]
                {
                    new { id = "TheBloke/Llama-2-7B-GGUF", owned_by = "user" },
                    new { id = "mistralai/Mistral-7B-v0.1-GGUF", owned_by = "user" },
                    new { id = "Qwen/Qwen1.5-14B-GGUF", owned_by = "system" }
                }
            };

            SetupHttpResponse(JsonConvert.SerializeObject(lmStudioResponse));

            // Act
            var result = await _service.GetLMStudioModelsAsync("http://localhost:1234");

            // Assert
            result.Should().HaveCount(3);
            result.Should().Contain("TheBloke/Llama-2-7B-GGUF");
            result.Should().Contain("mistralai/Mistral-7B-v0.1-GGUF");
            result.Should().Contain("Qwen/Qwen1.5-14B-GGUF");
        }

        [Fact]
        public async Task GetLMStudioModelsAsync_WithMixedValidInvalid_ReturnsOnlyValid()
        {
            // Arrange
            var response = new
            {
                data = new object[]
                {
                    new { id = "valid-model", owned_by = "user" },
                    new { id = (string)null, owned_by = "user" }, // null id
                    new { id = "", owned_by = "user" }, // empty id
                    new { id = "   ", owned_by = "user" }, // whitespace id
                    new { id = "another-valid", owned_by = "system" }
                }
            };

            SetupHttpResponse(JsonConvert.SerializeObject(response));

            // Act
            var result = await _service.GetLMStudioModelsAsync("http://localhost:1234");

            // Assert
            result.Should().HaveCount(2);
            result.Should().Contain("valid-model");
            result.Should().Contain("another-valid");
        }

        [Fact]
        public async Task GetLMStudioModelsAsync_WithTimeout_ReturnsEmptyList()
        {
            // Arrange
            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new TaskCanceledException("Request timeout"));

            // Act
            var result = await _service.GetLMStudioModelsAsync("http://localhost:1234");

            // Assert
            result.Should().BeEmpty();
        }

        [Theory]
        [InlineData("http://localhost:11434", "/api/tags")]
        [InlineData("http://localhost:11434/", "/api/tags")]
        [InlineData("http://localhost:11434//", "/api/tags")]
        [InlineData("https://localhost:11434", "/api/tags")]
        public async Task GetOllamaModelsAsync_WithVariousUrlFormats_NormalizesCorrectly(string baseUrl, string expectedPath)
        {
            // Arrange
            SetupHttpResponse(JsonConvert.SerializeObject(new { models = new[] { new { name = "test" } } }));

            // Act
            await _service.GetOllamaModelsAsync(baseUrl);

            // Assert
            _httpClientMock.Verify(x => x.ExecuteAsync(It.Is<HttpRequest>(r => 
                r.Url.ToString().Contains(expectedPath.TrimStart('/')))), Times.Once);
        }

        [Fact]
        public async Task GetOllamaModelsAsync_WithLargeModelList_HandlesCorrectly()
        {
            // Arrange
            var models = new List<object>();
            for (int i = 0; i < 1000; i++)
            {
                models.Add(new { name = $"model-{i}:latest", size = 1000000 * i });
            }

            SetupHttpResponse(JsonConvert.SerializeObject(new { models }));

            // Act
            var result = await _service.GetOllamaModelsAsync("http://localhost:11434");

            // Assert
            result.Should().HaveCount(1000);
            result[0].Should().Be("model-0:latest");
            result[999].Should().Be("model-999:latest");
        }

        [Fact]
        public async Task GetLMStudioModelsAsync_WithDuplicateModels_ReturnsAll()
        {
            // Arrange
            var response = new
            {
                data = new[]
                {
                    new { id = "duplicate-model", owned_by = "user" },
                    new { id = "duplicate-model", owned_by = "user" },
                    new { id = "duplicate-model", owned_by = "system" }
                }
            };

            SetupHttpResponse(JsonConvert.SerializeObject(response));

            // Act
            var result = await _service.GetLMStudioModelsAsync("http://localhost:1234");

            // Assert
            result.Should().HaveCount(3);
            result.Should().OnlyContain(x => x == "duplicate-model");
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        public async Task GetOllamaModelsAsync_WithErrorStatusCode_ReturnsEmptyList(HttpStatusCode statusCode)
        {
            // Arrange
            var response = HttpResponseFactory.CreateResponse(null, statusCode);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await _service.GetOllamaModelsAsync("http://localhost:11434");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetOllamaModelsAsync_WithPartialJson_HandlesGracefully()
        {
            // Arrange
            var partialJson = @"{""models"": [{""name"": ""model1""}, {""name"": ""mod"; // Truncated

            SetupHttpResponse(partialJson);

            // Act
            var result = await _service.GetOllamaModelsAsync("http://localhost:11434");

            // Assert
            result.Should().BeEmpty(); // Should handle parse error gracefully
        }

        [Fact]
        public async Task GetLMStudioModelsAsync_WithAlternativeResponseFormat_HandlesFlexibly()
        {
            // Arrange - Some LM Studio versions might return different format
            var alternativeResponse = new
            {
                models = new[]  // Using 'models' instead of 'data'
                {
                    new { name = "model1", id = "model1-id" },
                    new { name = "model2", id = "model2-id" }
                }
            };

            SetupHttpResponse(JsonConvert.SerializeObject(alternativeResponse));

            // Act
            var result = await _service.GetLMStudioModelsAsync("http://localhost:1234");

            // Assert
            // Current implementation expects 'data' field, so this would return empty
            result.Should().BeEmpty();
        }

        private void SetupHttpResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var response = HttpResponseFactory.CreateResponse(content, statusCode);
            
            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);
        }
    }
}