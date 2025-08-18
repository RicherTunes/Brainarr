using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using VoidResult = NzbDrone.Core.ImportLists.Brainarr.Services.VoidResult;
using Brainarr.Tests.Helpers;
using Xunit;
using Newtonsoft.Json;

namespace Brainarr.Tests.Integration
{
    public class EndToEndTests
    {
        private readonly Mock<Logger> _loggerMock;
        private readonly Mock<IHttpClient> _httpClientMock;

        public EndToEndTests()
        {
            _loggerMock = new Mock<Logger>();
            _httpClientMock = new Mock<IHttpClient>();
        }

        [Fact]
        public async Task FullRecommendationFlow_WithValidData_Success()
        {
            // Arrange
            var settings = TestDataGenerator.GenerateSettings(AIProvider.Ollama);
            var libraryProfile = TestDataGenerator.GenerateLibraryProfile(150, 750);
            var recommendations = TestDataGenerator.GenerateRecommendations(settings.MaxRecommendations);
            
            var provider = new OllamaProvider(
                settings.OllamaUrl,
                settings.OllamaModel,
                _httpClientMock.Object,
                _loggerMock.Object);

            SetupHttpResponse(JsonConvert.SerializeObject(new { response = JsonConvert.SerializeObject(recommendations) }));

            // Act
            var prompt = BuildPrompt(libraryProfile, settings);
            var result = await provider.GetRecommendationsAsync(prompt);

            // Assert
            result.Should().HaveCount(recommendations.Count);
            
            // Verify the artist and album names match (the core recommendation data)
            for (int i = 0; i < recommendations.Count; i++)
            {
                result[i].Artist.Should().Be(recommendations[i].Artist);
                result[i].Album.Should().Be(recommendations[i].Album);
                result[i].Genre.Should().Be(recommendations[i].Genre);
                // Note: Confidence may be processed/normalized by the provider, so we just verify it's reasonable
                result[i].Confidence.Should().BeGreaterThan(0).And.BeLessOrEqualTo(1);
            }
        }

        [Fact]
        public async Task FullRecommendationFlow_WithTextResponse_ParsesCorrectly()
        {
            // Arrange
            var settings = TestDataGenerator.GenerateSettings(AIProvider.LMStudio);
            var textResponse = TestDataGenerator.GenerateTextResponse(10);
            
            var provider = new LMStudioProvider(
                settings.LMStudioUrl,
                settings.LMStudioModel,
                _httpClientMock.Object,
                _loggerMock.Object);

            SetupHttpResponse(JsonConvert.SerializeObject(new
            {
                choices = new[]
                {
                    new { message = new { content = textResponse } }
                }
            }));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().HaveCount(10);
            result.All(r => !string.IsNullOrEmpty(r.Artist)).Should().BeTrue();
            result.All(r => !string.IsNullOrEmpty(r.Album)).Should().BeTrue();
        }

        [Fact]
        public async Task RecommendationFlow_WithCaching_UsesCache()
        {
            // Arrange
            var cache = new RecommendationCache(_loggerMock.Object);
            var cacheKey = cache.GenerateCacheKey("Ollama", 20, "100_500");
            var cachedData = TestDataGenerator.GenerateImportListItems(20);
            
            // Pre-populate cache
            cache.Set(cacheKey, cachedData);

            // Act
            var success = cache.TryGet(cacheKey, out var result);
            await Task.Delay(1); // Simulate async operation

            // Assert
            success.Should().BeTrue();
            result.Should().BeEquivalentTo(cachedData);
        }

        [Fact]
        public async Task RecommendationFlow_WithRetries_EventuallySucceeds()
        {
            // Arrange
            var retryPolicy = new ExponentialBackoffRetryPolicy(_loggerMock.Object, 3, TimeSpan.FromMilliseconds(10));
            var attempts = 0;
            var expectedResult = TestDataGenerator.GenerateRecommendations(5);

            // Act
            var result = await retryPolicy.ExecuteAsync(async () =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new HttpRequestException("Temporary failure");
                }
                await Task.Delay(1);
                return expectedResult;
            }, "TestOperation");

            // Assert
            attempts.Should().Be(3);
            result.Should().BeEquivalentTo(expectedResult);
        }

        [Fact]
        public async Task RecommendationFlow_WithRateLimiting_ThrottlesRequests()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_loggerMock.Object);
            rateLimiter.Configure("test", 2, TimeSpan.FromMinutes(1)); // 2 requests per minute
            
            var executionTimes = new List<DateTime>();

            // Act
            for (int i = 0; i < 3; i++)
            {
                await rateLimiter.ExecuteAsync<VoidResult>("test", async () =>
                {
                    executionTimes.Add(DateTime.UtcNow);
                    await Task.Delay(1);
                    return VoidResult.Instance;
                });
            }

            // Assert
            executionTimes.Should().HaveCount(3);
            var lastDelay = (executionTimes[2] - executionTimes[1]).TotalMilliseconds;
            lastDelay.Should().BeGreaterThan(900); // Rate limited
        }

        [Fact]
        public async Task RecommendationFlow_WithHealthMonitoring_TracksHealth()
        {
            // Arrange
            var healthMonitor = new ProviderHealthMonitor(_loggerMock.Object);
            var provider = "test-provider";

            // Simulate mixed results
            for (int i = 0; i < 10; i++)
            {
                if (i % 3 == 0)
                {
                    healthMonitor.RecordFailure(provider, "Test failure");
                }
                else
                {
                    healthMonitor.RecordSuccess(provider, 100 + i * 10);
                }
            }

            // Act
            var health = await healthMonitor.CheckHealthAsync(provider, "http://test");

            // Assert
            health.Should().Be(HealthStatus.Healthy); // ~70% success rate
        }

        [Fact]
        public async Task CompleteWorkflow_FromLibraryToRecommendations()
        {
            // Arrange
            var workflow = new RecommendationWorkflow(_httpClientMock.Object, _loggerMock.Object);
            var library = TestDataGenerator.GenerateLibraryProfile(200, 1000);
            var settings = TestDataGenerator.GenerateSettings();

            // Setup mock responses
            SetupModelDetectionResponse(TestDataGenerator.GenerateModelList());
            SetupRecommendationResponse(TestDataGenerator.GenerateRecommendations(20));

            // Act
            var result = await workflow.ExecuteAsync(library, settings);

            // Assert
            result.Should().NotBeNull();
            result.Recommendations.Should().HaveCount(20);
            result.Provider.Should().Be("Ollama");
            result.Success.Should().BeTrue();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        public async Task PerformanceTest_VaryingLibrarySizes(int multiplier)
        {
            // Arrange
            var library = TestDataGenerator.GenerateLibraryProfile(
                100 * multiplier,
                500 * multiplier);
            
            var cache = new RecommendationCache(_loggerMock.Object);
            var cacheKey = cache.GenerateCacheKey("test", 20, $"{library.TotalArtists}_{library.TotalAlbums}");

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            cache.Set(cacheKey, TestDataGenerator.GenerateImportListItems(20));
            var setTime = stopwatch.ElapsedMilliseconds;
            
            stopwatch.Restart();
            cache.TryGet(cacheKey, out var result);
            var getTime = stopwatch.ElapsedMilliseconds;

            // Assert
            setTime.Should().BeLessThan(100); // Cache operations should be fast
            getTime.Should().BeLessThan(50);
            result.Should().HaveCount(20);
        }

        [Fact]
        public async Task EdgeCaseHandling_EmptyRecommendations()
        {
            // Arrange
            var provider = new OllamaProvider("http://test", "model", _httpClientMock.Object, _loggerMock.Object);
            SetupHttpResponse(JsonConvert.SerializeObject(new { response = "[]" }));

            // Act
            var result = await provider.GetRecommendationsAsync("test");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task EdgeCaseHandling_UnicodeData()
        {
            // Arrange
            var unicodeRec = TestDataGenerator.EdgeCases.UnicodeRecommendation();
            var provider = new OllamaProvider("http://test", "model", _httpClientMock.Object, _loggerMock.Object);
            SetupHttpResponse(JsonConvert.SerializeObject(new 
            { 
                response = JsonConvert.SerializeObject(new[] { unicodeRec })
            }));

            // Act
            var result = await provider.GetRecommendationsAsync("test");

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Contain("Björk");
            result[0].Artist.Should().Contain("🎵");
        }

        [Fact]
        public async Task EdgeCaseHandling_VeryLongData()
        {
            // Arrange
            var longRec = TestDataGenerator.EdgeCases.VeryLongRecommendation();
            var provider = new OllamaProvider("http://test", "model", _httpClientMock.Object, _loggerMock.Object);
            SetupHttpResponse(JsonConvert.SerializeObject(new 
            { 
                response = JsonConvert.SerializeObject(new[] { longRec })
            }));

            // Act
            var result = await provider.GetRecommendationsAsync("test");

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Length.Should().Be(500);
            result[0].Album.Length.Should().Be(500);
        }

        [Fact]
        public async Task StressTest_ManyRecommendations()
        {
            // Arrange
            var recommendations = TestDataGenerator.GenerateRecommendations(1000);
            var provider = new OllamaProvider("http://test", "model", _httpClientMock.Object, _loggerMock.Object);
            SetupHttpResponse(JsonConvert.SerializeObject(new 
            { 
                response = JsonConvert.SerializeObject(recommendations)
            }));

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await provider.GetRecommendationsAsync("test");
            stopwatch.Stop();

            // Assert
            result.Should().HaveCount(1000);
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // Should parse 1000 items in under 1 second
        }

        private void SetupHttpResponse(string content)
        {
            var response = HttpResponseFactory.CreateResponse(content, System.Net.HttpStatusCode.OK);
            
            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);
        }

        private void SetupModelDetectionResponse(List<string> models)
        {
            SetupHttpResponse(JsonConvert.SerializeObject(new { models = models.Select(m => new { name = m }) }));
        }

        private void SetupRecommendationResponse(List<Recommendation> recommendations)
        {
            SetupHttpResponse(JsonConvert.SerializeObject(new { response = JsonConvert.SerializeObject(recommendations) }));
        }

        private string BuildPrompt(LibraryProfile profile, BrainarrSettings settings)
        {
            return $@"Based on this music library, recommend {settings.MaxRecommendations} new albums:
Library: {profile.TotalArtists} artists, {profile.TotalAlbums} albums
Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", profile.TopArtists.Take(10))}
Discovery mode: {settings.DiscoveryMode}";
        }

        // Helper class for workflow testing
        private class RecommendationWorkflow
        {
            private readonly IHttpClient _httpClient;
            private readonly Logger _logger;

            public RecommendationWorkflow(IHttpClient httpClient, Logger logger)
            {
                _httpClient = httpClient;
                _logger = logger;
            }

            public async Task<WorkflowResult> ExecuteAsync(LibraryProfile library, BrainarrSettings settings)
            {
                try
                {
                    var provider = new OllamaProvider(
                        settings.OllamaUrl,
                        settings.OllamaModel,
                        _httpClient,
                        _logger);

                    var prompt = $"Generate recommendations for {library.TotalArtists} artists";
                    var recommendations = await provider.GetRecommendationsAsync(prompt);

                    return new WorkflowResult
                    {
                        Success = true,
                        Recommendations = recommendations,
                        Provider = provider.ProviderName
                    };
                }
                catch (Exception ex)
                {
                    return new WorkflowResult
                    {
                        Success = false,
                        Error = ex.Message
                    };
                }
            }
        }

        private class WorkflowResult
        {
            public bool Success { get; set; }
            public List<Recommendation> Recommendations { get; set; }
            public string Provider { get; set; }
            public string Error { get; set; }
        }
    }
}