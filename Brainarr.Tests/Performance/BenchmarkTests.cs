using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Music;
using Brainarr.Plugin;
using Brainarr.Plugin.Services;
using Brainarr.Plugin.Services.Core;
using Brainarr.Plugin.Services.Library;

namespace Brainarr.Tests.Performance
{
    [TestFixture]
    [Category("Performance")]
    public class BenchmarkTests
    {
        private Mock<IArtistService> _artistServiceMock;
        private Mock<IAlbumService> _albumServiceMock;
        private Logger _logger;
        
        [SetUp]
        public void Setup()
        {
            _artistServiceMock = new Mock<IArtistService>();
            _albumServiceMock = new Mock<IAlbumService>();
            _logger = LogManager.GetCurrentClassLogger();
        }
        
        [Test]
        [TestCase(100)]
        [TestCase(500)]
        [TestCase(1000)]
        [TestCase(5000)]
        public void LibraryProfileService_PerformanceWithVariousLibrarySizes(int librarySize)
        {
            // Arrange
            var artists = GenerateTestArtists(librarySize);
            var albums = GenerateTestAlbums(librarySize * 10); // 10 albums per artist average
            
            _artistServiceMock.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumServiceMock.Setup(s => s.GetAllAlbums()).Returns(albums);
            
            var service = new LibraryProfileService(_artistServiceMock.Object, _albumServiceMock.Object, _logger);
            
            // Act
            var stopwatch = Stopwatch.StartNew();
            var profile = service.GetLibraryProfile();
            stopwatch.Stop();
            
            // Assert
            Console.WriteLine($"Library size: {librarySize} artists, {albums.Count} albums");
            Console.WriteLine($"Profile generation time: {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Memory used: ~{GC.GetTotalMemory(false) / 1024 / 1024}MB");
            
            // Performance targets based on library size
            var maxAllowedTime = librarySize switch
            {
                <= 100 => 50,      // 50ms for small libraries
                <= 500 => 200,     // 200ms for medium libraries
                <= 1000 => 500,    // 500ms for large libraries
                _ => 2000          // 2s for very large libraries
            };
            
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(maxAllowedTime),
                $"Profile generation took {stopwatch.ElapsedMilliseconds}ms, expected less than {maxAllowedTime}ms");
            
            Assert.That(profile.TotalArtists, Is.EqualTo(librarySize));
            Assert.That(profile.TopGenres, Is.Not.Empty);
            Assert.That(profile.SampleArtists.Count, Is.LessThanOrEqualTo(20));
        }
        
        [Test]
        public async Task ConcurrentFetchRequests_MaintainsPerformance()
        {
            // Arrange
            var providerFactoryMock = new Mock<IAIProviderFactory>();
            var cacheMock = new Mock<IRecommendationCache>();
            var healthMonitorMock = new Mock<IProviderHealth>();
            var retryPolicyMock = new Mock<IRetryPolicy>();
            var rateLimiterMock = new Mock<IRateLimiter>();
            var modelDetectionMock = new Mock<IModelDetectionService>();
            var providerMock = new Mock<IAIProvider>();
            
            providerMock.Setup(p => p.ProviderName).Returns("TestProvider");
            providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    // Simulate some processing time
                    Task.Delay(10).Wait();
                    return GenerateTestRecommendations(10);
                });
            
            providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>()))
                .Returns(providerMock.Object);
            
            healthMonitorMock.Setup(h => h.CheckHealthAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(HealthStatus.Healthy);
            
            rateLimiterMock.Setup(r => r.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<List<Recommendation>>>>()))
                .Returns<string, Func<Task<List<Recommendation>>>>((_, func) => func());
            
            retryPolicyMock.Setup(r => r.ExecuteAsync(
                It.IsAny<Func<Task<List<Recommendation>>>>(),
                It.IsAny<string>()))
                .Returns<Func<Task<List<Recommendation>>>, string>((func, _) => func());
            
            var orchestrator = new FetchOrchestrator(
                providerFactoryMock.Object,
                cacheMock.Object,
                healthMonitorMock.Object,
                retryPolicyMock.Object,
                rateLimiterMock.Object,
                modelDetectionMock.Object,
                _logger);
            
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var libraryProfile = new LibraryProfile();
            
            // Act - Simulate concurrent fetch requests
            var stopwatch = Stopwatch.StartNew();
            var tasks = new List<Task<IList<ImportListItemInfo>>>();
            
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() => orchestrator.ExecuteFetch(settings, libraryProfile)));
            }
            
            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();
            
            // Assert
            Console.WriteLine($"10 concurrent fetches completed in: {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Average time per fetch: {stopwatch.ElapsedMilliseconds / 10}ms");
            
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000), 
                "Concurrent fetches should complete within 5 seconds");
            
            foreach (var result in results)
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Count, Is.GreaterThan(0));
            }
        }
        
        [Test]
        public void CacheKeyGeneration_PerformanceTest()
        {
            // Arrange
            var cache = new RecommendationCache(_logger);
            var iterations = 10000;
            
            // Act
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var key = cache.GenerateCacheKey(
                    $"Provider{i % 10}",
                    i % 100,
                    $"fingerprint_{i}");
            }
            stopwatch.Stop();
            
            // Assert
            var avgTimePerKey = (double)stopwatch.ElapsedMilliseconds / iterations;
            Console.WriteLine($"Generated {iterations} cache keys in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Average time per key: {avgTimePerKey:F4}ms");
            
            Assert.That(avgTimePerKey, Is.LessThan(0.1), 
                "Cache key generation should be faster than 0.1ms per key");
        }
        
        [Test]
        public void MemoryUsage_RemainsStableUnderLoad()
        {
            // Arrange
            var service = new LibraryProfileService(_artistServiceMock.Object, _albumServiceMock.Object, _logger);
            var initialMemory = GC.GetTotalMemory(true) / 1024 / 1024; // MB
            
            // Act - Process multiple library profiles
            for (int i = 0; i < 100; i++)
            {
                var artists = GenerateTestArtists(100);
                var albums = GenerateTestAlbums(1000);
                
                _artistServiceMock.Setup(s => s.GetAllArtists()).Returns(artists);
                _albumServiceMock.Setup(s => s.GetAllAlbums()).Returns(albums);
                
                var profile = service.GetLibraryProfile();
                
                // Simulate some usage
                _ = profile.TopGenres.Count;
                _ = profile.SampleArtists.Count;
            }
            
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB
            var memoryGrowth = finalMemory - initialMemory;
            
            // Assert
            Console.WriteLine($"Initial memory: {initialMemory}MB");
            Console.WriteLine($"Final memory: {finalMemory}MB");
            Console.WriteLine($"Memory growth: {memoryGrowth}MB");
            
            Assert.That(memoryGrowth, Is.LessThan(50), 
                "Memory growth should be less than 50MB after processing 100 library profiles");
        }
        
        private List<Artist> GenerateTestArtists(int count)
        {
            var genres = new[] { "Rock", "Pop", "Jazz", "Classical", "Electronic", "Hip-Hop", "Country", "Blues" };
            var random = new Random();
            
            return Enumerable.Range(1, count)
                .Select(i => new Artist
                {
                    Id = i,
                    Name = $"Artist {i}",
                    Genres = genres.OrderBy(_ => random.Next()).Take(random.Next(1, 4)).ToList(),
                    Statistics = new ArtistStatistics { AlbumCount = random.Next(1, 20) }
                })
                .ToList();
        }
        
        private List<Album> GenerateTestAlbums(int count)
        {
            var labels = new[] { "Sony", "Universal", "Warner", "EMI", "Independent", "Atlantic", "Columbia" };
            var random = new Random();
            
            return Enumerable.Range(1, count)
                .Select(i => new Album
                {
                    Id = i,
                    Title = $"Album {i}",
                    ReleaseDate = DateTime.Now.AddYears(-random.Next(0, 50)).AddDays(-random.Next(0, 365)),
                    Label = labels[random.Next(labels.Length)]
                })
                .ToList();
        }
        
        private List<Recommendation> GenerateTestRecommendations(int count)
        {
            return Enumerable.Range(1, count)
                .Select(i => new Recommendation
                {
                    Artist = $"Recommended Artist {i}",
                    Album = $"Recommended Album {i}",
                    Genre = "Test Genre",
                    Year = (2020 + i % 5).ToString()
                })
                .ToList();
        }
    }
}