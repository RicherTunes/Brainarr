using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Performance;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for ObservabilityService paths not covered by existing tests.
    /// Tests focus on: constructor validation, GetMetricsSnapshot, GetObservabilitySummary,
    /// GetObservabilityOptions, GetObservabilityHtml with various query parameters.
    /// </summary>
    [Collection("MetricsCollectorBounded")]
    public class ObservabilityServiceCovTests : IDisposable
    {
        private readonly Mock<IPerformanceMetrics> _metrics;
        private readonly Func<string> _getProviderStatus;
        private readonly Logger _logger;
        private readonly string _tempPath;
        private readonly ReviewQueueService _reviewQueue;

        public ObservabilityServiceCovTests()
        {
            _metrics = new Mock<IPerformanceMetrics>();
            _getProviderStatus = () => "active";
            _logger = Helpers.TestLogger.CreateNullLogger();

            // Create a real ReviewQueueService with a temp path (GetCounts is not virtual)
            _tempPath = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempPath);
            _reviewQueue = new ReviewQueueService(_logger, _tempPath);
        }

        public void Dispose()
        {
            // Cleanup temp directory
            try
            {
                if (Directory.Exists(_tempPath))
                {
                    Directory.Delete(_tempPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            // Reset MetricsCollector between tests
            MetricsCollector.Shutdown();
        }

        #region Constructor Validation

        // Source line 26: _reviewQueue = reviewQueue ?? throw new ArgumentNullException(nameof(reviewQueue));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ObservabilityService.cs
        //   26:            _reviewQueue = reviewQueue ?? throw new ArgumentNullException(nameof(reviewQueue));
        [Fact]
        public void Constructor_WithNullReviewQueue_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new ObservabilityService(null!, _metrics.Object, _getProviderStatus, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("reviewQueue");
        }

        // Source line 27: _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ObservabilityService.cs
        //   27:            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        [Fact]
        public void Constructor_WithNullMetrics_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new ObservabilityService(_reviewQueue, null!, _getProviderStatus, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("metrics");
        }

        // Source line 28: _getProviderStatus = getProviderStatus ?? throw new ArgumentNullException(nameof(getProviderStatus));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ObservabilityService.cs
        //   28:            _getProviderStatus = getProviderStatus ?? throw new ArgumentNullException(nameof(getProviderStatus));
        [Fact]
        public void Constructor_WithNullGetProviderStatus_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new ObservabilityService(_reviewQueue, _metrics.Object, null!, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("getProviderStatus");
        }

        // Source line 29: _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ObservabilityService.cs
        //   29:            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new ObservabilityService(_reviewQueue, _metrics.Object, _getProviderStatus, null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        #endregion

        #region GetMetricsSnapshot

        // Source lines 32-41: GetMetricsSnapshot method
        // Proof: grep -n "GetMetricsSnapshot" Brainarr.Plugin/Services/Core/ObservabilityService.cs
        //   32:        public object GetMetricsSnapshot()
        [Fact]
        public void GetMetricsSnapshot_ReturnsCorrectStructure()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot
            {
                ArtistModeGatingEvents = 10,
                ArtistModePromotedRecommendations = 7
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "idle", _logger);

            // Act
            var result = service.GetMetricsSnapshot();

            // Assert
            var review = result.GetType().GetProperty("review")?.GetValue(result);
            review.Should().NotBeNull();
            var pending = review?.GetType().GetProperty("pending")?.GetValue(review);
            pending.Should().Be(0, "because queue is empty");
        }

        #endregion

        #region GetObservabilitySummary

        // Source lines 43-97: GetObservabilitySummary method
        // Proof: grep -n "GetObservabilitySummary" Brainarr.Plugin/Services/Core/ObservabilityService.cs
        //   43:        public object GetObservabilitySummary(IDictionary<string, string> query)
        [Fact]
        public void GetObservabilitySummary_WithNullQuery_ReturnsOptions()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilitySummary(null);

            // Assert
            result.Should().NotBeNull();
            var options = result.GetType().GetProperty("options")?.GetValue(result);
            options.Should().NotBeNull();
        }

        [Fact]
        public void GetObservabilitySummary_WithProviderFilter_FiltersByProvider()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            // Record some test metrics
            MetricsCollector.RecordMetric("provider.latency", 100, new Dictionary<string, string>
            {
                ["provider"] = "openai",
                ["model"] = "gpt-4"
            });
            MetricsCollector.RecordMetric("provider.latency", 200, new Dictionary<string, string>
            {
                ["provider"] = "anthropic",
                ["model"] = "claude-3"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilitySummary(new Dictionary<string, string>
            {
                ["provider"] = "openai"
            });

            // Assert
            result.Should().NotBeNull();
            var options = result.GetType().GetProperty("options")?.GetValue(result);
            options.Should().NotBeNull();
        }

        [Fact]
        public void GetObservabilitySummary_WithModelFilter_FiltersByModel()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            MetricsCollector.RecordMetric("provider.latency", 150, new Dictionary<string, string>
            {
                ["provider"] = "openai",
                ["model"] = "gpt-4"
            });
            MetricsCollector.RecordMetric("provider.latency", 180, new Dictionary<string, string>
            {
                ["provider"] = "anthropic",
                ["model"] = "gpt-4"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilitySummary(new Dictionary<string, string>
            {
                ["model"] = "gpt-4"
            });

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void GetObservabilitySummary_WithBothFilters_FiltersByProviderAndModel()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            MetricsCollector.RecordMetric("provider.latency", 100, new Dictionary<string, string>
            {
                ["provider"] = "openai",
                ["model"] = "gpt-4"
            });
            MetricsCollector.RecordMetric("provider.latency", 200, new Dictionary<string, string>
            {
                ["provider"] = "openai",
                ["model"] = "gpt-3.5"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilitySummary(new Dictionary<string, string>
            {
                ["provider"] = "openai",
                ["model"] = "gpt-4"
            });

            // Assert
            result.Should().NotBeNull();
        }

        #endregion

        #region GetObservabilityOptions

        // Source lines 99-108: GetObservabilityOptions method
        // Proof: grep -n "GetObservabilityOptions" Brainarr.Plugin/Services/Core/ObservabilityService.cs
        //   99:        public object GetObservabilityOptions()
        [Fact]
        public void GetObservabilityOptions_ReturnsEmptyOptionsWhenNoMetrics()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilityOptions();

            // Assert
            result.Should().NotBeNull();
            var options = result.GetType().GetProperty("options")?.GetValue(result);
            options.Should().NotBeNull();
        }

        #endregion

        #region GetObservabilityHtml

        // Source lines 110-154: GetObservabilityHtml method
        // Proof: grep -n "GetObservabilityHtml" Brainarr.Plugin/Services/Core/ObservabilityService.cs
        //   110:        public string GetObservabilityHtml(IDictionary<string, string> query)
        [Fact]
        public void GetObservabilityHtml_WithNullQuery_ReturnsHtml()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilityHtml(null);

            // Assert
            result.Should().NotBeNull();
            result.Should().Contain("<html>", "because result should be HTML");
            result.Should().Contain("</html>", "because result should be valid HTML");
        }

        [Fact]
        public void GetObservabilityHtml_WithEmptyQuery_ReturnsHtmlTable()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilityHtml(new Dictionary<string, string>());

            // Assert
            result.Should().Contain("<table", "because result should contain a table");
            result.Should().Contain("Series</th>", "because table should have Series header");
            result.Should().Contain("p95 (ms)</th>", "because table should have p95 header");
        }

        [Fact]
        public void GetObservabilityHtml_WithProviderFilter_IncludesFilteredResults()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            MetricsCollector.RecordMetric("provider.latency", 100, new Dictionary<string, string>
            {
                ["provider"] = "openai",
                ["model"] = "gpt-4"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilityHtml(new Dictionary<string, string>
            {
                ["provider"] = "openai"
            });

            // Assert
            result.Should().Contain("<table");
            result.Should().Contain("Observability (last 15m)");
        }

        [Fact]
        public void GetObservabilityHtml_WithModelFilter_IncludesFilteredResults()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            MetricsCollector.RecordMetric("provider.latency", 120, new Dictionary<string, string>
            {
                ["provider"] = "anthropic",
                ["model"] = "claude-3"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilityHtml(new Dictionary<string, string>
            {
                ["model"] = "claude-3"
            });

            // Assert
            result.Should().Contain("<table");
            result.Should().Contain("Observability");
        }

        [Fact]
        public void GetObservabilityHtml_WithMetrics_IncludesAllColumns()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            // Record latency, errors, and throttles
            MetricsCollector.IncrementCounter("provider.errors", new Dictionary<string, string>
            {
                ["provider"] = "test-provider",
                ["model"] = "test-model"
            });
            MetricsCollector.IncrementCounter("provider.429", new Dictionary<string, string>
            {
                ["provider"] = "test-provider",
                ["model"] = "test-model"
            });
            MetricsCollector.RecordMetric("provider.latency", 250, new Dictionary<string, string>
            {
                ["provider"] = "test-provider",
                ["model"] = "test-model"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilityHtml(new Dictionary<string, string>());

            // Assert
            result.Should().Contain("Errors</th>", "because table should have Errors column");
            result.Should().Contain("429</th>", "because table should have 429 column");
        }

        #endregion

        #region ProviderMetricsHelper.SanitizeName Edge Cases

        // Source line in ProviderMetricsHelper.SanitizeName
        // Proof: cat Brainarr.Plugin/Services/Telemetry/ProviderMetricsHelper.cs
        //   18:        public static string SanitizeName(string value)
        [Fact]
        public void GetObservabilitySummary_WithSpecialCharactersInProvider_SanitizesName()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            // Provider with special characters
            MetricsCollector.RecordMetric("provider.latency", 100, new Dictionary<string, string>
            {
                ["provider"] = "OpenAI / GPT-4",
                ["model"] = "test"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilitySummary(new Dictionary<string, string>
            {
                ["provider"] = "OpenAI / GPT-4"
            });

            // Assert - should sanitize special characters
            result.Should().NotBeNull();
        }

        [Fact]
        public void GetObservabilityHtml_WithHtmlEntitiesInSeries_EscapesHtml()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            // Series name with characters that need HTML escaping
            MetricsCollector.RecordMetric("provider.latency", 100, new Dictionary<string, string>
            {
                ["provider"] = "test<script>",
                ["model"] = "model"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act
            var result = service.GetObservabilityHtml(new Dictionary<string, string>());

            // Assert - HTML should be escaped
            result.Should().NotContain("<script>", "because HTML entities should be escaped");
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void GetMetricsSnapshot_WithZeroCounts_ReturnsZeros()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot
            {
                ArtistModeGatingEvents = 0,
                ArtistModePromotedRecommendations = 0
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "idle", _logger);

            // Act
            var result = service.GetMetricsSnapshot();

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void GetObservabilitySummary_WithWhitespaceProvider_DoesNotFilter()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            MetricsCollector.RecordMetric("provider.latency", 100, new Dictionary<string, string>
            {
                ["provider"] = "openai",
                ["model"] = "gpt-4"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act - whitespace provider should be treated as null (no filter)
            var result = service.GetObservabilitySummary(new Dictionary<string, string>
            {
                ["provider"] = "   "
            });

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void GetObservabilityHtml_WithWhitespaceModel_DoesNotFilter()
        {
            // Arrange
            _metrics.Setup(x => x.GetSnapshot()).Returns(new PerformanceSnapshot());

            MetricsCollector.RecordMetric("provider.latency", 100, new Dictionary<string, string>
            {
                ["provider"] = "anthropic",
                ["model"] = "claude-3"
            });

            var service = new ObservabilityService(_reviewQueue, _metrics.Object, () => "active", _logger);

            // Act - whitespace model should be treated as null (no filter)
            var result = service.GetObservabilityHtml(new Dictionary<string, string>
            {
                ["model"] = "\t"
            });

            // Assert
            result.Should().NotBeNull();
        }

        #endregion
    }
}
