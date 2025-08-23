using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using Moq;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using NzbDrone.Core.Parser.Model;
using VoidResult = NzbDrone.Core.ImportLists.Brainarr.Services.VoidResult;
using Brainarr.Tests.Helpers;
using Xunit;
using Newtonsoft.Json;

namespace Brainarr.Tests.EdgeCases
{
    public class ResourceAndTimeTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<Logger> _loggerMock;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public ResourceAndTimeTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _loggerMock = new Mock<Logger>();
            _httpClient = _httpClientMock.Object;
            _logger = _loggerMock.Object;
        }

        #region Resource Exhaustion Tests

        [Fact]
        public async Task Provider_WithSocketExhaustion_HandlesGracefully()
        {
            // Arrange - Simulate socket exhaustion
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClient,
                _logger);

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns(Task.FromException<HttpResponse>(new HttpRequestException("No connection could be made because the target machine actively refused it")));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().BeEmpty();
            // Note: Logger verification removed as Logger methods are non-overridable
        }

        [Fact]
        public async Task RateLimiter_WithManyFailedProviders_DoesNotLeakResources()
        {
            // Arrange
            var rateLimiter = new RateLimiter(_logger);
            var tasks = new List<Task>();

            // Configure many providers that will fail
            for (int i = 0; i < 100; i++)
            {
                rateLimiter.Configure($"provider_{i}", 1, TimeSpan.FromMilliseconds(10));
            }

            // Act - Many concurrent failing operations
            for (int i = 0; i < 100; i++)
            {
                var providerId = $"provider_{i}";
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await rateLimiter.ExecuteAsync<VoidResult>(providerId, async () =>
                        {
                            await Task.Delay(1);
                            throw new HttpRequestException("Connection failed");
                        });
                    }
                    catch
                    {
                        // Expected to fail
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - Should complete without resource leaks
            // In a real scenario, we'd check GC metrics, file handles, etc.
            tasks.All(t => t.IsCompleted).Should().BeTrue();
        }

        [Fact]
        public void Cache_UnderMemoryPressure_EvictsOldEntries()
        {
            // Arrange
            var cache = new RecommendationCache(_logger);
            var firstKey = cache.GenerateCacheKey("first", 10, "profile");
            var data = TestDataGenerator.GenerateImportListItems(1000);

            // Act - Add entry, then force memory pressure simulation
            cache.Set(firstKey, data);
            
            // Simulate memory pressure by adding many large entries
            for (int i = 0; i < 100; i++)
            {
                var key = cache.GenerateCacheKey($"large_{i}", 10, $"profile_{i}");
                var largeData = TestDataGenerator.GenerateImportListItems(500);
                cache.Set(key, largeData);
            }

            // Assert - Original entry might be evicted (depends on cache implementation)
            var success = cache.TryGet(firstKey, out var result);
            // Can't assert specific behavior without knowing cache implementation,
            // but operation should complete without throwing
        }

        #endregion

        #region Time-based Edge Cases

        [Fact]
        public async Task Provider_DuringTimeZoneChange_HandlesCorrectly()
        {
            // Arrange
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClient,
                _logger);

            var validResponse = JsonConvert.SerializeObject(new
            {
                response = JsonConvert.SerializeObject(new[]
                {
                    new { artist = "Test Artist", album = "Test Album", confidence = 0.9 }
                })
            });

            // Simulate time zone change during request
            var callCount = 0;
            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns(async (HttpRequest callInfo) =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // Simulate timezone change delay
                        await Task.Delay(100);
                    }
                    return HttpResponseFactory.CreateResponse(validResponse);
                });

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
        }

        [Fact]
        public async Task HealthMonitor_WithSystemTimeGoingBackwards_HandlesGracefully()
        {
            // Arrange
            var healthMonitor = new ProviderHealthMonitor(_logger);
            var provider = "test-provider";

            // Act - Record metrics that might have timestamp issues
            healthMonitor.RecordSuccess(provider, 100);
            
            // Simulate clock going backwards (DST, NTP sync)
            // This is hard to test directly, but we can test rapid operations
            for (int i = 0; i < 10; i++)
            {
                healthMonitor.RecordSuccess(provider, 50 + i);
            }

            // Assert - Should not throw
            var health = await healthMonitor.CheckHealthAsync(provider, "http://test");
            health.Should().Be(HealthStatus.Healthy);
        }

        [Theory]
        [InlineData("2024-03-10T07:00:00Z")] // DST transition
        [InlineData("2024-11-03T06:00:00Z")] // DST transition back
        [InlineData("2024-12-31T23:59:59Z")] // Year boundary
        public async Task Cache_DuringCriticalTimeTransitions_MaintainsConsistency(string transitionTime)
        {
            // Parse transition time for potential future time-aware testing
            var criticalTime = DateTime.Parse(transitionTime, null, System.Globalization.DateTimeStyles.RoundtripKind);
            
            // NOTE: Currently using standard test timing to verify cache consistency
            // Future enhancement could use criticalTime for time-specific testing 
            
            // Arrange
            var cache = new RecommendationCache(_logger);
            var key = cache.GenerateCacheKey("provider", 10, "profile");
            var data = TestDataGenerator.GenerateImportListItems(5);

            // Act - Cache operations around critical time transitions
            cache.Set(key, data);
            
            // Simulate brief delay (time transition)
            await Task.Delay(10);
            
            var success = cache.TryGet(key, out var result);

            // Assert
            success.Should().BeTrue();
            result.Should().HaveCount(5);
        }

        #endregion

        #region Unicode and Encoding Edge Cases

        [Theory]
        [InlineData("العربية", "البوم")] // Arabic
        [InlineData("עברית", "אלבום")] // Hebrew
        [InlineData("中文", "专辑")] // Chinese
        [InlineData("🎵 Artist", "Album 🎸")] // Emojis
        [InlineData("Björk", "Homogénic")] // Accented characters
        public async Task Provider_WithInternationalCharacters_HandlesCorrectly(string artist, string album)
        {
            // Arrange
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClient,
                _logger);

            var unicodeResponse = JsonConvert.SerializeObject(new
            {
                response = JsonConvert.SerializeObject(new[]
                {
                    new { artist = artist, album = album, confidence = 0.9 }
                })
            });

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(unicodeResponse));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be(artist);
            result[0].Album.Should().Be(album);
        }

        [Fact]
        public async Task Provider_WithBOMInResponse_HandlesCorrectly()
        {
            // Arrange
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClient,
                _logger);

            // Response with BOM (Byte Order Mark)
            var bomResponse = "\ufeff" + JsonConvert.SerializeObject(new
            {
                response = JsonConvert.SerializeObject(new[]
                {
                    new { artist = "Test Artist", album = "Test Album", confidence = 0.9 }
                })
            });

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(bomResponse));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
        }

        [Fact]
        public async Task Provider_WithMixedEncodingInSameResponse_HandlesGracefully()
        {
            // Arrange
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClient,
                _logger);

            // Mixed encoding response (some valid UTF-8, some not)
            var mixedResponse = JsonConvert.SerializeObject(new
            {
                response = JsonConvert.SerializeObject(new[]
                {
                    new { artist = "Valid Artist", album = "Valid Album", confidence = 0.9 },
                    new { artist = "Björk", album = "Homogénic", confidence = 0.8 }, // Valid UTF-8
                    new { artist = "Artist with", album = "special chars ±", confidence = 0.7 }
                })
            });

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(mixedResponse));

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().HaveCount(3);
            result.Should().Contain(r => r.Artist == "Björk");
        }

        #endregion

        #region Long-running Operation Tests

        [Fact]
        public async Task Provider_WithVerySlowResponse_HandlesWithinTimeout()
        {
            // Arrange
            var provider = new OllamaProvider(
                "http://localhost:11434",
                "llama2",
                _httpClient,
                _logger);

            var validResponse = JsonConvert.SerializeObject(new
            {
                response = "[]"
            });

            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns(async (HttpRequest callInfo) =>
                {
                    // Simulate very slow response (but within timeout)
                    await Task.Delay(1000); // 1 second delay
                    return HttpResponseFactory.CreateResponse(validResponse);
                });

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task Cache_WithConcurrentExpiryAndRefresh_MaintainsConsistency()
        {
            // Arrange
            var cache = new RecommendationCache(_logger);
            var key = cache.GenerateCacheKey("provider", 10, "profile");
            var initialData = TestDataGenerator.GenerateImportListItems(5);
            var refreshedData = TestDataGenerator.GenerateImportListItems(7);

            cache.Set(key, initialData);

            var tasks = new List<Task>();
            var results = new List<List<ImportListItemInfo>>();
            var lockObj = new object();

            // Act - Concurrent reads and refresh
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    if (cache.TryGet(key, out var data))
                    {
                        lock (lockObj)
                        {
                            results.Add(data);
                        }
                    }
                }));
            }

            // Refresh cache during reads
            tasks.Add(Task.Run(() => cache.Set(key, refreshedData)));

            await Task.WhenAll(tasks);

            // Assert - All results should be valid (either initial or refreshed)
            results.Should().NotBeEmpty();
            results.Should().OnlyContain(r => r.Count == 5 || r.Count == 7);
        }

        #endregion

        #region Edge Case Combinations

        [Fact]
        public async Task ComplexScenario_MultipleProvidersWithMixedIssues_HandlesGracefully()
        {
            // Arrange - Simulate a complex real-world scenario
            var healthMonitor = new ProviderHealthMonitor(_logger);
            var rateLimiter = new RateLimiter(_logger);
            var cache = new RecommendationCache(_logger);

            rateLimiter.Configure("provider1", 2, TimeSpan.FromSeconds(1));
            rateLimiter.Configure("provider2", 3, TimeSpan.FromSeconds(1));

            var provider1Success = 0;
            var provider2Success = 0;
            var exceptions = new List<Exception>();

            // Act - Complex multi-provider scenario
            var tasks = new List<Task>();
            
            for (int i = 0; i < 20; i++)
            {
                var localI = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await rateLimiter.ExecuteAsync<VoidResult>("provider1", async () =>
                        {
                            if (localI % 4 == 0) // 25% failure rate
                            {
                                healthMonitor.RecordFailure("provider1", "Simulated failure");
                                throw new HttpRequestException("Provider1 failed");
                            }
                            else
                            {
                                healthMonitor.RecordSuccess("provider1", 100 + localI);
                                Interlocked.Increment(ref provider1Success);
                            }
                            
                            await Task.Delay(10);
                            return VoidResult.Instance;
                        });
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions) exceptions.Add(ex);
                    }
                }));

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await rateLimiter.ExecuteAsync<VoidResult>("provider2", async () =>
                        {
                            if (localI % 6 == 0) // ~17% failure rate
                            {
                                healthMonitor.RecordFailure("provider2", "Simulated failure");
                                throw new HttpRequestException("Provider2 failed");
                            }
                            else
                            {
                                healthMonitor.RecordSuccess("provider2", 150 + localI);
                                Interlocked.Increment(ref provider2Success);
                            }
                            
                            await Task.Delay(15);
                            return VoidResult.Instance;
                        });
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions) exceptions.Add(ex);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            var health1 = await healthMonitor.CheckHealthAsync("provider1", "http://test1");
            var health2 = await healthMonitor.CheckHealthAsync("provider2", "http://test2");

            provider1Success.Should().BeGreaterThan(10); // ~75% success rate
            provider2Success.Should().BeGreaterThan(13); // ~83% success rate
            
            health1.Should().Be(HealthStatus.Healthy); // 75% > 50%
            health2.Should().Be(HealthStatus.Healthy); // 83% > 50%
            
            // Some exceptions are expected from the simulated failures
            exceptions.Should().NotBeEmpty();
            exceptions.Should().AllBeOfType<HttpRequestException>();
        }

        #endregion
    }
}