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
    [Trait("Provider", "Perplexity")]
    public class PerplexityProviderTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Mock<Logger> _logger;
        private readonly PerplexityProvider _provider;
        private readonly BrainarrSettings _settings;

        public PerplexityProviderTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = new Mock<Logger>();
            _settings = new BrainarrSettings
            {
                Provider = AIProvider.Perplexity,
                PerplexityApiKey = "pplx-test123",
                PerplexityModel = "Sonar_Large"
            };
            _provider = new PerplexityProvider(_httpClient.Object, _logger.Object, _settings.PerplexityApiKey, "llama-3.1-sonar-large-128k-online");
        }

        [Fact]
        public async Task GetRecommendations_HandlesSuccessfulResponse()
        {
            // Arrange
            var successResponse = @"{
                ""id"": ""cmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.1-sonar-large-128k-online"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""Based on the latest music trends and reviews, here are my recommendations:\n\n[{\""artist\"": \""Pink Floyd\"", \""album\"": \""The Dark Side of the Moon\"", \""genre\"": \""Progressive Rock\"", \""confidence\"": 0.95, \""reason\"": \""Classic progressive rock masterpiece with excellent recent remastered versions\""}]""
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""usage"": {
                    ""prompt_tokens"": 250,
                    ""completion_tokens"": 150,
                    ""total_tokens"": 400
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
                    ""message"": ""Rate limit reached for requests"",
                    ""type"": ""requests"",
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
                    ""message"": ""You exceeded your current quota, please check your plan and billing details"",
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
        public async Task GetRecommendations_HandlesOnlineSearchResults()
        {
            // Arrange - Perplexity includes online search results
            var searchResponse = @"{
                ""id"": ""cmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.1-sonar-large-128k-online"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""Based on current music charts and recent critical reviews from Pitchfork and Rolling Stone:\n\n[{\""artist\"": \""Test Artist\"", \""album\"": \""Test Album 2024\"", \""genre\"": \""Indie Rock\"", \""confidence\"": 0.9, \""reason\"": \""Critically acclaimed new release with 8.5/10 from Pitchfork\""}]""
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""citations"": [
                    ""https://pitchfork.com/reviews/albums/"",
                    ""https://www.rollingstone.com/music/""
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(searchResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
            result[0].Album.Should().Be("Test Album 2024");
            result[0].Reason.Should().Contain("Pitchfork");
        }

        [Fact]
        public async Task GetRecommendations_HandlesMalformedJSON()
        {
            // Arrange
            var successResponse = @"{
                ""id"": ""cmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.1-sonar-large-128k-online"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""Here are some recommendations:\n\n[{\""artist\"": \""Pink Floyd\"", \""album\"": \""The Dark Side of the Moon\"" // malformed JSON""
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
                ""id"": ""cmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.1-sonar-large-128k-online"",
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
        public async Task GetRecommendations_HandlesContentWithoutJSON()
        {
            // Arrange - Perplexity might return search results without structured JSON
            var textOnlyResponse = @"{
                ""id"": ""cmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.1-sonar-large-128k-online"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""Based on current music trends, I recommend checking out Pink Floyd's latest remaster of The Dark Side of the Moon. It's receiving great reviews from music critics.""
                        },
                        ""finish_reason"": ""stop""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(textOnlyResponse, HttpStatusCode.OK);
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
        public async Task TestConnection_SuccessfulConnection()
        {
            // Arrange
            var testResponse = @"{
                ""id"": ""cmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.1-sonar-large-128k-online"",
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
        public async Task GetRecommendations_HandlesSearchTimeout()
        {
            // Arrange - Perplexity specific search timeout
            var searchTimeoutResponse = @"{
                ""error"": {
                    ""message"": ""Search request timed out"",
                    ""type"": ""timeout_error"",
                    ""param"": null,
                    ""code"": ""search_timeout""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(searchTimeoutResponse, HttpStatusCode.RequestTimeout);
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
        public async Task GetRecommendations_HandlesCitationsInResponse()
        {
            // Arrange - Perplexity includes citations with online model
            var citationsResponse = @"{
                ""id"": ""cmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.1-sonar-large-128k-online"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""Based on recent music industry reports [1], here are trending recommendations:\n\n[{\""artist\"": \""Test Artist\"", \""album\"": \""Test Album\"", \""genre\"": \""Rock\"", \""confidence\"": 0.85, \""reason\"": \""Featured in Billboard's top new releases [2]\""}]""
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""citations"": [
                    ""https://www.billboard.com/charts/"",
                    ""https://www.musicindustryweekly.com/reports/""
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(citationsResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                      .Returns(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
            result[0].Reason.Should().Contain("Billboard");
            // Should handle citations gracefully
            _logger.Verify(x => x.Debug(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task GetRecommendations_HandlesLengthFinishReason()
        {
            // Arrange - Response cut off due to max tokens
            var lengthFinishResponse = @"{
                ""id"": ""cmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""llama-3.1-sonar-large-128k-online"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""Based on current trends, I recommend:\n\n[{\""artist\"": \""Partial Artist\"", \""album\"": \""Partial""
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
    }
}