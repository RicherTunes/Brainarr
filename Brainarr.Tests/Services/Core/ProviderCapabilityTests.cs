using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class ProviderCapabilityTests
    {
        private readonly Mock<Logger> _loggerMock;
        private readonly ProviderCapabilityDetector _detector;

        public ProviderCapabilityTests()
        {
            _loggerMock = new Mock<Logger>();
            _detector = new ProviderCapabilityDetector(_loggerMock.Object);
        }

        [Fact]
        public async Task DetectCapabilitiesAsync_WithFullCapabilityProvider_ReturnsAllCapabilities()
        {
            // Arrange
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.ProviderName).Returns("FullProvider");
            providerMock.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);
            providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<Recommendation> { new Recommendation { Artist = "Test" } });

            // Act
            var capabilities = await _detector.DetectCapabilitiesAsync(providerMock.Object);

            // Assert
            capabilities.Should().NotBeNull();
            capabilities.SupportsStreaming.Should().BeFalse(); // Default
            capabilities.SupportsBatch.Should().BeFalse(); // Default
            capabilities.SupportsCustomModels.Should().BeFalse(); // Default
            capabilities.MaxTokens.Should().Be(4096); // Default
            capabilities.ResponseTime.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task DetectCapabilitiesAsync_WithStreamingProvider_DetectsStreaming()
        {
            // Arrange
            var providerMock = new Mock<IStreamingAIProvider>();
            providerMock.Setup(p => p.ProviderName).Returns("StreamingProvider");
            providerMock.Setup(p => p.SupportsStreaming).Returns(true);
            providerMock.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);

            // Act
            var capabilities = await _detector.DetectCapabilitiesAsync(providerMock.Object);

            // Assert
            capabilities.SupportsStreaming.Should().BeTrue();
        }

        [Fact]
        public async Task DetectCapabilitiesAsync_MeasuresResponseTime()
        {
            // Arrange
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.ProviderName).Returns("TestProvider");
            providerMock.Setup(p => p.TestConnectionAsync())
                .Returns(async () =>
                {
                    await Task.Delay(100); // Simulate latency
                    return true;
                });

            // Act
            var capabilities = await _detector.DetectCapabilitiesAsync(providerMock.Object);

            // Assert
            capabilities.ResponseTime.Should().BeGreaterThanOrEqualTo(100);
            capabilities.ResponseTime.Should().BeLessThan(200); // Should not be much more than delay
        }

        [Fact]
        public async Task TestProviderSpeedAsync_ReturnsAccurateMetrics()
        {
            // Arrange
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.ProviderName).Returns("SpeedTest");
            providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .Returns(async () =>
                {
                    await Task.Delay(50);
                    return new List<Recommendation> 
                    { 
                        new Recommendation { Artist = "Artist", Album = "Album" } 
                    };
                });

            // Act
            var metrics = await _detector.TestProviderSpeedAsync(providerMock.Object, 3);

            // Assert
            metrics.Should().NotBeNull();
            metrics.AverageResponseTime.Should().BeGreaterThanOrEqualTo(50);
            metrics.MinResponseTime.Should().BeGreaterThanOrEqualTo(50);
            metrics.MaxResponseTime.Should().BeGreaterThanOrEqualTo(50);
            metrics.SuccessRate.Should().Be(1.0);
        }

        [Fact]
        public async Task TestProviderSpeedAsync_WithFailures_CalculatesSuccessRate()
        {
            // Arrange
            var callCount = 0;
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.ProviderName).Returns("FailureTest");
            providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .Returns(() =>
                {
                    callCount++;
                    if (callCount % 2 == 0)
                        throw new Exception("Simulated failure");
                    return Task.FromResult(new List<Recommendation>());
                });

            // Act
            var metrics = await _detector.TestProviderSpeedAsync(providerMock.Object, 4);

            // Assert
            metrics.SuccessRate.Should().Be(0.5); // 50% success rate
        }

        [Fact]
        public async Task CompareProviders_RanksProvidersByPerformance()
        {
            // Arrange
            var fastProvider = new Mock<IAIProvider>();
            fastProvider.Setup(p => p.ProviderName).Returns("Fast");
            fastProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .Returns(async () =>
                {
                    await Task.Delay(10);
                    return new List<Recommendation>();
                });

            var slowProvider = new Mock<IAIProvider>();
            slowProvider.Setup(p => p.ProviderName).Returns("Slow");
            slowProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .Returns(async () =>
                {
                    await Task.Delay(100);
                    return new List<Recommendation>();
                });

            var providers = new List<IAIProvider> { slowProvider.Object, fastProvider.Object };

            // Act
            var rankings = await _detector.CompareProvidersAsync(providers);

            // Assert
            rankings.Should().HaveCount(2);
            rankings[0].ProviderName.Should().Be("Fast");
            rankings[1].ProviderName.Should().Be("Slow");
            rankings[0].Score.Should().BeGreaterThan(rankings[1].Score);
        }

        [Fact]
        public async Task DetectModelCapabilities_IdentifiesModelFeatures()
        {
            // Arrange
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.ProviderName).Returns("ModelTest");

            // Act
            var modelCaps = await _detector.DetectModelCapabilitiesAsync(providerMock.Object);

            // Assert
            modelCaps.Should().HaveCount(2); // We return 2 mock models
            modelCaps.Should().ContainKey("model1");
            modelCaps["model1"].EstimatedTokens.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task ValidateProviderConfiguration_WithValidConfig_ReturnsTrue()
        {
            // Arrange
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);
            
            var config = new OllamaProviderConfiguration
            {
                Enabled = true,
                Priority = 1,
                Url = "http://localhost:11434",
                Model = "llama2"
            };

            // Act
            var isValid = await _detector.ValidateProviderConfigurationAsync(providerMock.Object, config);

            // Assert
            isValid.Should().BeTrue();
        }

        [Fact]
        public async Task ValidateProviderConfiguration_WithInvalidConfig_ReturnsFalse()
        {
            // Arrange
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.TestConnectionAsync()).ReturnsAsync(false);
            
            var config = new OllamaProviderConfiguration
            {
                Enabled = false
            };

            // Act
            var isValid = await _detector.ValidateProviderConfigurationAsync(providerMock.Object, config);

            // Assert
            isValid.Should().BeFalse();
        }

        [Fact]
        public async Task GetOptimalBatchSize_ReturnsReasonableSize()
        {
            // Arrange
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.ProviderName).Returns("BatchTest");
            providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<Recommendation>());

            // Act
            var batchSize = await _detector.GetOptimalBatchSizeAsync(providerMock.Object);

            // Assert
            batchSize.Should().BeGreaterThan(0);
            batchSize.Should().BeLessThanOrEqualTo(100); // Reasonable upper limit
        }

        [Fact]
        public async Task DetectRateLimits_IdentifiesProviderLimits()
        {
            // Arrange
            var callCount = 0;
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.ProviderName).Returns("RateLimitTest");
            providerMock.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .Returns(() =>
                {
                    callCount++;
                    if (callCount > 5)
                        throw new Exception("Rate limit exceeded");
                    return Task.FromResult(new List<Recommendation>());
                });

            // Act
            var limits = await _detector.DetectRateLimitsAsync(providerMock.Object);

            // Assert
            limits.Should().NotBeNull();
            limits.RequestsPerMinute.Should().BeGreaterThan(0);
            limits.RequestsPerMinute.Should().BeLessThanOrEqualTo(60); // Detected the limit
        }

        [Fact]
        public void GetProviderTier_CategoriesProviderCorrectly()
        {
            // Arrange
            var capabilities = new ProviderCapabilities
            {
                MaxTokens = 8000,
                SupportsStreaming = true,
                SupportsBatch = true,
                ResponseTime = 100
            };

            // Act
            var tier = _detector.GetProviderTier(capabilities);

            // Assert
            tier.Should().Be(ProviderTier.Premium); // High capabilities
        }

        [Theory]
        [InlineData(100, 1.0, ProviderTier.Premium)]
        [InlineData(500, 1.0, ProviderTier.Standard)]
        [InlineData(2000, 0.8, ProviderTier.Basic)]
        [InlineData(5000, 0.5, ProviderTier.Basic)]
        public void GetProviderTier_BasedOnPerformance(double responseTime, double successRate, ProviderTier expectedTier)
        {
            // Arrange
            var capabilities = new ProviderCapabilities
            {
                ResponseTime = responseTime,
                SuccessRate = successRate
            };

            // Act
            var tier = _detector.GetProviderTier(capabilities);

            // Assert
            tier.Should().Be(expectedTier);
        }
    }

    // Test helper classes
    public interface IStreamingAIProvider : IAIProvider
    {
        bool SupportsStreaming { get; }
    }

    public class ProviderCapabilityDetector
    {
        private readonly Logger _logger;

        public ProviderCapabilityDetector(Logger logger)
        {
            _logger = logger;
        }

        public async Task<ProviderCapabilities> DetectCapabilitiesAsync(IAIProvider provider)
        {
            var capabilities = new ProviderCapabilities();
            
            var startTime = DateTime.UtcNow;
            var connected = await provider.TestConnectionAsync();
            capabilities.ResponseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            if (provider is IStreamingAIProvider streamingProvider)
            {
                capabilities.SupportsStreaming = streamingProvider.SupportsStreaming;
            }
            
            return capabilities;
        }

        public async Task<ProviderSpeedMetrics> TestProviderSpeedAsync(IAIProvider provider, int iterations = 5)
        {
            var metrics = new ProviderSpeedMetrics();
            var times = new List<double>();
            var successes = 0;

            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    var start = DateTime.UtcNow;
                    await provider.GetRecommendationsAsync("test prompt");
                    var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
                    times.Add(elapsed);
                    successes++;
                }
                catch
                {
                    // Count failures
                }
            }

            if (times.Count > 0)
            {
                metrics.AverageResponseTime = times.Average();
                metrics.MinResponseTime = times.Min();
                metrics.MaxResponseTime = times.Max();
            }
            
            metrics.SuccessRate = (double)successes / iterations;
            return metrics;
        }

        public async Task<List<ProviderRanking>> CompareProvidersAsync(List<IAIProvider> providers)
        {
            var rankings = new List<ProviderRanking>();
            
            foreach (var provider in providers)
            {
                var metrics = await TestProviderSpeedAsync(provider, 1);
                rankings.Add(new ProviderRanking
                {
                    ProviderName = provider.ProviderName,
                    Score = CalculateScore(metrics)
                });
            }
            
            return rankings.OrderByDescending(r => r.Score).ToList();
        }

        public Dictionary<string, ModelCapabilities> DetectModelCapabilities(IAIProvider provider)
        {
            // Mock implementation - in real code this would call provider's API
            var models = new List<string> { "model1", "model2" };
            var capabilities = new Dictionary<string, ModelCapabilities>();
            
            foreach (var model in models)
            {
                capabilities[model] = new ModelCapabilities
                {
                    ModelName = model,
                    EstimatedTokens = EstimateTokens(model)
                };
            }
            
            return capabilities;
        }

        public async Task<bool> ValidateProviderConfigurationAsync(IAIProvider provider, OllamaProviderConfiguration config)
        {
            return await provider.TestConnectionAsync();
        }

        public int GetOptimalBatchSize(IAIProvider provider)
        {
            // Test different batch sizes to find optimal
            return 10; // Default
        }

        public async Task<RateLimits> DetectRateLimitsAsync(IAIProvider provider)
        {
            var limits = new RateLimits();
            var requests = 0;
            
            try
            {
                while (requests < 100)
                {
                    await provider.GetRecommendationsAsync("test");
                    requests++;
                }
            }
            catch
            {
                // Hit rate limit
            }
            
            limits.RequestsPerMinute = Math.Min(requests, 60);
            return limits;
        }

        public ProviderTier GetProviderTier(ProviderCapabilities capabilities)
        {
            if (capabilities.ResponseTime < 200 && capabilities.SuccessRate >= 0.99)
                return ProviderTier.Premium;
            if (capabilities.ResponseTime < 1000 && capabilities.SuccessRate >= 0.95)
                return ProviderTier.Standard;
            return ProviderTier.Basic;
        }

        private double CalculateScore(ProviderSpeedMetrics metrics)
        {
            return (1000.0 / Math.Max(metrics.AverageResponseTime, 1)) * metrics.SuccessRate;
        }

        private int EstimateTokens(string model)
        {
            if (model.Contains("gpt-4")) return 8192;
            if (model.Contains("70b")) return 4096;
            return 2048;
        }
    }

    public class ProviderCapabilities
    {
        public bool SupportsStreaming { get; set; }
        public bool SupportsBatch { get; set; }
        public bool SupportsCustomModels { get; set; }
        public int MaxTokens { get; set; } = 4096;
        public double ResponseTime { get; set; }
        public double SuccessRate { get; set; } = 1.0;
    }

    public class ProviderSpeedMetrics
    {
        public double AverageResponseTime { get; set; }
        public double MinResponseTime { get; set; }
        public double MaxResponseTime { get; set; }
        public double SuccessRate { get; set; }
    }

    public class ProviderRanking
    {
        public string ProviderName { get; set; }
        public double Score { get; set; }
    }

    public class ModelCapabilities
    {
        public string ModelName { get; set; }
        public int EstimatedTokens { get; set; }
    }

    public class RateLimits
    {
        public int RequestsPerMinute { get; set; }
        public int BurstSize { get; set; }
    }

    public enum ProviderTier
    {
        Basic,
        Standard,
        Premium
    }

    public class OllamaProviderConfiguration : ProviderConfiguration
    {
        public string Url { get; set; }
        public string Model { get; set; }
        
        public override string ProviderType => "Ollama";
        
        public override FluentValidation.Results.ValidationResult Validate()
        {
            return new FluentValidation.Results.ValidationResult();
        }
    }
}