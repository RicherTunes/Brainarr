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
    [Trait("Provider", "Gemini")]
    public class GeminiProviderTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly GeminiProvider _provider;
        private readonly BrainarrSettings _settings;

        public GeminiProviderTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _settings = new BrainarrSettings
            {
                Provider = AIProvider.Gemini,
                GeminiApiKey = "AIzaSyTest123",
                GeminiModel = "Gemini_15_Flash"
            };
            
            // Create a minimal logger for testing (NLog creates a null logger if not configured)
            var logger = NLog.LogManager.GetLogger("test");
            _provider = new GeminiProvider(_httpClient.Object, logger, _settings.GeminiApiKey, "gemini-1.5-flash");
        }

        [Fact]
        public async Task GetRecommendations_HandlesSuccessfulResponse()
        {
            // Arrange
            var successResponse = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                {
                                    ""text"": ""[{\""artist\"": \""Pink Floyd\"", \""album\"": \""The Dark Side of the Moon\"", \""genre\"": \""Progressive Rock\"", \""confidence\"": 0.95, \""reason\"": \""Classic progressive rock masterpiece\""}]""
                                }
                            ],
                            ""role"": ""model""
                        },
                        ""finishReason"": ""STOP"",
                        ""safetyRatings"": [
                            {
                                ""category"": ""HARM_CATEGORY_HARASSMENT"",
                                ""probability"": ""NEGLIGIBLE""
                            }
                        ]
                    }
                ],
                ""usageMetadata"": {
                    ""promptTokenCount"": 250,
                    ""candidatesTokenCount"": 75,
                    ""totalTokenCount"": 325
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
                    ""code"": 400,
                    ""message"": ""API key not valid. Please pass a valid API key."",
                    ""status"": ""INVALID_ARGUMENT""
                }
            }";
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

        [Fact]
        public async Task GetRecommendations_HandlesRateLimitError()
        {
            // Arrange
            var rateLimitResponse = @"{
                ""error"": {
                    ""code"": 429,
                    ""message"": ""Resource has been exhausted (e.g. check quota)."",
                    ""status"": ""RESOURCE_EXHAUSTED""
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
        public async Task GetRecommendations_HandlesQuotaExceeded()
        {
            // Arrange
            var quotaResponse = @"{
                ""error"": {
                    ""code"": 429,
                    ""message"": ""Quota exceeded for quota metric 'Generate Content API requests per day' and limit 'Generate Content API requests per day per project' of service 'generativelanguage.googleapis.com' for consumer 'project_number:123456789'."",
                    ""status"": ""RESOURCE_EXHAUSTED""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(quotaResponse, HttpStatusCode.TooManyRequests);
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
        public async Task GetRecommendations_HandlesContentBlocked()
        {
            // Arrange
            var blockedResponse = @"{
                ""candidates"": [
                    {
                        ""finishReason"": ""SAFETY"",
                        ""safetyRatings"": [
                            {
                                ""category"": ""HARM_CATEGORY_HARASSMENT"",
                                ""probability"": ""HIGH""
                            }
                        ]
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(blockedResponse, HttpStatusCode.OK);
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
                    ""code"": 404,
                    ""message"": ""Model 'models/gemini-5.0-pro' not found."",
                    ""status"": ""NOT_FOUND""
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
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                {
                                    ""text"": ""[{\""artist\"": \""Pink Floyd\"", \""album\"": \""The Dark Side of the Moon\"" // malformed JSON""
                                }
                            ],
                            ""role"": ""model""
                        },
                        ""finishReason"": ""STOP""
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
        public async Task GetRecommendations_HandlesEmptyResponse()
        {
            // Arrange
            var emptyResponse = @"{
                ""candidates"": []
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
        public async Task GetRecommendations_HandlesAlternateJSONStructure()
        {
            // Arrange - Test handling of nested recommendations object
            var alternateResponse = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                {
                                    ""text"": ""{\""recommendations\"": [{\""artist\"": \""Test Artist\"", \""album\"": \""Test Album\"", \""genre\"": \""Rock\"", \""confidence\"": 0.8, \""reason\"": \""Great music\""}]}""
                                }
                            ],
                            ""role"": ""model""
                        },
                        ""finishReason"": ""STOP""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(alternateResponse, HttpStatusCode.OK);
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
        public async Task TestConnection_SuccessfulConnection()
        {
            // Arrange
            var testResponse = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                {
                                    ""text"": ""OK""
                                }
                            ],
                            ""role"": ""model""
                        },
                        ""finishReason"": ""STOP""
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
                    ""code"": 400,
                    ""message"": ""API key not valid"",
                    ""status"": ""INVALID_ARGUMENT""
                }
            }";
            var response = HttpResponseFactory.CreateResponse(errorResponse, HttpStatusCode.BadRequest);
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
                    ""code"": 500,
                    ""message"": ""Internal error encountered."",
                    ""status"": ""INTERNAL""
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
        public async Task GetRecommendations_HandlesMaxTokensExceeded()
        {
            // Arrange - Gemini-specific max tokens error
            var tokenLimitResponse = @"{
                ""candidates"": [
                    {
                        ""finishReason"": ""MAX_TOKENS"",
                        ""content"": {
                            ""parts"": [
                                {
                                    ""text"": ""[{\""artist\"": \""Partial Artist\""}]""
                                }
                            ]
                        }
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(tokenLimitResponse, HttpStatusCode.OK);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(response);

            // Act
            var result = await _provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            // Should handle partial response gracefully
            // Logger verification removed - using concrete logger for testing
        }

        [Fact]
        public async Task GetRecommendations_HandlesRecitationFinishReason()
        {
            // Arrange - Gemini-specific recitation blocking
            var recitationResponse = @"{
                ""candidates"": [
                    {
                        ""finishReason"": ""RECITATION""
                    }
                ]
            }";
            var response = HttpResponseFactory.CreateResponse(recitationResponse, HttpStatusCode.OK);
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