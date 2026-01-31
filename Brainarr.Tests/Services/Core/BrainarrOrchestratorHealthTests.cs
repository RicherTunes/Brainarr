using System;
using Brainarr.Tests.Helpers;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorHealthTests
    {
        private readonly Mock<IProviderFactory> _providerFactory = new();
        private readonly Mock<ILibraryAnalyzer> _libraryAnalyzer = new();
        private readonly Mock<IRecommendationCache> _cache = new();
        private readonly Mock<IProviderHealthMonitor> _health = new();
        private readonly Mock<IRecommendationValidator> _validator = new();
        private readonly Mock<IModelDetectionService> _models = new();
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        private BrainarrOrchestrator CreateOrchestrator()
        {
            // Default: provider is available
            _providerFactory.Setup(x => x.IsProviderAvailable(It.IsAny<AIProvider>(), It.IsAny<BrainarrSettings>()))
                .Returns(true);
            _providerFactory
                .Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                .Returns(Mock.Of<IAIProvider>(p => p.ProviderName == "HealthProv"));

            return new BrainarrOrchestrator(
                _logger,
                _providerFactory.Object,
                _libraryAnalyzer.Object,
                _cache.Object,
                _health.Object,
                _validator.Object,
                _models.Object,
                _http.Object,
                duplicationPrevention: null,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object);
        }

        [Fact]
        public void ProviderHealth_HealthyAndUnhealthy_StatusTextMatches()
        {
            var orch = CreateOrchestrator();
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, OpenAIApiKey = "k", OpenAIModel = "m" };
            orch.InitializeProvider(settings);

            _health.Setup(h => h.IsHealthy("HealthProv")).Returns(true);
            Assert.True(orch.IsProviderHealthy());
            Assert.Contains("Healthy", orch.GetProviderStatus());

            _health.Setup(h => h.IsHealthy("HealthProv")).Returns(false);
            Assert.False(orch.IsProviderHealthy());
            Assert.Contains("Unhealthy", orch.GetProviderStatus());
        }

        [Fact]
        public void ProviderHealth_NotInitialized_ReturnsNotInitialized()
        {
            var orch = CreateOrchestrator();
            Assert.Equal("Not Initialized", orch.GetProviderStatus());
            Assert.False(orch.IsProviderHealthy());
        }
    }
}
