using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
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
    [Trait("Provider", "Groq")]
    public class GroqProviderTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Mock<Logger> _logger;
        private readonly GroqProvider _provider;
        private readonly BrainarrSettings _settings;

        public GroqProviderTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = new Mock<Logger>();
            _settings = new BrainarrSettings
            {
                Provider = AIProvider.Groq,
                GroqApiKey = "gsk_test123",
                GroqModel = "Llama33_70B"
            };
            _provider = new GroqProvider(_httpClient.Object, _logger.Object, _settings.GroqApiKey, "llama-3.3-70b-versatile");
        }

        [Fact]
        public async Task GetRecommendations_HandlesSuccessfulResponse()
        {
            // Arrange
            var successResponse = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.3-70b-versatile"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""[{\""artist\"": \""Pink Floyd\"", \""album\"": \""The Dark Side of the Moon\"", \""genre\"": \""Progressive Rock\"", \""confidence\"": 0.95, \""reason\"": \""Classic progressive rock masterpiece\""}]""
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""usage"": {
                    ""prompt_tokens"": 250,
                    ""completion_tokens"": 75,
                    ""total_tokens"": 325
                }
            }";
            var response = HttpResponseFactory.CreateResponse(successResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Pink Floyd");
            result[0].Album.Should().Be("The Dark Side of the Moon");
            result[0].Genre.Should().Be("Progressive Rock");
            result[0].Confidence.Should().Be(0.95);
        }

        [Fact]
        public async Task GetRecommendations_HandlesInvalidAPIKey()
        {
            // Arrange
            var errorResponse = @"{
                ""error"": {
                    ""message"": ""Invalid API key provided"",
                    ""type"": ""authentication_error"",
                    ""param"": null,
                    ""code"": ""invalid_api_key""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(errorResponse, HttpStatusCode.Unauthorized);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            _logger.Verify(x => x.Error(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_HandlesRateLimitError()
        {
            // Arrange
            var rateLimitResponse = @"{
                ""error"": {
                    ""message"": ""Rate limit exceeded"",
                    ""type"": ""rate_limit_error"",
                    ""param"": null,
                    ""code"": ""rate_limit_exceeded""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(rateLimitResponse, HttpStatusCode.TooManyRequests);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            _logger.Verify(x => x.Warn(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_HandlesTokenLimitExceeded()
        {
            // Arrange
            var tokenLimitResponse = @"{
                ""error"": {
                    ""message"": ""This model's maximum context length is 131072 tokens"",
                    ""type"": ""invalid_request_error"",
                    ""param"": ""messages"",
                    ""code"": ""context_length_exceeded""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(tokenLimitResponse, HttpStatusCode.BadRequest);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            _logger.Verify(x => x.Error(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_HandlesModelNotFound()
        {
            // Arrange
            var modelErrorResponse = @"{
                ""error"": {
                    ""message"": ""The model 'nonexistent-model' does not exist"",
                    ""type"": ""invalid_request_error"",
                    ""param"": ""model"",
                    ""code"": ""model_not_found""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(modelErrorResponse, HttpStatusCode.NotFound);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            _logger.Verify(x => x.Error(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_HandlesTimeoutError()
        {
            // Arrange
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Throws(new TaskCanceledException("Request timeout"));

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            _logger.Verify(x => x.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_HandlesMalformedJSON()
        {
            // Arrange
            var successResponse = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.3-70b-versatile"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""[{\""artist\"": \""Pink Floyd\"", \""album\"": \""The Dark Side of the Moon\"" // malformed JSON""
                        },
                        ""finish_reason"": ""stop""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(successResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            _logger.Verify(x => x.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_HandlesEmptyChoices()
        {
            // Arrange
            var emptyResponse = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.3-70b-versatile"",
                ""choices"": [],
                ""usage"": {
                    ""prompt_tokens"": 250,
                    ""completion_tokens"": 0,
                    ""total_tokens"": 250
                }
            }";
            var response = HttpResponseFactory.CreateResponse(emptyResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            _logger.Verify(x => x.Warn(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_HandlesContentFilter()
        {
            // Arrange
            var contentFilterResponse = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.3-70b-versatile"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": null
                        },
                        ""finish_reason"": ""content_filter""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(contentFilterResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            _logger.Verify(x => x.Warn(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_HandlesJSONObjectFormat()
        {
            // Arrange - Test Groq's JSON object response format
            var jsonObjectResponse = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.3-70b-versatile"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""{\""recommendations\"": [{\""artist\"": \""Test Artist\"", \""album\"": \""Test Album\"", \""genre\"": \""Rock\"", \""confidence\"": 0.8, \""reason\"": \""Great music\""}]}""
                        },
                        ""finish_reason"": ""stop""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(jsonObjectResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
            result[0].Album.Should().Be("Test Album");
        }

        [Fact]
        public async Task TestConnection_SuccessfulConnection()
        {
            // Arrange
            var testResponse = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.3-70b-versatile"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""OK""
                        },
                        ""finish_reason"": ""stop""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(testResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.TestConnectionAsync();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task TestConnection_InvalidAPIKey()
        {
            // Arrange
            var errorResponse = @"{
                ""error"": {
                    ""message"": ""Invalid API key"",
                    ""type"": ""authentication_error""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(errorResponse, HttpStatusCode.Unauthorized);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }


        [Fact]
        public async Task GetRecommendations_HandlesServerError()
        {
            // Arrange
            var serverErrorResponse = @"{
                ""error"": {
                    ""message"": ""Internal server error"",
                    ""type"": ""server_error"",
                    ""param"": null,
                    ""code"": null
                }
            }";
            var response = HttpResponseFactory.CreateResponse(serverErrorResponse, HttpStatusCode.InternalServerError);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
            _logger.Verify(x => x.Error(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_TracksFastResponseTime()
        {
            // Arrange
            var successResponse = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.3-70b-versatile"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""[{\""artist\"": \""Test Artist\"", \""album\"": \""Test Album\"", \""genre\"": \""Rock\"", \""confidence\"": 0.8, \""reason\"": \""Great music\""}]""
                        },
                        ""finish_reason"": ""stop""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(successResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            // Should log response time for Groq's ultra-fast inference
            _logger.Verify(x => x.Debug(It.Is<string>(s => s.Contains("response time"))), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_HandlesMaxTokensFinish()
        {
            // Arrange - Response cut off due to max tokens
            var maxTokensResponse = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.3-70b-versatile"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""[{\""artist\"": \""Partial Artist\"", \""album\"": \""Partial""
                        },
                        ""finish_reason"": ""length""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(maxTokensResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            // Should handle incomplete response gracefully
            _logger.Verify(x => x.Debug(It.IsAny<string>()), Times.AtLeastOnce);
        }
    }
}