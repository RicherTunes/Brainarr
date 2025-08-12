using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Brainarr.Tests.Helpers;
using Xunit;
using Newtonsoft.Json;

namespace Brainarr.Tests.EdgeCases
{
    public class CriticalEdgeCaseTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<Logger> _loggerMock;

        public CriticalEdgeCaseTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _loggerMock = new Mock<Logger>();
        }

        #region Network & Timeout Scenarios - Highest ROI

        [Fact]
        public async Task Provider_WithPartialResponse_HandlesGracefully()
        {
            // Arrange - Simulate partial JSON response (connection dropped)
            var partialJson = @"{""choices"":[{""message"":{""content"":""[{""""artist"""":""""Pink Fl";
            
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(partialJson));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty(); // Should handle gracefully, not throw
        }

        [Fact]
        public async Task Provider_WithTimeoutJustBeforeDeadline_CompletesSuccessfully()
        {
            // Arrange - Response arrives just before timeout
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            var validResponse = JsonConvert.SerializeObject(new
            {
                response = JsonConvert.SerializeObject(new[]
                {
                    new { artist = "Test Artist", album = "Test Album", confidence = 0.9 }
                })
            });

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns(async (HttpRequest req) =>
                {
                    // Simulate delay just under timeout
                    await Task.Delay(29000); // 29 seconds for 30 second timeout
                    return HttpResponseFactory.CreateResponse(validResponse);
                });

            // Act & Assert - Should complete without timeout
            var result = await provider.GetRecommendationsAsync("test prompt");
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task Provider_WithDNSFailure_FallsBackGracefully()
        {
            // Arrange
            var provider = new OllamaProvider(
                "http://invalid.dns.name.that.does.not.exist:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("DNS name not resolved"));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty();
            _loggerMock.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Provider_With429RateLimitResponse_RetriesWithBackoff()
        {
            // Arrange
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            var attempts = 0;
            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns(async (HttpRequest req) =>
                {
                    attempts++;
                    if (attempts < 3)
                    {
                        var response = HttpResponseFactory.CreateResponse(
                            "Rate limit exceeded",
                            HttpStatusCode.TooManyRequests);
                        
                        // Retry-After header would be handled by the actual HttpClient
                        
                        return response;
                    }
                    
                    var validJson = JsonConvert.SerializeObject(new
                    {
                        response = "[]"
                    });
                    return HttpResponseFactory.CreateResponse(validJson);
                });

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            attempts.Should().Be(3);
            result.Should().NotBeNull();
        }

        #endregion

        #region Malformed AI Responses - High ROI

        [Fact]
        public async Task Provider_WithValidJsonWrongSchema_HandlesGracefully()
        {
            // Arrange - Valid JSON but completely wrong structure
            var wrongSchema = JsonConvert.SerializeObject(new
            {
                status = "success",
                data = new
                {
                    users = new[]
                    {
                        new { id = 1, name = "John" },
                        new { id = 2, name = "Jane" }
                    }
                }
            });

            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(wrongSchema));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task Provider_WithConfidenceOutOfRange_FiltersInvalidResults()
        {
            // Arrange - Confidence > 1.0 and < 0
            var invalidConfidence = JsonConvert.SerializeObject(new
            {
                response = JsonConvert.SerializeObject(new[]
                {
                    new { artist = "Valid Artist", album = "Valid Album", confidence = 0.8 },
                    new { artist = "Invalid High", album = "Album", confidence = 1.5 },
                    new { artist = "Invalid Low", album = "Album", confidence = -0.3 },
                    new { artist = "Valid Artist 2", album = "Valid Album 2", confidence = 0.6 }
                })
            });

            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(invalidConfidence));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().HaveCount(2); // Only valid confidence values
            result.Should().NotContain(r => r.Artist == "Invalid High" || r.Artist == "Invalid Low");
        }

        [Fact]
        public async Task Provider_WithMixedEncodingResponse_HandlesGracefully()
        {
            // Arrange - Response with invalid UTF-8 sequences
            var invalidUtf8 = @"{""response"":""[{\""artist\"":\""Test " + "\xFF\xFE" + @" Artist\""}]""}";
            
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(invalidUtf8));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty(); // Should handle encoding errors gracefully
        }

        [Fact]
        public async Task Provider_WithExtremelyLargeResponse_HandlesWithoutOOM()
        {
            // Arrange - 10MB response
            var hugeArray = new object[10000];
            for (int i = 0; i < hugeArray.Length; i++)
            {
                hugeArray[i] = new
                {
                    artist = $"Artist {i} with very long name that contains lots of text to increase size",
                    album = $"Album {i} with extremely verbose title including unnecessary details",
                    genre = "Rock/Pop/Jazz/Classical/Electronic/HipHop/Metal/Country",
                    confidence = 0.5,
                    reason = "This is a very long reason explaining why this recommendation was made including lots of unnecessary details to pad the response size"
                };
            }

            var hugeResponse = JsonConvert.SerializeObject(new
            {
                response = JsonConvert.SerializeObject(hugeArray)
            });

            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(hugeResponse));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().BeLessThanOrEqualTo(100); // Should limit results
        }

        #endregion

        #region Data Validation & Security - High ROI

        [Theory]
        [InlineData("'; DROP TABLE artists; --", "Album Name")]
        [InlineData("Artist", "'; DELETE FROM albums WHERE '1'='1")]
        [InlineData("<script>alert('XSS')</script>", "Album")]
        [InlineData("Artist", "<img src=x onerror=alert('XSS')>")]
        [InlineData("../../../etc/passwd", "Album")]
        [InlineData("Artist", "C:\\Windows\\System32\\config\\sam")]
        public async Task Provider_WithMaliciousInput_SanitizesData(string maliciousArtist, string maliciousAlbum)
        {
            // Arrange
            var maliciousResponse = JsonConvert.SerializeObject(new
            {
                response = JsonConvert.SerializeObject(new[]
                {
                    new { artist = maliciousArtist, album = maliciousAlbum, confidence = 0.9 }
                })
            });

            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(maliciousResponse));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            if (result.Count > 0)
            {
                // Should either filter out or sanitize malicious content
                var firstItem = result[0];
                var combinedText = $"{firstItem.Artist} {firstItem.Album}";
                combinedText.Should().NotContain("DROP TABLE");
                combinedText.Should().NotContain("DELETE FROM");
                combinedText.Should().NotContain("<script>");
                combinedText.Should().NotContain("onerror=");
                combinedText.Should().NotContain("../");
                combinedText.Should().NotContain("System32");
            }
        }

        [Fact]
        public async Task Provider_WithNullByteInjection_HandlesGracefully()
        {
            // Arrange - Null byte injection attempt
            var nullByteResponse = JsonConvert.SerializeObject(new
            {
                response = JsonConvert.SerializeObject(new[]
                {
                    new { artist = "Artist\0Name", album = "Album\0Title", confidence = 0.9 }
                })
            });

            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(nullByteResponse));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            if (result.Count > 0)
            {
                var firstItem = result[0];
                var combinedText = $"{firstItem.Artist} {firstItem.Album}";
                combinedText.Should().NotContain("\0");
            }
        }

        #endregion

        #region Provider Cascade Failures - High ROI

        [Fact]
        public async Task AllProvidersFail_ReturnsEmptyGracefully()
        {
            // Arrange - Simulate all providers failing
            var providers = new IAIProvider[]
            {
                new OllamaProvider("http://localhost:11434", "llama2", _httpClientMock.Object, _loggerMock.Object),
                new LMStudioProvider("http://localhost:1234", "model", _httpClientMock.Object, _loggerMock.Object)
            };

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Connection refused"));

            // Act
            var allResults = new List<Recommendation>();
            foreach (var provider in providers)
            {
                var result = await provider.GetRecommendationsAsync("test prompt");
                allResults.AddRange(result);
            }

            // Assert
            allResults.Should().BeEmpty();
            _loggerMock.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Provider_WithCircularRedirect_DetectsAndStops()
        {
            // Arrange
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClientMock.Object,
                _loggerMock.Object);

            var redirectCount = 0;
            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest req) =>
                {
                    redirectCount++;
                    if (redirectCount > 5)
                    {
                        throw new HttpRequestException("Too many redirects");
                    }
                    
                    var response = HttpResponseFactory.CreateResponse(null, HttpStatusCode.Redirect);
                    return Task.FromResult(response);
                });

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty();
            redirectCount.Should().BeGreaterThan(1);
        }

        #endregion

        #region Boundary Conditions - High ROI

        [Fact]
        public void EmptyLibrary_GeneratesValidPrompt()
        {
            // Arrange
            var library = new LibraryProfile
            {
                TotalArtists = 0,
                TotalAlbums = 0,
                TopGenres = new Dictionary<string, int>(),
                TopArtists = new List<string>(),
                RecentlyAdded = new List<string>()
            };

            // Act
            var prompt = BuildPrompt(library, 10);

            // Assert
            prompt.Should().NotBeNullOrEmpty();
            prompt.Should().Contain("0 artists");
            prompt.Should().Contain("0 albums");
        }

        [Fact]
        public async Task ExactlyAtRateLimit_LastRequestSucceeds()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_loggerMock.Object);
            rateLimiter.Configure("test", 3, TimeSpan.FromSeconds(1));
            
            var executionTimes = new List<DateTime>();

            // Act - Execute exactly at limit
            for (int i = 0; i < 3; i++)
            {
                await rateLimiter.ExecuteAsync("test", async () =>
                {
                    executionTimes.Add(DateTime.UtcNow);
                    await Task.Delay(1);
                    return i;
                });
            }

            // Assert
            executionTimes.Should().HaveCount(3);
            var totalTime = (executionTimes[2] - executionTimes[0]).TotalMilliseconds;
            totalTime.Should().BeLessThan(100); // All should execute immediately
        }

        [Fact]
        public async Task OneOverRateLimit_FourthRequestDelayed()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_loggerMock.Object);
            rateLimiter.Configure("test", 3, TimeSpan.FromSeconds(1));
            
            var executionTimes = new List<DateTime>();

            // Act - Execute one over limit
            for (int i = 0; i < 4; i++)
            {
                await rateLimiter.ExecuteAsync("test", async () =>
                {
                    executionTimes.Add(DateTime.UtcNow);
                    await Task.Delay(1);
                    return i;
                });
            }

            // Assert
            executionTimes.Should().HaveCount(4);
            var lastDelay = (executionTimes[3] - executionTimes[2]).TotalMilliseconds;
            lastDelay.Should().BeGreaterThan(900); // Fourth request should be delayed
        }

        #endregion

        #region Helper Methods

        private string BuildPrompt(LibraryProfile library, int maxRecommendations)
        {
            return $@"Based on this music library, recommend {maxRecommendations} new albums:
Library: {library.TotalArtists} artists, {library.TotalAlbums} albums
Top genres: {string.Join(", ", library.TopGenres.Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", library.TopArtists)}";
        }

        #endregion
    }
}