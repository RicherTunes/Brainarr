using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

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
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
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
        public async Task TestProviderConnectionAsync_Success_RecordsSuccess()
        {
            var orch = Create(out var factory, out var health, out var provider, out var logger);
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);
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
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(false);
            factory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                   .Returns(provider.Object);

            var ok = await orch.TestProviderConnectionAsync(new BrainarrSettings { Provider = AIProvider.OpenAI });
            Assert.False(ok);
            health.Verify(h => h.RecordFailure("Fake", It.IsAny<string>()), Times.Once);
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
