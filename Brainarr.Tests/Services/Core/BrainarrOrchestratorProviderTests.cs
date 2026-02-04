using System;
using System.Collections.Generic;
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
using NzbDrone.Core.Parser.Model;
using Xunit;
using Lidarr.Plugin.Common.Abstractions.Llm;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorProviderTests
    {
        private static BrainarrOrchestrator Create(
            out Mock<IProviderFactory> factory,
            out Mock<IProviderHealthMonitor> health,
            out Mock<IAIProvider> provider,
            out Logger logger)
        {
            logger = Helpers.TestLogger.CreateNullLogger();
            factory = new Mock<IProviderFactory>();
            health = new Mock<IProviderHealthMonitor>();
            provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("Fake");

            var lib = new Mock<ILibraryAnalyzer>();


            lib.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))


                .Returns((List<ImportListItemInfo> items) => items);


            lib.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))


                .Returns((List<Recommendation> recs, bool _) => recs);
            var cache = new Mock<IRecommendationCache>();
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();
            var http = new Mock<IHttpClient>();

            // Provide a minimal working orchestrator; other optional dependencies can be null
            return new BrainarrOrchestrator(
                logger,
                factory.Object,
                lib.Object,
                cache.Object,
                health.Object,
                validator.Object,
                modelDetection.Object,
                http.Object,
                duplicationPrevention: null,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object);
        }

        [Fact]
        public void InitializeProvider_Idempotent_For_Same_Provider()
        {
            var orch = Create(out var factory, out var health, out var provider, out var logger);
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);

            orch.InitializeProvider(settings);
            // Call again with same provider type
            orch.InitializeProvider(settings);

            factory.Verify(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()), Times.Once);
        }

        [Fact]
        public void InitializeProvider_Reinitializes_On_ProviderChange()
        {
            var orch = Create(out var factory, out var health, out var provider, out var logger);
            var settings1 = new BrainarrSettings { Provider = AIProvider.Ollama };
            var settings2 = new BrainarrSettings { Provider = AIProvider.OpenRouter };
            factory.SetupSequence(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object)
                   .Returns(provider.Object);

            orch.InitializeProvider(settings1);
            orch.InitializeProvider(settings2);

            factory.Verify(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()), Times.Exactly(2));
        }

        [Fact]
        public async Task TestProviderConnectionAsync_Success_RecordsSuccess()
        {
            var orch = Create(out var factory, out var health, out var provider, out var logger);
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(ProviderHealthResult.Healthy(responseTime: TimeSpan.FromSeconds(1)));
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);

            var ok = await orch.TestProviderConnectionAsync(new BrainarrSettings { Provider = AIProvider.OpenAI });
            Assert.True(ok);
            health.Verify(h => h.RecordSuccess("Fake", It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task TestProviderConnectionAsync_Failure_RecordsFailure()
        {
            var orch = Create(out var factory, out var health, out var provider, out var logger);
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(ProviderHealthResult.Unhealthy("Connection failed"));
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);

            var ok = await orch.TestProviderConnectionAsync(new BrainarrSettings { Provider = AIProvider.OpenAI });
            Assert.False(ok);
            health.Verify(h => h.RecordFailure("Fake", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void GetProviderStatus_Healthy()
        {
            var orch = Create(out var factory, out var health, out var provider, out var logger);
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);
            health.Setup(h => h.IsHealthy("Fake")).Returns(true);

            orch.InitializeProvider(new BrainarrSettings { Provider = AIProvider.OpenAI });
            var status = orch.GetProviderStatus();
            Assert.Contains("Healthy", status);
        }

        [Fact]
        public void GetProviderStatus_Before_And_After_Init()
        {
            var orch = Create(out var factory, out var health, out var provider, out var logger);
            Assert.Equal("Not Initialized", orch.GetProviderStatus());

            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);
            health.Setup(h => h.IsHealthy("Fake")).Returns(true);

            orch.InitializeProvider(new BrainarrSettings { Provider = AIProvider.OpenAI });
            var status = orch.GetProviderStatus();
            Assert.Contains("Fake: Healthy", status);
        }

        [Fact]
        public void IsProviderHealthy_ReturnsFalse_WhenNotInitialized()
        {
            var orch = Create(out var factory, out var health, out var provider, out var logger);
            Assert.False(orch.IsProviderHealthy());
        }
    }
}
