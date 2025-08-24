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
    [Trait("Provider", "DeepSeek")]
    public class DeepSeekProviderTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Mock<Logger> _logger;
        private readonly DeepSeekProvider _provider;
        private readonly BrainarrSettings _settings;

        public DeepSeekProviderTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = new Mock<Logger>();
            _settings = new BrainarrSettings
            {
                Provider = AIProvider.DeepSeek,
                DeepSeekApiKey = "sk-deepseek-test123",
                DeepSeekModel = "DeepSeek_Chat"
            };
            _provider = new DeepSeekProvider(_httpClient.Object, _logger.Object, _settings.DeepSeekApiKey, "deepseek-chat");
        }

        [Fact]
        public async Task GetRecommendations_HandlesSuccessfulResponse()
        {
            // Arrange
            var successResponse = @"{
                ""id"": ""ds-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""deepseek-chat"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""[{\""artist\"": \""Pink Floyd\"", \""album\"": \""The Dark Side of the Moon\"", \""genre\"": \""Progressive Rock\"", \""confidence\"": 0.95, \""reason\"": \""Classic progressive rock masterpiece with exceptional audio engineering\""}]""
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""usage"": {
                    ""prompt_tokens"": 250,
                    ""completion_tokens"": 75,
                    ""total_tokens"": 325,
                    ""prompt_cache_hit_tokens"": 0,
                    ""prompt_cache_miss_tokens"": 250
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
                    ""type"": ""invalid_request_error"",
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
        public async Task GetRecommendations_HandlesQuotaExceeded()
        {
            // Arrange
            var quotaResponse = @"{
                ""error"": {
                    ""message"": ""You have exceeded your quota. Please check your billing details."",
                    ""type"": ""insufficient_quota"",
                    ""param"": null,
                    ""code"": ""insufficient_quota""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(quotaResponse, HttpStatusCode.PaymentRequired);
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
                ""id"": ""ds-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""deepseek-chat"",
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
                ""id"": ""ds-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""deepseek-chat"",
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
        public async Task GetRecommendations_HandlesReasoningMode()
        {
            // Arrange - DeepSeek R1 reasoning mode response
            var reasoningResponse = @"{
                ""id"": ""ds-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""deepseek-reasoner"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""<thinking>\nLet me analyze the user's music preferences and find recommendations that match their taste. Based on their listening history, I can see they prefer progressive rock and complex compositions.\n</thinking>\n\n[{\""artist\"": \""Tool\"", \""album\"": \""Lateralus\"", \""genre\"": \""Progressive Metal\"", \""confidence\"": 0.92, \""reason\"": \""Complex time signatures and progressive elements similar to user's preferences\""}]""
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""reasoning_content"": ""<thinking>\nLet me analyze the user's music preferences...\n</thinking>""
            }";
            var response = HttpResponseFactory.CreateResponse(reasoningResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Tool");
            result[0].Album.Should().Be("Lateralus");
            // Should handle reasoning content gracefully
            _logger.Verify(x => x.Debug(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task GetRecommendations_HandlesCoderModelResponse()
        {
            // Arrange - DeepSeek Coder model might be used
            var coderResponse = @"{
                ""id"": ""ds-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""deepseek-coder"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""// Music recommendation algorithm result\n[{\""artist\"": \""Kraftwerk\"", \""album\"": \""Computer World\"", \""genre\"": \""Electronic\"", \""confidence\"": 0.88, \""reason\"": \""Perfect intersection of technology and music creativity\""}]""
                        },
                        ""finish_reason"": ""stop""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(coderResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Kraftwerk");
            result[0].Album.Should().Be("Computer World");
        }

        [Fact]
        public async Task TestConnection_SuccessfulConnection()
        {
            // Arrange
            var testResponse = @"{
                ""id"": ""ds-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""deepseek-chat"",
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
        public async Task GetRecommendations_HandlesContextLengthExceeded()
        {
            // Arrange
            var contextLengthResponse = @"{
                ""error"": {
                    ""message"": ""This model's maximum context length is 64000 tokens"",
                    ""type"": ""invalid_request_error"",
                    ""param"": ""messages"",
                    ""code"": ""context_length_exceeded""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(contextLengthResponse, HttpStatusCode.BadRequest);
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
        public async Task GetRecommendations_HandlesCacheHitTokens()
        {
            // Arrange - DeepSeek includes cache hit/miss tokens in usage
            var cacheResponse = @"{
                ""id"": ""ds-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""deepseek-chat"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""[{\""artist\"": \""Test Artist\"", \""album\"": \""Test Album\"", \""genre\"": \""Rock\"", \""confidence\"": 0.8, \""reason\"": \""Great music\""}]""
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""usage"": {
                    ""prompt_tokens"": 250,
                    ""completion_tokens"": 75,
                    ""total_tokens"": 325,
                    ""prompt_cache_hit_tokens"": 150,
                    ""prompt_cache_miss_tokens"": 100
                }
            }";
            var response = HttpResponseFactory.CreateResponse(cacheResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
            // Should handle cache tokens gracefully
            _logger.Verify(x => x.Debug(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task GetRecommendations_HandlesLengthFinishReason()
        {
            // Arrange - Response cut off due to max tokens
            var lengthFinishResponse = @"{
                ""id"": ""ds-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""deepseek-chat"",
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
            var response = HttpResponseFactory.CreateResponse(lengthFinishResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            // Should handle incomplete response gracefully
            _logger.Verify(x => x.Debug(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task GetRecommendations_HandlesFunctionCalling()
        {
            // Arrange - DeepSeek might support function calling
            var functionResponse = @"{
                ""id"": ""ds-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""deepseek-chat"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": null,
                            ""function_call"": {
                                ""name"": ""get_music_recommendations"",
                                ""arguments"": ""{\""recommendations\"": [{\""artist\"": \""Test Artist\"", \""album\"": \""Test Album\""}]}""
                            }
                        },
                        ""finish_reason"": ""function_call""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(functionResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            _logger.Verify(x => x.Debug(It.IsAny<string>()), Times.AtLeastOnce);
        }
    }
}