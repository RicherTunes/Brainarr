using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.Parser.Model;
using Brainarr.Tests.Helpers;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorEndToEndLiteTests
    {
        [Fact]
        public async Task FetchRecommendationsAsync_Invokes_GenerateRecommendations_And_Returns_Items()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();

            var provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("Fake");
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);

            var providerFactory = new Mock<IProviderFactory>();
            providerFactory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                           .Returns(provider.Object);
            providerFactory.Setup(f => f.IsProviderAvailable(It.IsAny<AIProvider>(), It.IsAny<BrainarrSettings>())).Returns(true);

            var lib = new Mock<ILibraryAnalyzer>();
            lib.Setup(l => l.AnalyzeLibrary()).Returns(new LibraryProfile());
            lib.Setup(l => l.BuildPrompt(It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<DiscoveryMode>()))
               .Returns(string.Empty);
            lib.Setup(l => l.BuildPrompt(It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<DiscoveryMode>(), It.IsAny<bool>()))
               .Returns(string.Empty);

            var cache = new Mock<IRecommendationCache>();
            List<ImportListItemInfo> notUsed;
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
            cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<TimeSpan?>())).Verifiable();

            var health = new Mock<IProviderHealthMonitor>();
            health.Setup(h => h.IsHealthy(It.IsAny<string>())).Returns(true);
            health.Setup(h => h.GetMetrics(It.IsAny<string>())).Returns(new ProviderMetrics());

            var validator = new Mock<IRecommendationValidator>();
            validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                     .Returns<List<Recommendation>, bool>((lst, _) => new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                     {
                         ValidRecommendations = lst,
                         FilteredRecommendations = new List<Recommendation>(),
                         TotalCount = lst.Count,
                         ValidCount = lst.Count,
                         FilteredCount = 0
                     });

            var modelDetection = new Mock<IModelDetectionService>();
            var http = new Mock<IHttpClient>();

            var promptBuilder = new Mock<ILibraryAwarePromptBuilder>();
            promptBuilder.Setup(p => p.BuildLibraryAwarePromptWithMetrics(
                    It.IsAny<LibraryProfile>(), It.IsAny<List<NzbDrone.Core.Music.Artist>>(), It.IsAny<List<NzbDrone.Core.Music.Album>>(), It.IsAny<BrainarrSettings>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                         .Returns(new LibraryPromptResult { Prompt = "p", SampledAlbums = 0, SampledArtists = 0, EstimatedTokens = 10 });

            var providerInvoker = new Mock<IProviderInvoker>();
            providerInvoker.Setup(i => i.InvokeAsync(It.IsAny<IAIProvider>(), It.IsAny<string>(), It.IsAny<Logger>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                           .ReturnsAsync(new List<Recommendation>
                           {
                               new Recommendation { Artist = "A", Album = "B" },
                               new Recommendation { Artist = "C", Album = "D" }
                           });

            var coordinator = new Mock<IRecommendationCoordinator>();
            coordinator.Setup(c => c.RunAsync(
                        It.IsAny<BrainarrSettings>(),
                        It.IsAny<Func<LibraryProfile, CancellationToken, Task<List<Recommendation>>>>(),
                        It.IsAny<ReviewQueueService>(),
                        It.IsAny<IAIProvider>(),
                        It.IsAny<ILibraryAwarePromptBuilder>(),
                        It.IsAny<CancellationToken>()))
                .Returns<BrainarrSettings, Func<LibraryProfile, CancellationToken, Task<List<Recommendation>>>, ReviewQueueService, IAIProvider, ILibraryAwarePromptBuilder, CancellationToken>(
                    async (s, fetch, q, prov, pb, ct) =>
                    {
                        var recs = await fetch(new LibraryProfile(), CancellationToken.None);
                        return recs.Select(r => new ImportListItemInfo { Artist = r.Artist, Album = r.Album }).ToList();
                    });

            // Duplication prevention pass-through stub
            var duplication = new PassThroughDuplicationPrevention(logger);

            var orch = new BrainarrOrchestrator(
                logger,
                providerFactory.Object,
                lib.Object,
                cache.Object,
                health.Object,
                validator.Object,
                modelDetection.Object,
                http.Object,
                duplication,
                null,
                null,
                null,
                null,
                null,
                providerInvoker.Object,
                null,
                null,
                null,
                coordinator.Object,
                promptBuilder.Object,
                styleCatalog: null,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object,
                duplicateFilter: Mock.Of<IDuplicateFilterService>());

            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, ModelSelection = "m", MaxRecommendations = 2 };
            var items = await orch.FetchRecommendationsAsync(settings);
            Assert.Equal(2, items.Count);
        }

        private class PassThroughDuplicationPrevention : IDuplicationPrevention
        {
            private readonly Logger _logger;
            public PassThroughDuplicationPrevention(Logger logger) { _logger = logger; }
            public Task<T> PreventConcurrentFetch<T>(string operationKey, Func<Task<T>> fetchOperation) => fetchOperation();
            public List<ImportListItemInfo> DeduplicateRecommendations(List<ImportListItemInfo> recommendations) => recommendations;
            public List<ImportListItemInfo> FilterPreviouslyRecommended(List<ImportListItemInfo> recommendations, ISet<string>? sessionAllowList = null) => recommendations;
            public void ClearHistory() { }
        }
    }
}
