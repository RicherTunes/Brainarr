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
using Xunit;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorMoreTests
    {
        private static BrainarrOrchestrator CreateWithInvoker(
            out Mock<IProviderFactory> factory,
            out Mock<IProviderHealthMonitor> health,
            out Mock<IRecommendationCoordinator> coordinator,
            out Mock<IProviderInvoker> invoker,
            out Mock<IAIProvider> provider,
            out Logger logger)
        {
            logger = Helpers.TestLogger.CreateNullLogger();
            factory = new Mock<IProviderFactory>();
            health = new Mock<IProviderHealthMonitor>();
            coordinator = new Mock<IRecommendationCoordinator>();
            invoker = new Mock<IProviderInvoker>();
            provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("Fake");

            var lib = new Mock<ILibraryAnalyzer>();
            lib.Setup(l => l.AnalyzeLibrary()).Returns(new LibraryProfile());
            lib.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>())).Returns<List<ImportListItemInfo>>(x => x);
            var cache = new Mock<IRecommendationCache>();
            List<ImportListItemInfo> dummy;
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out dummy)).Returns(false);
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();
            var http = new Mock<IHttpClient>();

            return new BrainarrOrchestrator(
                logger,
                factory.Object,
                lib.Object,
                cache.Object,
                health.Object,
                validator.Object,
                modelDetection.Object,
                http.Object,
                null,
                null,
                null,
                null,
                null,
                null,
                invoker.Object,
                null,
                null,
                null,
                coordinator.Object,
                new LibraryAwarePromptBuilder(logger));
        }
        private static BrainarrOrchestrator CreateBase(
            out Mock<IProviderFactory> factory,
            out Mock<IProviderHealthMonitor> health,
            out Mock<IRecommendationCoordinator> coordinator,
            out Mock<IAIProvider> provider,
            out Logger logger,
            Action persist = null)
        {
            logger = Helpers.TestLogger.CreateNullLogger();
            factory = new Mock<IProviderFactory>();
            health = new Mock<IProviderHealthMonitor>();
            coordinator = new Mock<IRecommendationCoordinator>();
            provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("Fake");

            var lib = new Mock<ILibraryAnalyzer>();
            lib.Setup(l => l.AnalyzeLibrary()).Returns(new LibraryProfile());
            lib.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>())).Returns<List<ImportListItemInfo>>(x => x);
            var cache = new Mock<IRecommendationCache>();
            List<ImportListItemInfo> dummy;
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out dummy)).Returns(false);
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();
            var http = new Mock<IHttpClient>();

            return new BrainarrOrchestrator(
                logger,
                factory.Object,
                lib.Object,
                cache.Object,
                health.Object,
                validator.Object,
                modelDetection.Object,
                http.Object,
                null,
                null,
                null,
                persist,
                null,
                null,
                null,
                null,
                null,
                null,
                coordinator.Object,
                new LibraryAwarePromptBuilder(logger));
        }

        [Fact]
        public async Task FetchRecommendations_DebugLogging_LocalProvider_ArtistMode_Works()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var factory = new Mock<IProviderFactory>();
            var health = new Mock<IProviderHealthMonitor>();
            var coordinator = new Mock<IRecommendationCoordinator>();
            var invoker = new Mock<IProviderInvoker>();
            var provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("Local");

            var lib = new Mock<ILibraryAnalyzer>();
            lib.Setup(l => l.AnalyzeLibrary()).Returns(new LibraryProfile());
            lib.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
               .Returns<List<ImportListItemInfo>>(x => x);
            var cache = new Mock<IRecommendationCache>();
            List<ImportListItemInfo> outList;
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out outList)).Returns(false);
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();
            var http = new Mock<IHttpClient>();

            var promptBuilder = new LibraryAwarePromptBuilder(logger);

            var orch = new BrainarrOrchestrator(
                logger, factory.Object, lib.Object, cache.Object, health.Object, validator.Object,
                modelDetection.Object, http.Object,
                null, null, null, null,
                null, null, invoker.Object, null, null, null,
                coordinator.Object, promptBuilder);

            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);

            // Coordinator simply maps the delegate results
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
                        var list = new List<ImportListItemInfo>();
                        foreach (var r in recs) list.Add(new ImportListItemInfo { Artist = r.Artist, Album = r.Album });
                        return list;
                    });

            invoker.Setup(i => i.InvokeAsync(It.IsAny<IAIProvider>(), It.IsAny<string>(), It.IsAny<Logger>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                   .ReturnsAsync(new List<Recommendation> { new Recommendation { Artist = "X", Album = "Y" } });

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama, // local provider path
                RecommendationMode = RecommendationMode.Artists,
                EnableDebugLogging = true,
                MaxRecommendations = 1
            };

            var items = await orch.FetchRecommendationsAsync(settings);
            Assert.Single(items);
        }

        [Fact]
        public async Task FetchRecommendations_UnhealthyProvider_ReturnsEmpty()
        {
            var orch = CreateBase(out var factory, out var health, out var coordinator, out var provider, out var logger);
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);
            health.Setup(h => h.IsHealthy("Fake")).Returns(false);

            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var items = await orch.FetchRecommendationsAsync(settings);

            Assert.Empty(items);
            coordinator.Verify(c => c.RunAsync(It.IsAny<BrainarrSettings>(),
                It.IsAny<Func<LibraryProfile, CancellationToken, Task<List<Recommendation>>>>(),
                It.IsAny<ReviewQueueService>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task FetchRecommendations_ApprovalsApplied_AddsItems_AndPersists()
        {
            bool persisted = false;
            var orch = CreateBase(out var factory, out var health, out var coordinator, out var provider, out var logger, persist: () => persisted = true);
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);
            health.Setup(h => h.IsHealthy("Fake")).Returns(true);
            coordinator.Setup(c => c.RunAsync(
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<Func<LibraryProfile, CancellationToken, Task<List<Recommendation>>>>(),
                    It.IsAny<ReviewQueueService>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ImportListItemInfo>());

            // Pre-populate review queue with an item matching key "A|B"
            var qf = typeof(BrainarrOrchestrator).GetField("_reviewQueue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var queue = new ReviewQueueService(logger);
            qf!.SetValue(orch, queue);
            queue.Enqueue(new[] { new Recommendation { Artist = "A", Album = "B" } });

            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, ReviewApproveKeys = new[] { "A|B" } };
            var items = await orch.FetchRecommendationsAsync(settings);

            Assert.Single(items);
            Assert.True(persisted);
            Assert.Empty(settings.ReviewApproveKeys);
        }

        [Fact]
        public void InitializeProvider_Throws_WhenFactoryReturnsNull()
        {
            var orch = CreateBase(out var factory, out var health, out var coordinator, out var provider, out var logger);
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns((IAIProvider)null);
            Assert.Throws<InvalidOperationException>(() => orch.InitializeProvider(new BrainarrSettings { Provider = AIProvider.Ollama }));
        }

        [Fact]
        public void UpdateProviderConfiguration_CallsInitialize()
        {
            var orch = CreateBase(out var factory, out var health, out var coordinator, out var provider, out var logger);
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);
            orch.UpdateProviderConfiguration(new BrainarrSettings { Provider = AIProvider.Ollama });
            factory.Verify(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()), Times.Once);
        }

        [Fact]
        public async Task TestProviderConnectionAsync_Exception_RecordsFailure()
        {
            var orch = CreateBase(out var factory, out var health, out var coordinator, out var provider, out var logger);
            provider.Setup(p => p.TestConnectionAsync()).ThrowsAsync(new Exception("boom"));
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);
            var ok = await orch.TestProviderConnectionAsync(new BrainarrSettings { Provider = AIProvider.OpenAI });
            Assert.False(ok);
            health.Verify(h => h.RecordFailure("Fake", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void GetProviderStatus_Unhealthy()
        {
            var orch = CreateBase(out var factory, out var health, out var coordinator, out var provider, out var logger);
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);
            health.Setup(h => h.IsHealthy("Fake")).Returns(false);

            orch.InitializeProvider(new BrainarrSettings { Provider = AIProvider.OpenAI });
            var status = orch.GetProviderStatus();
            Assert.Contains("Unhealthy", status);
        }

        [Fact]
        public async Task GenerateRecommendations_Success_RecordsHealthSuccess()
        {
            var orch = CreateWithInvoker(out var factory, out var health, out var coord, out var invoker, out var provider, out var logger);
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);
            // Coordinator calls provided fetch delegate and maps to import items
            coord.Setup(c => c.RunAsync(
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
                        var list = new List<ImportListItemInfo>();
                        foreach (var r in recs) list.Add(new ImportListItemInfo { Artist = r.Artist, Album = r.Album });
                        return list;
                    });

            invoker.Setup(i => i.InvokeAsync(It.IsAny<IAIProvider>(), It.IsAny<string>(), It.IsAny<Logger>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                   .ReturnsAsync(new List<Recommendation> { new Recommendation { Artist = "A", Album = "B" } });

            var items = await orch.FetchRecommendationsAsync(new BrainarrSettings { Provider = AIProvider.OpenAI });
            Assert.Single(items);
            health.Verify(h => h.RecordSuccess("Fake", It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task GenerateRecommendations_Empty_RecordsHealthFailure()
        {
            var orch = CreateWithInvoker(out var factory, out var health, out var coord, out var invoker, out var provider, out var logger);
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);
            coord.Setup(c => c.RunAsync(
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
                        var list = new List<ImportListItemInfo>();
                        foreach (var r in recs) list.Add(new ImportListItemInfo { Artist = r.Artist, Album = r.Album });
                        return list;
                    });

            invoker.Setup(i => i.InvokeAsync(It.IsAny<IAIProvider>(), It.IsAny<string>(), It.IsAny<Logger>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                   .ReturnsAsync(new List<Recommendation>());

            var items = await orch.FetchRecommendationsAsync(new BrainarrSettings { Provider = AIProvider.OpenAI });
            Assert.Empty(items); // downstream pipeline returns empty
            health.Verify(h => h.RecordFailure("Fake", It.IsAny<string>()), Times.Once);
        }
    }
}
