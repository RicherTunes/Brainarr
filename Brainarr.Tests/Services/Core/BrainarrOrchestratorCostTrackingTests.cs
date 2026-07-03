using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using Xunit;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Cost;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Feature A2: wires the previously-dead <see cref="TokenCostEstimator"/> into the real
    /// recommendation path. Integration point is <c>RecommendationGenerator</c> (via
    /// <c>BrainarrOrchestrator</c>) — the only place with provider/model/prompt tokens +
    /// a Stopwatch measuring the real provider round trip. <c>IAIProvider.GetRecommendationsAsync</c>
    /// returns PARSED recommendations, never raw response text, so tracking must use the
    /// token-count overload, not a text-based one.
    /// </summary>
    [Collection("LimiterRegistryBounded")]
    [Trait("Category", "Unit")]
    [Trait("Component", "Orchestrator")]
    public class BrainarrOrchestratorCostTrackingTests : IDisposable
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        public BrainarrOrchestratorCostTrackingTests()
        {
            LimiterRegistry.ResetForTesting();
            NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.ResetForTesting();
            TokenCostEstimator.ResetUsageHistoryForTesting();
        }

        void IDisposable.Dispose()
        {
            LimiterRegistry.ResetForTesting();
            TokenCostEstimator.ResetUsageHistoryForTesting();
        }

        private static (BrainarrOrchestrator orchestrator, Mock<IAIProvider> provider) BuildOrchestrator(
            Logger logger,
            ITokenCostEstimator tokenCostEstimator,
            AIProvider providerType,
            Func<Mock<IAIProvider>, Mock<IAIProvider>> configureProvider = null)
        {
            var http = new Mock<IHttpClient>();
            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            var providerFactory = new Mock<IProviderFactory>();
            var cache = new Mock<IRecommendationCache>();
            var health = new Mock<IProviderHealthMonitor>();
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();

            artistService.Setup(a => a.GetAllArtists()).Returns(new List<Artist>());
            albumService.Setup(a => a.GetAllAlbums()).Returns(new List<Album>());

            var styleCatalog = new StyleCatalogService(logger, httpClient: null);
            var libraryAnalyzer = new LibraryAnalyzer(artistService.Object, albumService.Object, styleCatalog, logger);

            cache.Setup(c => c.TryGet(It.IsAny<string>(), out It.Ref<List<ImportListItemInfo>>.IsAny))
                 .Returns(false);
            health.Setup(h => h.IsHealthy(It.IsAny<string>())).Returns(true);
            validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                     .Returns((List<Recommendation> recs, bool strict) => new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                     {
                         ValidRecommendations = recs ?? new List<Recommendation>(),
                         FilteredRecommendations = new List<Recommendation>(),
                         TotalCount = recs?.Count ?? 0,
                         ValidCount = recs?.Count ?? 0,
                         FilteredCount = 0
                     });

            var provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns(providerType.ToString());
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);
            provider = configureProvider != null ? configureProvider(provider) : provider;

            providerFactory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), http.Object, logger))
                           .Returns(provider.Object);

            var duplicationPrevention = new DuplicationPreventionService(logger);
            duplicationPrevention.ClearHistory();
            var breakerRegistry = PassThroughBreakerRegistry.CreateMock();
            var duplicateFilter = new DuplicateFilterService(artistService.Object, albumService.Object, logger);

            var mbidResolver = new Mock<IMusicBrainzResolver>();
            mbidResolver.Setup(r => r.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                        .Returns((List<Recommendation> recs, CancellationToken _) => Task.FromResult(recs));
            var artistResolver = new Mock<IArtistMbidResolver>();
            artistResolver.Setup(r => r.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                          .Returns((List<Recommendation> recs, CancellationToken _) => Task.FromResult(recs));

            var orchestrator = new BrainarrOrchestrator(
                logger,
                providerFactory.Object,
                libraryAnalyzer,
                cache.Object,
                health.Object,
                validator.Object,
                modelDetection.Object,
                http.Object,
                duplicationPrevention,
                mbidResolver: mbidResolver.Object,
                artistResolver: artistResolver.Object,
                breakerRegistry: breakerRegistry.Object,
                duplicateFilter: duplicateFilter,
                tokenCostEstimator: tokenCostEstimator);

            return (orchestrator, provider);
        }

        [Fact]
        public async Task FetchRecommendationsAsync_OnSuccessfulProviderCall_TracksUsageForCorrectProviderAndModel()
        {
            var tokenCostEstimatorMock = new Mock<ITokenCostEstimator>();
            tokenCostEstimatorMock
                .Setup(e => e.TrackUsage(It.IsAny<AIProvider>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .Returns(new UsageReport());

            var (orchestrator, provider) = BuildOrchestrator(_logger, tokenCostEstimatorMock.Object, AIProvider.OpenAI, p =>
            {
                p.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new List<Recommendation>
                 {
                     new Recommendation { Artist = "Test Artist", Album = "Test Album", Confidence = 0.9 }
                 });
                return p;
            });

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "test-key",
                OpenAIModel = "gpt-4o-mini",
                // MaxRecommendations matches the single mocked recommendation so the run is
                // satisfied on the first call and top-up doesn't fire (top-up isn't gated by
                // a flag — it kicks in whenever the aggregate is short of target, so a
                // deliberate mismatch here would make this test exercise top-up too, which
                // is covered separately below).
                MaxRecommendations = 1,
                RequireMbids = false // mock recommendation has no MBIDs; keep enrichment permissive
            };

            var result = await orchestrator.FetchRecommendationsAsync(settings);

            Assert.NotNull(result);
            tokenCostEstimatorMock.Verify(
                e => e.TrackUsage(
                    AIProvider.OpenAI,
                    "gpt-4o-mini",
                    It.Is<int>(t => t > 0),
                    It.Is<int>(t => t >= 0),
                    It.IsAny<TimeSpan>()),
                Times.Once,
                "TrackUsage must be attributed to the settings' actual provider/model, once per real provider round trip");
        }

        [Fact]
        public async Task FetchRecommendationsAsync_WithTopUpEnabled_TracksUsageExactlyOncePerRealProviderCall()
        {
            // Regression guard against double-counting: TrackUsage must fire exactly once
            // per real HTTP round trip to the provider, even across top-up iterations —
            // never once per logical "run" (undercount) and never more than once per call
            // (overcount, e.g. once for the plan + once for the actual call).
            var tokenCostEstimatorMock = new Mock<ITokenCostEstimator>();
            tokenCostEstimatorMock
                .Setup(e => e.TrackUsage(It.IsAny<AIProvider>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .Returns(new UsageReport());

            var (orchestrator, provider) = BuildOrchestrator(_logger, tokenCostEstimatorMock.Object, AIProvider.LMStudio, p =>
            {
                p.SetupSequence(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Recommendation>
                    {
                        new Recommendation { Artist = "Arctic Monkeys", Album = "The Car", Confidence = 0.9 }
                    })
                    .ReturnsAsync(new List<Recommendation>
                    {
                        new Recommendation { Artist = "Phoebe Bridgers", Album = "Punisher", Confidence = 0.9 }
                    })
                    .ReturnsAsync(new List<Recommendation>())
                    .ReturnsAsync(new List<Recommendation>())
                    .ReturnsAsync(new List<Recommendation>())
                    .ReturnsAsync(new List<Recommendation>())
                    .ReturnsAsync(new List<Recommendation>());
                return p;
            });

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "http://localhost:1234",
                LMStudioModel = "test-model",
                MaxRecommendations = 2,
                EnableIterativeRefinement = true,
                RequireMbids = false
            };

            await orchestrator.FetchRecommendationsAsync(settings);

            var realProviderCalls = provider.Invocations.Count(i => i.Method.Name == nameof(IAIProvider.GetRecommendationsAsync));
            realProviderCalls.Should().BeGreaterThanOrEqualTo(2);

            tokenCostEstimatorMock.Verify(
                e => e.TrackUsage(AIProvider.LMStudio, "test-model", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeSpan>()),
                Times.Exactly(realProviderCalls));
        }

        [Fact]
        public async Task FetchRecommendationsAsync_WithLocalProvider_RealEstimator_ReportsKnownZeroCost()
        {
            var estimator = new TokenCostEstimator(_logger);

            var (orchestrator, _) = BuildOrchestrator(_logger, estimator, AIProvider.Ollama, p =>
            {
                p.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new List<Recommendation>
                 {
                     new Recommendation { Artist = "Test Artist", Album = "Test Album", Confidence = 0.9 }
                 });
                return p;
            });

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = "llama3",
                // See the OpenAI single-call test above: matches the single mocked
                // recommendation so top-up doesn't fire and this stays a clean
                // one-real-call assertion.
                MaxRecommendations = 1,
                RequireMbids = false
            };

            await orchestrator.FetchRecommendationsAsync(settings);

            var stats = estimator.GetUsageStatistics(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
            stats.TotalRequests.Should().Be(1);
            stats.TotalCost.Should().Be(0m);
            stats.UnpricedRequestCount.Should().Be(0, "a local provider is a known $0, not an unpriced/unknown model");
        }

        [Fact]
        public void HandleAction_CostGet_ReturnsUsageStatisticsFromEstimator()
        {
            var expectedStats = new UsageStatistics { TotalRequests = 3, TotalCost = 1.23m };
            var tokenCostEstimatorMock = new Mock<ITokenCostEstimator>();
            tokenCostEstimatorMock
                .Setup(e => e.GetUsageStatistics(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .Returns(expectedStats);

            var (orchestrator, _) = BuildOrchestrator(_logger, tokenCostEstimatorMock.Object, AIProvider.OpenAI);

            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, OpenAIApiKey = "k", OpenAIModel = "gpt-4o-mini" };
            var result = orchestrator.HandleAction("cost/get", new Dictionary<string, string>(), settings);

            Assert.Same(expectedStats, result);
        }

        [Fact]
        public void HandleAction_CostGet_HonorsDaysQueryParam()
        {
            var tokenCostEstimatorMock = new Mock<ITokenCostEstimator>();
            DateTime capturedStart = default, capturedEnd = default;
            tokenCostEstimatorMock
                .Setup(e => e.GetUsageStatistics(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .Callback<DateTime, DateTime>((start, end) => { capturedStart = start; capturedEnd = end; })
                .Returns(new UsageStatistics());

            var (orchestrator, _) = BuildOrchestrator(_logger, tokenCostEstimatorMock.Object, AIProvider.OpenAI);
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, OpenAIApiKey = "k", OpenAIModel = "gpt-4o-mini" };

            orchestrator.HandleAction("cost/get", new Dictionary<string, string> { ["days"] = "7" }, settings);

            (capturedEnd - capturedStart).TotalDays.Should().BeApproximately(7, 0.01);
        }

        #region ResolveCostLookbackDays (pure function hardening)

        [Theory]
        [InlineData(null, 30)]
        [InlineData("", 30)]
        [InlineData("not-a-number", 30)]
        [InlineData("7", 7)]
        [InlineData("1", 1)]
        [InlineData("365", 365)]
        [InlineData("9999", 365)] // clamps to max
        [InlineData("0", 1)]      // clamps to min
        [InlineData("-5", 1)]     // clamps to min
        public void ResolveCostLookbackDays_ParsesAndClamps(string daysValue, int expected)
        {
            var query = daysValue == null ? null : new Dictionary<string, string> { ["days"] = daysValue };

            var result = BrainarrOrchestrator.ResolveCostLookbackDays(query);

            result.Should().Be(expected);
        }

        [Fact]
        public void ResolveCostLookbackDays_NullQuery_ReturnsDefault()
        {
            BrainarrOrchestrator.ResolveCostLookbackDays(null).Should().Be(30);
        }

        [Fact]
        public void ResolveCostLookbackDays_QueryWithoutDaysKey_ReturnsDefault()
        {
            BrainarrOrchestrator.ResolveCostLookbackDays(new Dictionary<string, string> { ["other"] = "x" }).Should().Be(30);
        }

        #endregion
    }
}
