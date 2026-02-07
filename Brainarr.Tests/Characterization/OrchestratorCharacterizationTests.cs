using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Characterization
{
    /// <summary>
    /// M6-1: Characterization tests locking BrainarrOrchestrator behavior at public seams.
    /// These tests reuse the lightweight fakes from BrainarrOrchestratorConfigAndActionTests
    /// to verify that extraction in M6-3 preserves observable behavior.
    /// </summary>
    [Trait("Category", "Characterization")]
    [Trait("Area", "Orchestrator")]
    public class OrchestratorCharacterizationTests
    {
        // ─── Fakes (same pattern as BrainarrOrchestratorConfigAndActionTests) ──

        private class FakeProvider : IAIProvider
        {
            private readonly bool _ok;
            private readonly List<Recommendation> _recs;
            public string ProviderName { get; set; } = "Ollama";
            public FakeProvider(bool ok, List<Recommendation> recs = null)
            {
                _ok = ok;
                _recs = recs ?? new List<Recommendation>();
            }
            public Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
                => Task.FromResult(_recs);
            public Task<bool> TestConnectionAsync() => Task.FromResult(_ok);
            public void UpdateModel(string modelName) { }
        }

        private class FakeProviderFactory : IProviderFactory
        {
            private readonly IAIProvider _provider;
            public FakeProviderFactory(IAIProvider provider) { _provider = provider; }
            public IAIProvider CreateProvider(BrainarrSettings settings, IHttpClient httpClient, Logger logger) => _provider;
            public bool IsProviderAvailable(AIProvider providerType, BrainarrSettings settings) => true;
        }

        private class FakeModelDetection : IModelDetectionService
        {
            private readonly List<string> _models;
            public FakeModelDetection(List<string> models = null) { _models = models ?? new List<string>(); }
            public Task<List<string>> GetOllamaModelsAsync(string baseUrl) => Task.FromResult(_models);
            public Task<List<string>> GetLMStudioModelsAsync(string baseUrl) => Task.FromResult(new List<string>());
            public Task<string> DetectBestModelAsync(AIProvider providerType, string baseUrl) => Task.FromResult<string>(null);
        }

        private class NoopLibAnalyzer : ILibraryAnalyzer
        {
            public LibraryProfile AnalyzeLibrary() => new LibraryProfile
            {
                TotalArtists = 10,
                TotalAlbums = 30,
                TopGenres = new Dictionary<string, int> { ["Rock"] = 10 },
                TopArtists = new List<string> { "TestArtist" }
            };
            public string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode) => "test prompt";
            public string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode, bool artistMode) => "test prompt";
            public List<NzbDrone.Core.Music.Artist> GetAllArtists() => new();
            public List<NzbDrone.Core.Music.Album> GetAllAlbums() => new();
            public List<ImportListItemInfo> FilterDuplicates(List<ImportListItemInfo> recommendations) => recommendations;
            public List<Recommendation> FilterExistingRecommendations(List<Recommendation> recommendations, bool includeAlbums) => recommendations;
        }

        private class NoopCache : IRecommendationCache
        {
            public bool TryGet(string cacheKey, out List<ImportListItemInfo> recommendations)
            { recommendations = null; return false; }
            public void Set(string cacheKey, List<ImportListItemInfo> recommendations, TimeSpan? duration = null) { }
            public void Clear() { }
            public string GenerateCacheKey(string provider, int maxRecommendations, string libraryFingerprint) => "k";
        }

        private class NoopHealth : IProviderHealthMonitor
        {
            public Task<HealthStatus> CheckHealthAsync(string providerName, string baseUrl) => Task.FromResult(HealthStatus.Healthy);
            public void RecordFailure(string providerName, string error) { }
            public void RecordSuccess(string providerName, double responseTimeMs) { }
            public void RecordRateLimitInfo(string providerName, int remaining, DateTime resetAt) { }
            public void RecordAuthResult(string providerName, bool isValid) { }
            public ProviderMetrics GetMetrics(string providerName) => new ProviderMetrics();
            public HealthStatus GetHealthStatus(string providerName) => HealthStatus.Healthy;
            public bool IsHealthy(string providerName) => true;
        }

        private class NoopValidator : IRecommendationValidator
        {
            public bool ValidateRecommendation(Recommendation recommendation, bool allowArtistOnly = false) => true;
            public NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult ValidateBatch(List<Recommendation> recommendations, bool allowArtistOnly = false)
                => new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                {
                    TotalCount = recommendations.Count,
                    ValidCount = recommendations.Count,
                    FilteredCount = 0,
                    ValidRecommendations = recommendations,
                    FilteredRecommendations = new List<Recommendation>()
                };
        }

        private class NoopHttpClient : IHttpClient
        {
            public Task<HttpResponse> ExecuteAsync(HttpRequest request) => Task.FromResult(new HttpResponse(request, new HttpHeader(), "{}"));
            public HttpResponse Execute(HttpRequest request) => new HttpResponse(request, new HttpHeader(), "{}");
            public void DownloadFile(string url, string fileName) => throw new NotImplementedException();
            public Task DownloadFileAsync(string url, string fileName) => throw new NotImplementedException();
            public HttpResponse Get(HttpRequest request) => Execute(request);
            public Task<HttpResponse> GetAsync(HttpRequest request) => ExecuteAsync(request);
            public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public HttpResponse Head(HttpRequest request) => Execute(request);
            public Task<HttpResponse> HeadAsync(HttpRequest request) => ExecuteAsync(request);
            public HttpResponse Post(HttpRequest request) => Execute(request);
            public Task<HttpResponse> PostAsync(HttpRequest request) => ExecuteAsync(request);
            public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
        }

        private static BrainarrOrchestrator MakeOrchestrator(
            IAIProvider provider = null,
            IModelDetectionService md = null)
        {
            provider ??= new FakeProvider(true);
            md ??= new FakeModelDetection();

            return new BrainarrOrchestrator(
                LogManager.GetCurrentClassLogger(),
                new FakeProviderFactory(provider),
                new NoopLibAnalyzer(),
                new NoopCache(),
                new NoopHealth(),
                new NoopValidator(),
                md,
                new NoopHttpClient(),
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object);
        }

        // ─── ValidateConfiguration: failure shape contracts ───────────────────

        [Fact]
        public void ValidateConfiguration_HealthyOllama_NoProviderFailure()
        {
            var orch = MakeOrchestrator(
                provider: new FakeProvider(true) { ProviderName = "Ollama" },
                md: new FakeModelDetection(new List<string> { "llama3.2:latest" }));
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };

            var failures = new List<ValidationFailure>();
            orch.ValidateConfiguration(settings, failures);

            failures.Should().NotContain(f => f.PropertyName == "Provider" && f.ErrorMessage.Contains("connection"),
                "healthy provider should not produce connection failure");
        }

        [Fact]
        public void ValidateConfiguration_UnhealthyProvider_AddsProviderFailure()
        {
            var orch = MakeOrchestrator(
                provider: new FakeProvider(false) { ProviderName = "Ollama" },
                md: new FakeModelDetection());
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };

            var failures = new List<ValidationFailure>();
            orch.ValidateConfiguration(settings, failures);

            failures.Should().Contain(f => f.PropertyName == "Provider",
                "unhealthy provider should produce Provider failure");
        }

        [Fact]
        public void ValidateConfiguration_NoModels_AddsModelFailure()
        {
            var orch = MakeOrchestrator(
                provider: new FakeProvider(false) { ProviderName = "Ollama" },
                md: new FakeModelDetection(new List<string>()));
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };

            var failures = new List<ValidationFailure>();
            orch.ValidateConfiguration(settings, failures);

            failures.Should().Contain(f => f.PropertyName == "Model",
                "empty model list should produce Model failure");
        }

        // ─── HandleAction: response type contracts ────────────────────────────

        [Fact]
        public void HandleAction_TestConnection_ReturnsBool()
        {
            var orch = MakeOrchestrator(new FakeProvider(true) { ProviderName = "Ollama" });
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };

            var result = orch.HandleAction("testconnection", new Dictionary<string, string>(), settings);

            result.Should().BeOfType<bool>();
            ((bool)result).Should().BeTrue();
        }

        [Fact]
        public void HandleAction_TestConnection_FailedProvider_ReturnsFalse()
        {
            var orch = MakeOrchestrator(new FakeProvider(false) { ProviderName = "Ollama" });
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };

            var result = orch.HandleAction("testconnection", new Dictionary<string, string>(), settings);

            result.Should().BeOfType<bool>();
            ((bool)result).Should().BeFalse();
        }

        [Fact]
        public void HandleAction_GetModelOptions_ReturnsNonNull()
        {
            var orch = MakeOrchestrator(
                new FakeProvider(true) { ProviderName = "Ollama" },
                new FakeModelDetection(new List<string> { "llama3.2:latest", "mistral:latest" }));
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };

            var result = orch.HandleAction("getmodeloptions", new Dictionary<string, string>(), settings);

            result.Should().NotBeNull("getmodeloptions should return model options object");
        }

        [Fact]
        public void HandleAction_UnknownAction_DoesNotThrow()
        {
            var orch = MakeOrchestrator();
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };

            Action act = () => orch.HandleAction("nonexistent_action", new Dictionary<string, string>(), settings);

            act.Should().NotThrow("unknown actions should degrade gracefully");
        }

        // ─── Provider lifecycle: InitializeProvider ───────────────────────────

        [Fact]
        public void InitializeProvider_SetsProvider_DoesNotThrow()
        {
            var orch = MakeOrchestrator(new FakeProvider(true) { ProviderName = "Ollama" });
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };

            Action act = () => orch.InitializeProvider(settings);

            act.Should().NotThrow();
        }

        [Fact]
        public void IsProviderHealthy_AfterInit_ReturnsTrue()
        {
            var orch = MakeOrchestrator(new FakeProvider(true) { ProviderName = "Ollama" });
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };
            orch.InitializeProvider(settings);

            var healthy = orch.IsProviderHealthy();

            healthy.Should().BeTrue("healthy provider with NoopHealth should be healthy");
        }

        [Fact]
        public void GetProviderStatus_AfterInit_ReturnsNonEmpty()
        {
            var orch = MakeOrchestrator(new FakeProvider(true) { ProviderName = "Ollama" });
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };
            orch.InitializeProvider(settings);

            var status = orch.GetProviderStatus();

            status.Should().NotBeNullOrWhiteSpace("status should describe current provider state");
        }
    }
}
