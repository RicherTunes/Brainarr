using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests
{
    [Trait("Category", "Integration")]
    public class ModelOptionsRefreshTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        private static string ToJson(object obj)
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
        }

        [Fact]
        public void GetModelOptions_Should_Reflect_Provider_Changes_Within_Session()
        {
            // Arrange: mocks for required orchestrator deps (only model detection used here)
            var providerFactory = new Mock<IProviderFactory>();
            // Default: provider is available
            providerFactory.Setup(x => x.IsProviderAvailable(It.IsAny<AIProvider>(), It.IsAny<BrainarrSettings>()))
                .Returns(true);
            var libraryAnalyzer = new Mock<ILibraryAnalyzer>();

            libraryAnalyzer.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))

                .Returns<List<ImportListItemInfo>>(x => x);

            libraryAnalyzer.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))

                .Returns<List<Recommendation>, bool>((recs, _) => recs);
            var cache = new Mock<IRecommendationCache>();
            var providerHealth = new Mock<IProviderHealthMonitor>();
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();
            var httpClient = new Mock<IHttpClient>();

            // LM Studio live detection should return local models
            modelDetection
                .Setup(m => m.GetLMStudioModelsAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<string> { "local-A", "local-B" });

            var orchestrator = new BrainarrOrchestrator(
                _logger,
                providerFactory.Object,
                libraryAnalyzer.Object,
                cache.Object,
                providerHealth.Object,
                validator.Object,
                modelDetection.Object,
                httpClient.Object,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object);

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "http://localhost:1234"
            };

            // Act 1: ask for LM Studio models
            var lmResult = orchestrator.HandleAction(
                "getModelOptions",
                new Dictionary<string, string> { { "provider", "LMStudio" } },
                settings);

            var lmJson = ToJson(lmResult);

            // Act 2: simulate provider change to Perplexity, then request models again
            var _ = orchestrator.HandleAction(
                "providerChanged",
                new Dictionary<string, string> { { "provider", "Perplexity" } },
                settings);

            var pplxResult = orchestrator.HandleAction(
                "getModelOptions",
                new Dictionary<string, string> { { "provider", "Perplexity" } },
                settings);

            var pplxJson = ToJson(pplxResult);

            // Assert: LM Studio response contains local models; Perplexity contains canonical sonar options
            lmJson.Should().Contain("local-A").And.Contain("local-B");
            pplxJson.Should().ContainEquivalentOf("sonar");
            pplxJson.Should().NotContain("local-A");
        }

        [Theory]
        [InlineData(AIProvider.OpenAI, "GPT41")]
        [InlineData(AIProvider.Anthropic, "ClaudeSonnet4")]
        [InlineData(AIProvider.Perplexity, "Sonar_Pro")]
        [InlineData(AIProvider.OpenRouter, "Auto")]
        [InlineData(AIProvider.DeepSeek, "DeepSeek_Chat")]
        [InlineData(AIProvider.Gemini, "Gemini_25_Flash")]
        [InlineData(AIProvider.Groq, "Llama33_70B_Versatile")]
        public void StaticProviders_Should_Return_Canonical_Model_Options(AIProvider provider, string expectedValue)
        {
            var providerFactory = new Mock<IProviderFactory>();
            // Default: provider is available
            providerFactory.Setup(x => x.IsProviderAvailable(It.IsAny<AIProvider>(), It.IsAny<BrainarrSettings>()))
                .Returns(true);
            var libraryAnalyzer = new Mock<ILibraryAnalyzer>();

            libraryAnalyzer.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))

                .Returns<List<ImportListItemInfo>>(x => x);

            libraryAnalyzer.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))

                .Returns<List<Recommendation>, bool>((recs, _) => recs);
            var cache = new Mock<IRecommendationCache>();
            var providerHealth = new Mock<IProviderHealthMonitor>();
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();
            var httpClient = new Mock<IHttpClient>();

            var orchestrator = new BrainarrOrchestrator(
                _logger,
                providerFactory.Object,
                libraryAnalyzer.Object,
                cache.Object,
                providerHealth.Object,
                validator.Object,
                modelDetection.Object,
                httpClient.Object,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object);

            var settings = new BrainarrSettings
            {
                Provider = provider
            };

            var result = orchestrator.HandleAction(
                "getModelOptions",
                new Dictionary<string, string> { { "provider", provider.ToString() } },
                settings);

            var json = ToJson(result);
            Console.WriteLine($"Provider: {provider}, JSON: {json}");
            json.Should().Contain(expectedValue);
        }
    }
}
