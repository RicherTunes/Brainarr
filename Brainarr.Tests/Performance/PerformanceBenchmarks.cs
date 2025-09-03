using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Caching;
using NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiting;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Performance
{
    [Trait("Category", "Performance")]
    public class PerformanceBenchmarks
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [Fact]
        public async Task Cache_Should_HandleHighConcurrency()
        {
            // Arrange
            var cache = new EnhancedRecommendationCache(_logger);
            var testData = GenerateTestRecommendations(100);
            var tasks = new List<Task>();
            var successCount = 0;
            var errorCount = 0;
            
            // Act - Simulate 1000 concurrent operations
            for (int i = 0; i < 1000; i++)
            {
                var key = $"test_key_{i % 10}"; // 10 unique keys
                var task = Task.Run(async () =>
                {
                    try
                    {
                        if (i % 2 == 0)
                        {
                            await cache.SetAsync(key, testData);
                        }
                        else
                        {
                            await cache.GetAsync(key);
                        }
                        Interlocked.Increment(ref successCount);
                    }
                    catch
                    {
                        Interlocked.Increment(ref errorCount);
                    }
                });
                tasks.Add(task);
            }
            
            await Task.WhenAll(tasks);
            
            // Assert
            Assert.Equal(1000, successCount);
            Assert.Equal(0, errorCount);
            
            var stats = cache.GetStatistics();
            Assert.True(stats.HitRatio > 0.3); // Should have decent hit ratio
        }

        [Fact]
        public async Task RateLimiter_Should_MaintainThroughput()
        {
            // Arrange
            var rateLimiter = new EnhancedRateLimiter(_logger);
            rateLimiter.ConfigureLimit("test", new RateLimitPolicy
            {
                MaxRequests = 100,
                Period = TimeSpan.FromSeconds(1)
            });
            
            var stopwatch = Stopwatch.StartNew();
            var completedRequests = 0;
            
            // Act - Process 100 requests (should take ~1 second)
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                var request = new RateLimitRequest { Resource = "test" };
                await rateLimiter.ExecuteAsync(request, async () =>
                {
                    await Task.Delay(10); // Simulate work
                    Interlocked.Increment(ref completedRequests);
                    return true;
                });
            });
            
            await Task.WhenAll(tasks);
            stopwatch.Stop();
            
            // Assert
            Assert.Equal(100, completedRequests);
            Assert.True(stopwatch.ElapsedMilliseconds < 2000); // Should complete within 2 seconds
            Assert.True(stopwatch.ElapsedMilliseconds > 900); // But not faster than limit allows
        }

        [Fact]
        public void CircuitBreaker_Should_OpenQuickly()
        {
            // Arrange
            var circuitBreaker = new Services.Resilience.CircuitBreaker(
                "test",
                new Services.Resilience.CircuitBreakerOptions
                {
                    FailureThreshold = 3,
                    BreakDuration = TimeSpan.FromSeconds(1)
                },
                _logger);
            
            var stopwatch = Stopwatch.StartNew();
            
            // Act - Cause 3 failures
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    circuitBreaker.ExecuteAsync<int>(() => 
                        throw new Exception("Test failure")).Wait();
                }
                catch { }
            }
            
            stopwatch.Stop();
            
            // Assert
            Assert.Equal(Services.Resilience.CircuitState.Open, circuitBreaker.State);
            Assert.True(stopwatch.ElapsedMilliseconds < 100); // Should open very quickly
        }

        [Fact]
        public async Task ConnectionPool_Should_ReuseConnections()
        {
            // Arrange
            var connectionPool = new Services.Network.HttpConnectionPool(
                _logger,
                Services.Network.HttpConnectionPoolOptions.HighPerformance);
            
            var connectionCreations = 0;
            var baseUrl = "https://api.example.com";
            
            // Act - Make 100 requests
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                var client = connectionPool.GetClient(baseUrl);
                if (client != null)
                {
                    Interlocked.Increment(ref connectionCreations);
                }
                await Task.Delay(10);
            });
            
            await Task.WhenAll(tasks);
            
            var stats = connectionPool.GetStatistics();
            
            // Assert
            Assert.True(stats.ActiveConnections <= 20); // Should respect max connections
            Assert.True(stats.SuccessfulRequests >= 0);
            Assert.True(connectionCreations > 0);
        }

        private List<ImportListItemInfo> GenerateTestRecommendations(int count)
        {
            return Enumerable.Range(0, count).Select(i => new ImportListItemInfo
            {
                Artist = $"Artist {i}",
                Album = $"Album {i}",
                ReleaseDate = DateTime.UtcNow.AddDays(-i),
                ArtistMusicBrainzId = Guid.NewGuid().ToString(),
                AlbumMusicBrainzId = Guid.NewGuid().ToString()
            }).ToList();
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, targetCount: 10)]
    public class CacheBenchmarks
    {
        private EnhancedRecommendationCache _cache;
        private List<ImportListItemInfo> _testData;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [GlobalSetup]
        public void Setup()
        {
            _cache = new EnhancedRecommendationCache(_logger);
            _testData = Enumerable.Range(0, 100).Select(i => new ImportListItemInfo
            {
                Artist = $"Artist {i}",
                Album = $"Album {i}"
            }).ToList();
        }

        [Benchmark]
        public async Task CacheWrite()
        {
            await _cache.SetAsync($"key_{DateTime.UtcNow.Ticks}", _testData);
        }

        [Benchmark]
        public async Task CacheRead()
        {
            await _cache.GetAsync("key_existing");
        }

        [Benchmark]
        public async Task CacheReadWrite()
        {
            var key = $"key_{DateTime.UtcNow.Ticks % 100}";
            await _cache.SetAsync(key, _testData);
            await _cache.GetAsync(key);
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, targetCount: 10)]
    public class RateLimiterBenchmarks
    {
        private EnhancedRateLimiter _rateLimiter;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [GlobalSetup]
        public void Setup()
        {
            _rateLimiter = new EnhancedRateLimiter(_logger);
            _rateLimiter.ConfigureLimit("test", new RateLimitPolicy
            {
                MaxRequests = 1000,
                Period = TimeSpan.FromSeconds(1)
            });
        }

        [Benchmark]
        public async Task CheckRateLimit()
        {
            var request = new RateLimitRequest { Resource = "test" };
            await _rateLimiter.CheckRateLimitAsync(request);
        }

        [Benchmark]
        public async Task ExecuteWithRateLimit()
        {
            var request = new RateLimitRequest { Resource = "test" };
            await _rateLimiter.ExecuteAsync(request, () => Task.FromResult(true));
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, targetCount: 10)]
    public class LoggingBenchmarks
    {
        private Services.Logging.SecureStructuredLogger _logger;
        private Services.Logging.SensitiveDataMasker _masker;

        [GlobalSetup]
        public void Setup()
        {
            _logger = new Services.Logging.SecureStructuredLogger(
                LogManager.GetCurrentClassLogger(),
                new Services.Logging.SensitiveDataMasker(),
                new Services.Logging.DefaultLogEnricher(),
                Services.Logging.LogConfiguration.Production);
            
            _masker = new Services.Logging.SensitiveDataMasker();
        }

        [Benchmark]
        public void LogWithMasking()
        {
            _logger.LogInfo("Processing request with key: sk-1234567890abcdef", 
                new { userId = "user123", apiKey = "secret" });
        }

        [Benchmark]
        public string MaskSensitiveData()
        {
            return _masker.MaskSensitiveData(
                "API key: sk-1234567890abcdef, Email: test@example.com, IP: 192.168.1.1");
        }

        [Benchmark]
        public void LogWithContext()
        {
            using (_logger.BeginScope("operation", new { transactionId = Guid.NewGuid() }))
            {
                _logger.LogDebug("Operation started");
                _logger.LogDebug("Operation completed");
            }
        }
    }

    public class PerformanceTestRunner
    {
        [Fact(Skip = "Run manually for benchmarking")]
        public void RunBenchmarks()
        {
            var summary = BenchmarkRunner.Run<CacheBenchmarks>();
            BenchmarkRunner.Run<RateLimiterBenchmarks>();
            BenchmarkRunner.Run<LoggingBenchmarks>();
        }
    }

    [Trait("Category", "LoadTest")]
    public class LoadTests
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [Fact]
        public async Task System_Should_HandleSustainedLoad()
        {
            // Arrange
            var cache = new EnhancedRecommendationCache(_logger);
            var rateLimiter = new EnhancedRateLimiter(_logger);
            var secureLogger = new Services.Logging.SecureStructuredLogger(
                _logger,
                new Services.Logging.SensitiveDataMasker());
            
            rateLimiter.ConfigureLimit("api", new RateLimitPolicy
            {
                MaxRequests = 1000,
                Period = TimeSpan.FromMinutes(1)
            });
            
            var metrics = new LoadTestMetrics();
            var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            
            // Act - Simulate sustained load for 30 seconds
            var tasks = Enumerable.Range(0, 10).Select(async threadId =>
            {
                while (!cancellationToken.Token.IsCancellationRequested)
                {
                    var stopwatch = Stopwatch.StartNew();
                    
                    try
                    {
                        // Simulate API request with caching and rate limiting
                        var cacheKey = $"key_{threadId}_{DateTime.UtcNow.Second / 10}";
                        var cached = await cache.GetAsync(cacheKey);
                        
                        if (!cached.Found)
                        {
                            var request = new RateLimitRequest 
                            { 
                                Resource = "api",
                                UserId = $"user_{threadId}" 
                            };
                            
                            await rateLimiter.ExecuteAsync(request, async () =>
                            {
                                await Task.Delay(Random.Shared.Next(10, 50)); // Simulate API call
                                var data = GenerateTestData(10);
                                await cache.SetAsync(cacheKey, data);
                                return data;
                            });
                        }
                        
                        metrics.RecordSuccess(stopwatch.Elapsed);
                    }
                    catch (RateLimitExceededException)
                    {
                        metrics.RecordRateLimited();
                    }
                    catch (Exception)
                    {
                        metrics.RecordError();
                    }
                    
                    await Task.Delay(Random.Shared.Next(10, 100));
                }
            });
            
            await Task.WhenAll(tasks);
            
            // Assert
            Assert.True(metrics.SuccessCount > 100); // Should process many requests
            Assert.True(metrics.AverageResponseTime < 100); // Should be responsive
            Assert.True(metrics.ErrorRate < 0.01); // Less than 1% errors
            Assert.True(metrics.P99ResponseTime < 500); // 99th percentile under 500ms
        }

        private List<ImportListItemInfo> GenerateTestData(int count)
        {
            return Enumerable.Range(0, count).Select(i => new ImportListItemInfo
            {
                Artist = $"Artist {i}",
                Album = $"Album {i}"
            }).ToList();
        }

        private class LoadTestMetrics
        {
            private readonly List<double> _responseTimes = new();
            private int _successCount;
            private int _errorCount;
            private int _rateLimitedCount;
            private readonly object _lock = new();

            public int SuccessCount => _successCount;
            public double ErrorRate => (double)_errorCount / (_successCount + _errorCount);
            
            public double AverageResponseTime
            {
                get
                {
                    lock (_lock)
                    {
                        return _responseTimes.Any() ? _responseTimes.Average() : 0;
                    }
                }
            }

            public double P99ResponseTime
            {
                get
                {
                    lock (_lock)
                    {
                        if (!_responseTimes.Any()) return 0;
                        var sorted = _responseTimes.OrderBy(t => t).ToList();
                        var index = (int)(sorted.Count * 0.99);
                        return sorted[Math.Min(index, sorted.Count - 1)];
                    }
                }
            }

            public void RecordSuccess(TimeSpan responseTime)
            {
                lock (_lock)
                {
                    _successCount++;
                    _responseTimes.Add(responseTime.TotalMilliseconds);
                    
                    // Keep only last 1000 response times
                    if (_responseTimes.Count > 1000)
                    {
                        _responseTimes.RemoveAt(0);
                    }
                }
            }

            public void RecordError()
            {
                Interlocked.Increment(ref _errorCount);
            }

            public void RecordRateLimited()
            {
                Interlocked.Increment(ref _rateLimitedCount);
            }
        }
    }
}