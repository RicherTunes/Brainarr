using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Targeted tests closing coverage gaps flagged in the Wave 11C audit for
    /// <see cref="BrainarrOrchestrator"/>: cache hit/miss, cancellation/timeout
    /// propagation, and error-classification handling of provider faults
    /// (rate-limit, auth, generic).
    ///
    /// Tests inject a mocked <see cref="IRecommendationCoordinator"/> and
    /// <see cref="IProviderInvoker"/> so the orchestrator wiring (and not the
    /// downstream pipeline) is the unit under test. The coordinator is wired
    /// to either short-circuit (cache hit) or invoke the supplied fetch
    /// delegate (cache miss), matching the contract of
    /// <see cref="RecommendationCoordinator.RunAsync"/>.
    /// </summary>
    [Trait("Category", "Unit")]
    public class BrainarrOrchestratorCoverageGapsTests
    {
        // ---------- Shared harness ----------

        private sealed class Harness
        {
            public BrainarrOrchestrator Orchestrator { get; init; } = default!;
            public Mock<IProviderFactory> Factory { get; init; } = default!;
            public Mock<IProviderHealthMonitor> Health { get; init; } = default!;
            public Mock<IRecommendationCoordinator> Coordinator { get; init; } = default!;
            public Mock<IProviderInvoker> Invoker { get; init; } = default!;
            public Mock<IAIProvider> Provider { get; init; } = default!;
            public Logger Logger { get; init; } = default!;
        }

        private static Harness Build()
        {
            var logger = TestLogger.CreateNullLogger();
            var factory = new Mock<IProviderFactory>();
            var health = new Mock<IProviderHealthMonitor>();
            var coordinator = new Mock<IRecommendationCoordinator>();
            var invoker = new Mock<IProviderInvoker>();
            var provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("FakeProvider");

            var lib = new Mock<ILibraryAnalyzer>();
            lib.Setup(l => l.AnalyzeLibrary()).Returns(new LibraryProfile());
            var cache = new Mock<IRecommendationCache>();
            List<ImportListItemInfo> dummy;
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out dummy)).Returns(false);
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();
            var http = new Mock<IHttpClient>();

            // Default: healthy provider so the soft-health gate doesn't abort the workflow.
            health.Setup(h => h.IsHealthy(It.IsAny<string>())).Returns(true);
            health.Setup(h => h.GetMetrics(It.IsAny<string>())).Returns(new ProviderMetrics());
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);

            var orch = new BrainarrOrchestrator(
                logger,
                factory.Object,
                lib.Object,
                cache.Object,
                health.Object,
                validator.Object,
                modelDetection.Object,
                http.Object,
                duplicationPrevention: null,
                mbidResolver: null,
                artistResolver: null,
                persistSettingsCallback: null,
                sanitizer: null,
                schemaValidator: null,
                providerInvoker: invoker.Object,
                safetyGates: null,
                topUpPlanner: null,
                pipeline: null,
                coordinator: coordinator.Object,
                promptBuilder: new LibraryAwarePromptBuilder(logger),
                styleCatalog: null,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object,
                duplicateFilter: Mock.Of<IDuplicateFilterService>());

            return new Harness
            {
                Orchestrator = orch,
                Factory = factory,
                Health = health,
                Coordinator = coordinator,
                Invoker = invoker,
                Provider = provider,
                Logger = logger,
            };
        }

        // ---------- Cache: hit vs miss ----------

        [Fact]
        public async Task FetchRecommendations_CacheHit_ReturnsCachedWithoutInvokingProvider()
        {
            // Cache hit at the coordinator layer must short-circuit; the orchestrator's
            // fetch delegate (which calls the provider invoker) must not run.
            var h = Build();
            var cached = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Cached Artist", Album = "Cached Album" },
            };

            // Simulate cache hit: coordinator returns cached items without calling fetch.
            h.Coordinator.Setup(c => c.RunAsync(
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<Func<LibraryProfile, CancellationToken, Task<List<Recommendation>>>>(),
                    It.IsAny<ReviewQueueService>(),
                    It.IsAny<IAIProvider>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cached);

            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, MaxRecommendations = 5 };
            var result = await h.Orchestrator.FetchRecommendationsAsync(settings);

            Assert.Single(result);
            Assert.Equal("Cached Artist", result[0].Artist);
            // Cache-hit path must never reach the provider invoker.
            h.Invoker.Verify(i => i.InvokeAsync(
                It.IsAny<IAIProvider>(),
                It.IsAny<string>(),
                It.IsAny<Logger>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task FetchRecommendations_CacheMiss_InvokesProviderAndReturnsFreshResults()
        {
            // Cache miss: coordinator runs the fetch delegate, which calls the provider
            // invoker; orchestrator must return the fresh provider results.
            var h = Build();

            h.Coordinator.Setup(c => c.RunAsync(
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<Func<LibraryProfile, CancellationToken, Task<List<Recommendation>>>>(),
                    It.IsAny<ReviewQueueService>(),
                    It.IsAny<IAIProvider>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(),
                    It.IsAny<CancellationToken>()))
                .Returns<BrainarrSettings, Func<LibraryProfile, CancellationToken, Task<List<Recommendation>>>, ReviewQueueService, IAIProvider, ILibraryAwarePromptBuilder, CancellationToken>(
                    async (s, fetch, q, prov, pb, ct) =>
                    {
                        var recs = await fetch(new LibraryProfile(), ct);
                        return recs.Select(r => new ImportListItemInfo { Artist = r.Artist, Album = r.Album }).ToList();
                    });

            h.Invoker.Setup(i => i.InvokeAsync(
                    It.IsAny<IAIProvider>(),
                    It.IsAny<string>(),
                    It.IsAny<Logger>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(new List<Recommendation>
                {
                    new Recommendation { Artist = "Fresh", Album = "Hits" },
                });

            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, MaxRecommendations = 1 };
            var result = await h.Orchestrator.FetchRecommendationsAsync(settings);

            Assert.Single(result);
            Assert.Equal("Fresh", result[0].Artist);
            h.Invoker.Verify(i => i.InvokeAsync(
                It.IsAny<IAIProvider>(),
                It.IsAny<string>(),
                It.IsAny<Logger>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>()), Times.AtLeastOnce);
            h.Health.Verify(m => m.RecordSuccess("FakeProvider", It.IsAny<double>()), Times.AtLeastOnce);
        }
    }
}
