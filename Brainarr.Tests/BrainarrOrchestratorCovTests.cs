using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for BrainarrOrchestrator - constructor validation, gap planner actions,
    /// observability toggle, and edge cases.
    /// Source: Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs
    /// </summary>
    public class BrainarrOrchestratorCovTests : IDisposable
    {
        private readonly Mock<IProviderFactory> _providerFactory = new();
        private readonly Mock<ILibraryAnalyzer> _libraryAnalyzer = new();
        private readonly Mock<IRecommendationCache> _cache = new();
        private readonly Mock<IProviderHealthMonitor> _health = new();
        private readonly Mock<IRecommendationValidator> _validator = new();
        private readonly Mock<IModelDetectionService> _modelDetection = new();
        private readonly Mock<IHttpClient> _http = new();
        private readonly Mock<IBreakerRegistry> _breakerRegistry = PassThroughBreakerRegistry.CreateMock();
        private readonly Logger _logger = TestLogger.CreateNullLogger();
        private readonly string _tempRoot;

        public BrainarrOrchestratorCovTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "BrainarrCovTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
        }

        /// <summary>
        /// Swaps the internal audit service with one that uses a temp directory,
        /// ensuring no cross-test contamination from persisted audit data.
        /// </summary>
        private void SwapAuditService(BrainarrOrchestrator orch)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var auditServiceType = typeof(BrainarrOrchestrator).Assembly.GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Support.ReviewActionAuditService");
            var freshAudit = Activator.CreateInstance(auditServiceType!, _logger, _tempRoot);
            var af = typeof(BrainarrOrchestrator).GetField("_auditService", flags);
            af!.SetValue(orch, freshAudit);
        }

        // ====== CONSTRUCTOR NULL ARGUMENT TESTS ======
        // Source lines 105-113, 155-156

        [Fact]
        public void Constructor_NullLogger_ThrowsArgumentNullException()
        {
            // Arrange & Act
            var act = () => new BrainarrOrchestrator(
                logger: null,
                _providerFactory.Object,
                _libraryAnalyzer.Object,
                _cache.Object,
                _health.Object,
                _validator.Object,
                _modelDetection.Object,
                _http.Object,
                breakerRegistry: _breakerRegistry.Object);

            // Assert - Source line 105: _logger = logger ?? throw new ArgumentNullException(nameof(logger))
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        [Fact]
        public void Constructor_NullProviderFactory_ThrowsArgumentNullException()
        {
            // Arrange & Act
            var act = () => new BrainarrOrchestrator(
                _logger,
                providerFactory: null,
                _libraryAnalyzer.Object,
                _cache.Object,
                _health.Object,
                _validator.Object,
                _modelDetection.Object,
                _http.Object,
                breakerRegistry: _breakerRegistry.Object);

            // Assert - Source line 106: _providerFactory = providerFactory ?? throw new ArgumentNullException
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("providerFactory");
        }

        [Fact]
        public void Constructor_NullLibraryAnalyzer_ThrowsArgumentNullException()
        {
            // Arrange & Act
            var act = () => new BrainarrOrchestrator(
                _logger,
                _providerFactory.Object,
                libraryAnalyzer: null,
                _cache.Object,
                _health.Object,
                _validator.Object,
                _modelDetection.Object,
                _http.Object,
                breakerRegistry: _breakerRegistry.Object);

            // Assert - Source line 107: _libraryAnalyzer = libraryAnalyzer ?? throw new ArgumentNullException
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("libraryAnalyzer");
        }

        [Fact]
        public void Constructor_NullCache_ThrowsArgumentNullException()
        {
            // Arrange & Act
            var act = () => new BrainarrOrchestrator(
                _logger,
                _providerFactory.Object,
                _libraryAnalyzer.Object,
                cache: null,
                _health.Object,
                _validator.Object,
                _modelDetection.Object,
                _http.Object,
                breakerRegistry: _breakerRegistry.Object);

            // Assert - Source line 108: _cache = cache ?? throw new ArgumentNullException
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("cache");
        }

        [Fact]
        public void Constructor_NullProviderHealth_ThrowsArgumentNullException()
        {
            // Arrange & Act
            var act = () => new BrainarrOrchestrator(
                _logger,
                _providerFactory.Object,
                _libraryAnalyzer.Object,
                _cache.Object,
                providerHealth: null,
                _validator.Object,
                _modelDetection.Object,
                _http.Object,
                breakerRegistry: _breakerRegistry.Object);

            // Assert - Source line 109: _providerHealth = providerHealth ?? throw new ArgumentNullException
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("providerHealth");
        }

        [Fact]
        public void Constructor_NullValidator_ThrowsArgumentNullException()
        {
            // Arrange & Act
            var act = () => new BrainarrOrchestrator(
                _logger,
                _providerFactory.Object,
                _libraryAnalyzer.Object,
                _cache.Object,
                _health.Object,
                validator: null,
                _modelDetection.Object,
                _http.Object,
                breakerRegistry: _breakerRegistry.Object);

            // Assert - Source line 110: _validator = validator ?? throw new ArgumentNullException
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("validator");
        }

        [Fact]
        public void Constructor_NullModelDetection_ThrowsArgumentNullException()
        {
            // Arrange & Act
            var act = () => new BrainarrOrchestrator(
                _logger,
                _providerFactory.Object,
                _libraryAnalyzer.Object,
                _cache.Object,
                _health.Object,
                _validator.Object,
                modelDetection: null,
                _http.Object,
                breakerRegistry: _breakerRegistry.Object);

            // Assert - Source line 111: _modelDetection = modelDetection ?? throw new ArgumentNullException
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("modelDetection");
        }

        [Fact]
        public void Constructor_NullHttpClient_ThrowsArgumentNullException()
        {
            // Arrange & Act
            var act = () => new BrainarrOrchestrator(
                _logger,
                _providerFactory.Object,
                _libraryAnalyzer.Object,
                _cache.Object,
                _health.Object,
                _validator.Object,
                _modelDetection.Object,
                httpClient: null,
                breakerRegistry: _breakerRegistry.Object);

            // Assert - Source line 113: _httpClient = httpClient ?? throw new ArgumentNullException
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClient");
        }

        // ====== GAP PLANNER ACTION TESTS ======
        // Source lines 445-554

        private BrainarrOrchestrator CreateOrchestratorWithLibrary()
        {
            _libraryAnalyzer.Setup(x => x.AnalyzeLibrary()).Returns(new LibraryProfile
            {
                TotalArtists = 30,
                TotalAlbums = 120,
                TopGenres = new Dictionary<string, int> { ["Rock"] = 40, ["Jazz"] = 8, ["Ambient"] = 4 },
                Metadata = new Dictionary<string, object>
                {
                    ["GenreDistribution"] = new Dictionary<string, double> { ["Rock"] = 68.0, ["Jazz"] = 6.0 },
                    ["PreferredEras"] = new List<string> { "Modern" },
                    ["NewReleaseRatio"] = 0.52
                }
            });

            _providerFactory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                .Returns(Mock.Of<IAIProvider>(p => p.ProviderName == "Test"));

            return new BrainarrOrchestrator(
                _logger,
                _providerFactory.Object,
                _libraryAnalyzer.Object,
                _cache.Object,
                _health.Object,
                _validator.Object,
                _modelDetection.Object,
                _http.Object,
                breakerRegistry: _breakerRegistry.Object,
                duplicateFilter: Mock.Of<IDuplicateFilterService>());
        }

        [Fact]
        public void HandleAction_PlanningSimulateGapPlan_ReturnsDryRunResult()
        {
            // Arrange - Source lines 445-462: SimulateGapPlan method
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();
            var query = new Dictionary<string, string> { ["budget"] = "10", ["max"] = "3" };

            // Act
            var result = orch.HandleAction("planning/simulategapplan", query, settings);

            // Assert
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"dryRun\":true", "because SimulateGapPlan sets dryRun=true (line 454)");
            json.Should().Contain("\"ok\":true", "because SimulateGapPlan returns ok=true");
            json.Should().Contain("\"items\"", "because SimulateGapPlan returns items array (line 455)");
        }

        [Fact]
        public void HandleAction_PlanningSimulateGapPlan_WithMinConfidence_FiltersItems()
        {
            // Arrange - Source lines 449, 450: minConfidence parameter
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();
            var query = new Dictionary<string, string> { ["minConfidence"] = "0.8", ["max"] = "5" };

            // Act
            var result = orch.HandleAction("planning/simulategapplan", query, settings);

            // Assert
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"ok\":true");
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.GetProperty("items");
            foreach (var item in items.EnumerateArray())
            {
                item.GetProperty("confidence").GetDouble().Should().BeGreaterOrEqualTo(0.8,
                    "because minConfidence=0.8 should filter lower confidence items (line 450)");
            }
        }

        [Fact]
        public void HandleAction_PlanningApplyGapPlan_WithoutIdempotencyKey_ReturnsError()
        {
            // Arrange - Source lines 466-471: idempotencyKey validation
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();
            var query = new Dictionary<string, string> { ["max"] = "3" };

            // Act
            var result = orch.HandleAction("planning/applygapplan", query, settings);

            // Assert - Source line 471: error message for missing idempotencyKey
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"ok\":false", "because missing idempotencyKey should return ok=false");
            json.Should().Contain("idempotencyKey is required", "because error message should explain the requirement (line 471)");
        }

        [Fact]
        public void HandleAction_PlanningApplyGapPlan_WithIdempotencyKey_ReturnsSuccess()
        {
            // Arrange - Source lines 464-513: ApplyGapPlan method
            var orch = CreateOrchestratorWithLibrary();
            SwapAuditService(orch);  // Fresh audit service for this test
            var settings = new BrainarrSettings();
            var query = new Dictionary<string, string>
            {
                ["idempotencyKey"] = "test-idempotency-key-001",
                ["actor"] = "unit-test",
                ["max"] = "2"
            };

            // Act
            var result = orch.HandleAction("planning/applygapplan", query, settings);

            // Assert
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"ok\":true", "because ApplyGapPlan should succeed with valid idempotencyKey");
            json.Should().Contain("\"id\":", "because audit ID should be returned (line 508)");
            json.Should().Contain("\"appliedCount\":", "because appliedCount should be in response (line 509)");
        }

        [Fact]
        public void HandleAction_PlanningApplyGapPlan_ReplayReturnsSameResult()
        {
            // Arrange - Source lines 474-477: replay detection via idempotency key
            var orch = CreateOrchestratorWithLibrary();
            SwapAuditService(orch);  // Fresh audit service for this test
            var settings = new BrainarrSettings();
            var idempotencyKey = "replay-test-key-002";
            var query = new Dictionary<string, string>
            {
                ["idempotencyKey"] = idempotencyKey,
                ["actor"] = "unit-test",
                ["max"] = "1"
            };

            // Act - First call
            var first = orch.HandleAction("planning/applygapplan", query, settings);
            var firstJson = JsonSerializer.Serialize(first);

            // Act - Second call with same key (replay)
            var second = orch.HandleAction("planning/applygapplan", query, settings);
            var secondJson = JsonSerializer.Serialize(second);

            // Assert - Source lines 475-476: replay detection
            firstJson.Should().NotContain("\"replay\":true", "because first call should not be a replay");
            secondJson.Should().Contain("\"replay\":true", "because second call with same idempotencyKey should be replay (line 476)");
        }

        [Fact]
        public void HandleAction_PlanningRollbackGapPlan_WithoutId_ReturnsErrorWhenNoRecentApplies()
        {
            // Arrange - Source lines 519-527: fallback to most recent apply
            var orch = CreateOrchestratorWithLibrary();
            SwapAuditService(orch);
            var settings = new BrainarrSettings();
            var query = new Dictionary<string, string>();

            // Act
            var result = orch.HandleAction("planning/rollbackgapplan", query, settings);

            // Assert - Source lines 523-524: error when no applies found
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"ok\":false", "because rollback without ID and no recent applies should fail");
            json.Should().Contain("No gap plan applications found", "because error message should explain (line 524)");
        }

        [Fact]
        public void HandleAction_PlanningRollbackGapPlan_WithInvalidId_ReturnsError()
        {
            // Arrange - Source lines 530-533: audit entry lookup
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();
            var query = new Dictionary<string, string> { ["id"] = "nonexistent-id-12345" };

            // Act
            var result = orch.HandleAction("planning/rollbackgapplan", query, settings);

            // Assert - Source lines 531-532: audit entry not found
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"ok\":false", "because invalid ID should return ok=false");
            json.Should().Contain("not found", "because error should indicate audit entry not found (line 532)");
        }

        [Fact]
        public void HandleAction_PlanningRollbackGapPlan_AfterApply_ReturnsSuccess()
        {
            // Arrange - Apply first, then rollback
            var orch = CreateOrchestratorWithLibrary();
            SwapAuditService(orch);  // Fresh audit service for this test
            var settings = new BrainarrSettings();

            // Apply
            var applyQuery = new Dictionary<string, string>
            {
                ["idempotencyKey"] = "rollback-test-key-003",
                ["actor"] = "unit-test",
                ["max"] = "2"
            };
            var applyResult = orch.HandleAction("planning/applygapplan", applyQuery, settings);
            var applyJson = JsonSerializer.Serialize(applyResult);
            using var applyDoc = JsonDocument.Parse(applyJson);
            var applyId = applyDoc.RootElement.GetProperty("id").GetString();

            // Act - Rollback
            var rollbackQuery = new Dictionary<string, string>
            {
                ["id"] = applyId,
                ["actor"] = "unit-test"
            };
            var result = orch.HandleAction("planning/rollbackgapplan", rollbackQuery, settings);

            // Assert - Source lines 535-553: successful rollback
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"ok\":true", "because rollback should succeed");
            json.Should().Contain("\"rollbackId\":", "because rollbackId should be in response (line 537)");
            json.Should().Contain($"\"rolledBackId\":\"{applyId}\"", "because rolledBackId should match apply ID (line 537)");
        }

        // ====== OBSERVABILITY TOGGLE TESTS ======
        // Source lines 399-401

        [Fact]
        public void HandleAction_ObservabilityGet_WhenDisabled_ReturnsDisabledTrue()
        {
            // Arrange - Source line 399: disabled check
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings { EnableObservabilityPreview = false };

            // Act
            var result = orch.HandleAction("observability/get", new Dictionary<string, string>(), settings);

            // Assert - Source line 399: new { disabled = true }
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"disabled\":true", "because observability is disabled when EnableObservabilityPreview=false (line 399)");
        }

        [Fact]
        public void HandleAction_ObservabilityGetOptions_WhenDisabled_ReturnsEmptyOptions()
        {
            // Arrange - Source line 400: disabled check
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings { EnableObservabilityPreview = false };

            // Act
            var result = orch.HandleAction("observability/getoptions", new Dictionary<string, string>(), settings);

            // Assert - Source line 400: new { options = Array.Empty<object>() }
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"options\":[]", "because options should be empty when disabled (line 400)");
        }

        [Fact]
        public void HandleAction_ObservabilityHtml_WhenDisabled_ReturnsDisabledMessage()
        {
            // Arrange - Source line 401: disabled check
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings { EnableObservabilityPreview = false };

            // Act
            var result = orch.HandleAction("observability/html", new Dictionary<string, string>(), settings);

            // Assert - Source line 401: "<html><body><p>Observability preview is disabled.</p></body></html>"
            result.Should().BeOfType<string>("because observability/html returns HTML string");
            var html = result as string;
            html.Should().Contain("Observability preview is disabled", "because HTML should show disabled message (line 401)");
        }

        [Fact]
        public void HandleAction_ObservabilityGet_WhenEnabled_ReturnsData()
        {
            // Arrange - Source line 399: enabled path
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings { EnableObservabilityPreview = true };

            // Act
            var result = orch.HandleAction("observability/get", new Dictionary<string, string>(), settings);

            // Assert - Should NOT contain disabled:true
            var json = JsonSerializer.Serialize(result);
            json.Should().NotContain("\"disabled\":true", "because observability is enabled");
        }

        // ====== QUERY PARSER EDGE CASE TESTS ======
        // Source lines 571-589: TryParseQueryInt and TryParseQueryDouble

        [Fact]
        public void HandleAction_PlanningSimulateGapPlan_WithNullQuery_HandlesGracefully()
        {
            // Arrange - Source lines 571-589: null query handling
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();

            // Act - null query should not throw
            var result = orch.HandleAction("planning/simulategapplan", query: null, settings);

            // Assert - Should return defaults (max=5, budget=null, minConfidence=0.0)
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"ok\":true", "because null query should use defaults (lines 448-450)");
        }

        [Fact]
        public void HandleAction_PlanningSimulateGapPlan_WithInvalidIntegers_UsesDefaults()
        {
            // Arrange - Source lines 571-579: TryParseQueryInt handles invalid values
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();
            var query = new Dictionary<string, string>
            {
                ["budget"] = "not-a-number",
                ["max"] = "also-invalid"
            };

            // Act
            var result = orch.HandleAction("planning/simulategapplan", query, settings);

            // Assert - Should use defaults instead of throwing
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"ok\":true", "because invalid integers should fall back to defaults");
        }

        [Fact]
        public void HandleAction_PlanningSimulateGapPlan_WithInvalidDouble_UsesDefault()
        {
            // Arrange - Source lines 581-589: TryParseQueryDouble handles invalid values
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();
            var query = new Dictionary<string, string>
            {
                ["minConfidence"] = "not-a-double"
            };

            // Act
            var result = orch.HandleAction("planning/simulategapplan", query, settings);

            // Assert - Should use default minConfidence=0.0
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"ok\":true", "because invalid double should fall back to default 0.0");
        }

        // ====== UNSUPPORTED ACTION TEST ======
        // Source line 433

        [Fact]
        public void HandleAction_UnknownAction_ReturnsErrorObject()
        {
            // Arrange - Source line 433: throw new NotSupportedException for unknown action
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();

            // Act
            var result = orch.HandleAction("unknown/invalid-action", new Dictionary<string, string>(), settings);

            // Assert - Exception is caught and returned as error object (lines 436-439)
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"error\":", "because unknown action should return error object (line 439)");
            json.Should().Contain("not supported", "because error message should indicate action not supported (line 433)");
        }

        // ====== STYLES GETOPTIONS TEST ======
        // Source line 403

        [Fact]
        public void HandleAction_StylesGetOptions_ReturnsOptions()
        {
            // Arrange - Source line 403: "styles/getoptions"
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();

            // Act
            var result = orch.HandleAction("styles/getoptions", new Dictionary<string, string>(), settings);

            // Assert
            result.Should().NotBeNull("because styles/getoptions should return a result");
        }

        // ====== METRICS ENDPOINTS TESTS ======
        // Source lines 395-397

        [Fact]
        public void HandleAction_MetricsGet_ReturnsSnapshot()
        {
            // Arrange - Source line 395: "metrics/get"
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();

            // Act
            var result = orch.HandleAction("metrics/get", new Dictionary<string, string>(), settings);

            // Assert
            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("review", "because metrics snapshot should include review metrics (line 395)");
        }

        [Fact]
        public void HandleAction_MetricsPrometheus_ReturnsText()
        {
            // Arrange - Source line 396: "metrics/prometheus"
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings();

            // Act
            var result = orch.HandleAction("metrics/prometheus", new Dictionary<string, string>(), settings);

            // Assert - Returns Prometheus-formatted text
            result.Should().NotBeNull("because metrics/prometheus should return Prometheus export");
        }

        // ====== FETCH RECOMMENDATIONS WITH CANCELLATION TOKEN TESTS ======
        // Source lines 205-228

        [Fact]
        public async Task FetchRecommendationsAsync_WithCancelledToken_ReturnsEmptyList()
        {
            // Arrange - Source lines 205-228: cancellation-aware overload
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, OllamaUrl = "http://localhost:11434" };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act - Source line 213: cancellationToken.ThrowIfCancellationRequested()
            var result = await orch.FetchRecommendationsAsync(settings, cts.Token);

            // Assert - Source lines 217-219: OperationCanceledException caught, returns empty list
            result.Should().NotBeNull("because the method should return a non-null list even when cancelled");
            result.Should().BeEmpty("because a cancelled operation should return an empty list (lines 218-219)");
        }

        [Fact]
        public async Task FetchRecommendationsAsync_WithValidToken_ReturnsResults()
        {
            // Arrange - Source lines 205-228: cancellation-aware overload
            var orch = CreateOrchestratorWithLibrary();
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = "llama3"
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));

            // Act
            var result = await orch.FetchRecommendationsAsync(settings, cts.Token);

            // Assert
            result.Should().NotBeNull("because the method should return a non-null list");
        }

        // ====== ATTACH ARTIST SEARCH SERVICE TESTS ======
        // Source lines 596-609

        [Fact]
        public void AttachArtistSearchService_WithSearchService_DoesNotThrow()
        {
            // Arrange - Source lines 596-609: AttachArtistSearchService method
            var orch = CreateOrchestratorWithLibrary();
            var mockSearch = new Mock<NzbDrone.Core.MetadataSource.ISearchForNewArtist>();

            // Act - Source lines 597-608: attaches search service to resolver
            var act = () => orch.AttachArtistSearchService(mockSearch.Object);

            // Assert - Should not throw
            act.Should().NotThrow("because AttachArtistSearchService should handle valid search service (lines 597-608)");
        }

        [Fact]
        public void AttachArtistSearchService_WithNull_DoesNotThrow()
        {
            // Arrange - Source lines 596-609: method handles null gracefully
            var orch = CreateOrchestratorWithLibrary();

            // Act - Internal implementation uses pattern matching
            var act = () => orch.AttachArtistSearchService(null!);

            // Assert - Should not throw (pattern matching on line 600 handles this)
            act.Should().NotThrow("because AttachArtistSearchService should handle null gracefully");
        }
    }
}
