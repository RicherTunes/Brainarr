using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorConnectionTests
    {
        private BrainarrOrchestrator Create(
            out Mock<IProviderFactory> factory,
            out Mock<IProviderHealthMonitor> health,
            out Mock<IAIProvider> provider)
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            factory = new Mock<IProviderFactory>();
            // Default: provider is available
            factory.Setup(x => x.IsProviderAvailable(It.IsAny<AIProvider>(), It.IsAny<BrainarrSettings>()))
                .Returns(true);
            var lib = new Mock<ILibraryAnalyzer>();

            lib.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))

                .Returns((List<ImportListItemInfo> items) => items);

            lib.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))

                .Returns((List<Recommendation> recs, bool _) => recs);
            var cache = new Mock<IRecommendationCache>();
            health = new Mock<IProviderHealthMonitor>();
            var validator = new Mock<IRecommendationValidator>();
            var models = new Mock<IModelDetectionService>();
            var http = new Mock<IHttpClient>();
            provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("ConnProv");
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);

            return new BrainarrOrchestrator(
                logger,
                factory.Object,
                lib.Object,
                cache.Object,
                health.Object,
                validator.Object,
                models.Object,
                http.Object,
                duplicationPrevention: null,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object);
        }

        [Fact]
        public async Task TestProviderConnectionAsync_Success_RecordsSuccess()
        {
            var orch = Create(out var factory, out var health, out var provider);
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, OpenAIApiKey = "k" };
            var ok = await orch.TestProviderConnectionAsync(settings);
            Assert.True(ok);
            health.Verify(h => h.RecordSuccess("ConnProv", It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task TestProviderConnectionAsync_Failure_RecordsFailure()
        {
            var orch = Create(out var factory, out var health, out var provider);
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(false);
            var settings = new BrainarrSettings { Provider = AIProvider.Anthropic, AnthropicApiKey = "k" };
            var ok = await orch.TestProviderConnectionAsync(settings);
            Assert.False(ok);
            health.Verify(h => h.RecordFailure("ConnProv", It.IsAny<string>()), Times.Once);
        }
    }
}
