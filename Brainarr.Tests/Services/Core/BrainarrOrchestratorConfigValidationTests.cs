using System;
using System.Collections.Generic;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;
using Lidarr.Plugin.Common.Abstractions.Llm;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorConfigValidationTests
    {
        private readonly Mock<IProviderFactory> _providerFactory = new();
        private readonly Mock<ILibraryAnalyzer> _libraryAnalyzer = new();
        private readonly Mock<IRecommendationCache> _cache = new();
        private readonly Mock<IProviderHealthMonitor> _health = new();
        private readonly Mock<IRecommendationValidator> _validator = new();
        private readonly Mock<IModelDetectionService> _models = new();
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        private BrainarrOrchestrator CreateOrchestrator(Action<Mock<IAIProvider>> configureProvider)
        {
            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.ProviderName).Returns("TestProvider");
            configureProvider(mockProvider);
            _providerFactory
                .Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                .Returns(mockProvider.Object);

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

        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        public void ValidateConfiguration_WithEmptyLocalUrls_AddsFailures(AIProvider provider)
        {
            var orch = CreateOrchestrator(p => p.Setup(x => x.TestConnectionAsync()).ReturnsAsync(ProviderHealthResult.Unhealthy("Connection failed")));

            var settings = new BrainarrSettings { Provider = provider };
            if (provider == AIProvider.Ollama) settings.OllamaUrl = string.Empty;
            if (provider == AIProvider.LMStudio) settings.LMStudioUrl = string.Empty;

            _models.Setup(m => m.GetOllamaModelsAsync(It.IsAny<string>())).ReturnsAsync(new List<string>());
            _models.Setup(m => m.GetLMStudioModelsAsync(It.IsAny<string>())).ReturnsAsync(new List<string>());

            var failures = new List<ValidationFailure>();

            orch.ValidateConfiguration(settings, failures);

            failures.Should().NotBeEmpty();
            failures.Should().Contain(f => f.PropertyName == "Provider" && f.ErrorMessage.Contains("Unable to connect"));
            failures.Should().Contain(f => f.PropertyName == "Model" && f.ErrorMessage.Contains("No models detected"));
        }

        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        public void ValidateConfiguration_WithMalformedLocalUrl_AddsConfigurationFailure(AIProvider provider)
        {
            var orch = CreateOrchestrator(p => p.Setup(x => x.TestConnectionAsync()).ReturnsAsync(ProviderHealthResult.Unhealthy("Connection failed")));

            var settings = new BrainarrSettings { Provider = provider };
            if (provider == AIProvider.Ollama) settings.OllamaUrl = "http://bad:999999"; // invalid port
            if (provider == AIProvider.LMStudio) settings.LMStudioUrl = "http://bad:999999";

            _models.Setup(m => m.GetOllamaModelsAsync(It.IsAny<string>())).ThrowsAsync(new Exception("bad url"));
            _models.Setup(m => m.GetLMStudioModelsAsync(It.IsAny<string>())).ThrowsAsync(new Exception("bad url"));

            var failures = new List<ValidationFailure>();

            orch.ValidateConfiguration(settings, failures);

            failures.Should().Contain(f => f.PropertyName == "Configuration" && f.ErrorMessage.StartsWith("Validation error:"));
        }
    }
}
