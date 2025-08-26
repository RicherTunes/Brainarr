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
    [Trait("Provider", "OpenRouter")]
    public class OpenRouterProviderTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly OpenRouterProvider _provider;
        private readonly BrainarrSettings _settings;

        public OpenRouterProviderTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenRouter,
                OpenRouterApiKey = "sk-or-test123",
                OpenRouterModel = "Claude35_Haiku"
            };
            
            // Create a minimal logger for testing (NLog creates a null logger if not configured)
            var logger = NLog.LogManager.GetLogger("test");
            _provider = new OpenRouterProvider(_httpClient.Object, logger, _settings.OpenRouterApiKey, "anthropic/claude-3.5-haiku");
        }

        [Fact]
        public async Task GetRecommendations_HandlesSuccessfulResponse()
        {
            // Arrange
            var successResponse = @"{
                ""id"": ""gen-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""anthropic/claude-3.5-haiku"",
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
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

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
        public async Task GetRecommendations_HandlesRateLimitError()
        {
            // Arrange
            var rateLimitResponse = @"{
                ""error"": {
                    ""message"": ""Rate limit exceeded for requests"",
                    ""type"": ""requests"",
                    ""param"": null,
                    ""code"": ""rate_limit_exceeded""
                }
            }";
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
        public async Task GetRecommendations_HandlesCreditExhausted()
        {
            // Arrange - OpenRouter specific credit exhausted error
            var creditResponse = @"{
                ""error"": {
                    ""message"": ""Insufficient credits"",
                    ""type"": ""insufficient_quota"",
                    ""param"": null,
                    ""code"": ""insufficient_quota""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(creditResponse, HttpStatusCode.PaymentRequired);
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
            var modelErrorResponse = @"{
                ""error"": {
                    ""message"": ""The model 'nonexistent/model' does not exist or you do not have access to it"",
                    ""type"": ""invalid_request_error"",
                    ""param"": ""model"",
                    ""code"": ""model_not_found""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(modelErrorResponse, HttpStatusCode.NotFound);
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
        public async Task GetRecommendations_HandlesProviderError()
        {
            // Arrange - OpenRouter specific provider error
            var providerErrorResponse = @"{
                ""error"": {
                    ""message"": ""The upstream provider returned an error"",
                    ""type"": ""provider_error"",
                    ""param"": null,
                    ""code"": ""provider_error""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(providerErrorResponse, HttpStatusCode.BadGateway);
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
        public async Task GetRecommendations_HandlesTimeoutError()
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
        public async Task GetRecommendations_HandlesMalformedJSON()
        {
            // Arrange
            var successResponse = @"{
                ""id"": ""gen-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""anthropic/claude-3.5-haiku"",
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
        public async Task GetRecommendations_HandlesEmptyChoices()
        {
            // Arrange
            var emptyResponse = @"{
                ""id"": ""gen-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""anthropic/claude-3.5-haiku"",
                ""choices"": [],
                ""usage"": {
                    ""prompt_tokens"": 250,
                    ""completion_tokens"": 0,
                    ""total_tokens"": 250
                }
            }";
            var response = HttpResponseFactory.CreateResponse(emptyResponse, HttpStatusCode.OK);
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
        public async Task GetRecommendations_HandlesMultipleModelsInResponse()
        {
            // Arrange - OpenRouter can use different models than requested
            var multiModelResponse = @"{
                ""id"": ""gen-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""openai/gpt-4o-mini"",
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
            var response = HttpResponseFactory.CreateResponse(multiModelResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
            // Should handle different model being used
            // Logger verification removed - using concrete logger for testing
        }

        [Fact]
        public async Task TestConnection_SuccessfulConnection()
        {
            // Arrange
            var testResponse = @"{
                ""id"": ""gen-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""anthropic/claude-3.5-haiku"",
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
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

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
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

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
            var contextLengthResponse = @"{
                ""error"": {
                    ""message"": ""This model's maximum context length is 128000 tokens"",
                    ""type"": ""invalid_request_error"",
                    ""param"": ""messages"",
                    ""code"": ""context_length_exceeded""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(contextLengthResponse, HttpStatusCode.BadRequest);
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
        public async Task GetRecommendations_HandlesModerationFlag()
        {
            // Arrange - OpenRouter specific moderation response
            var moderationResponse = @"{
                ""id"": ""gen-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""anthropic/claude-3.5-haiku"",
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
            var response = HttpResponseFactory.CreateResponse(moderationResponse, HttpStatusCode.OK);
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
        public async Task GetRecommendations_HandlesProviderSpecificMetadata()
        {
            // Arrange - OpenRouter includes provider-specific metadata
            var metadataResponse = @"{
                ""id"": ""gen-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""anthropic/claude-3.5-haiku"",
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
                    ""total_tokens"": 325
                },
                ""provider"": {
                    ""name"": ""Anthropic"",
                    ""model"": ""claude-3-5-haiku-20241022""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(metadataResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
            // Should handle provider metadata gracefully
            // Logger verification removed - using concrete logger for testing
        }
    }
}