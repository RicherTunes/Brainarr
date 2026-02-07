using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation.Results;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorConfigAndActionTests
    {
        private class FakeProvider : IAIProvider
        {
            private readonly bool _ok;
            public string ProviderName { get; set; } = "Ollama";
            public FakeProvider(bool ok) { _ok = ok; }
            public Task<List<Recommendation>> GetRecommendationsAsync(string prompt) => Task.FromResult(new List<Recommendation>());
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
            private readonly List<string> _ollama;
            private readonly List<string> _lm;
            public FakeModelDetection(List<string> ollama, List<string> lm) { _ollama = ollama; _lm = lm; }
            public Task<List<string>> GetOllamaModelsAsync(string baseUrl) => Task.FromResult(_ollama);
            public Task<List<string>> GetLMStudioModelsAsync(string baseUrl) => Task.FromResult(_lm);
            public Task<string> DetectBestModelAsync(AIProvider providerType, string baseUrl) => Task.FromResult<string>(null);
        }

        private class NoopLibAnalyzer : ILibraryAnalyzer
        {
            public LibraryProfile AnalyzeLibrary() => new LibraryProfile();
            public string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode) => string.Empty;
            public string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode, bool artistMode) => string.Empty;
            public System.Collections.Generic.List<NzbDrone.Core.Music.Artist> GetAllArtists() => new();
            public System.Collections.Generic.List<NzbDrone.Core.Music.Album> GetAllAlbums() => new();
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

        private class NoopValidator : NzbDrone.Core.ImportLists.Brainarr.Services.IRecommendationValidator
        {
            public bool ValidateRecommendation(Recommendation recommendation, bool allowArtistOnly = false) => true;
            public NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult ValidateBatch(List<Recommendation> recommendations, bool allowArtistOnly = false)
            {
                return new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                {
                    TotalCount = recommendations.Count,
                    ValidCount = recommendations.Count,
                    FilteredCount = 0,
                    ValidRecommendations = recommendations,
                    FilteredRecommendations = new List<Recommendation>()
                };
            }
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

        private static BrainarrOrchestrator MakeOrchestrator(IAIProvider provider, IModelDetectionService md)
        {
            return new BrainarrOrchestrator(
                LogManager.GetCurrentClassLogger(),
                new FakeProviderFactory(provider),
                new NoopLibAnalyzer(),
                new NoopCache(),
                new NoopHealth(),
                new NoopValidator(),
                md,
                new NoopHttpClient(),
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object
            );
        }

        [Fact]
        public void ValidateConfiguration_adds_failures_on_connection_and_models()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };
            var orch = MakeOrchestrator(new FakeProvider(false) { ProviderName = "Ollama" }, new FakeModelDetection(new List<string>(), new List<string>()));

            var failures = new List<ValidationFailure>();
            orch.ValidateConfiguration(settings, failures);
            failures.Should().NotBeEmpty();
            failures.Should().Contain(f => f.PropertyName == "Provider");
            failures.Should().Contain(f => f.PropertyName == "Model");
        }

        [Fact]
        public void HandleAction_getmodeloptions_returns_options()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };
            var orch = MakeOrchestrator(new FakeProvider(true) { ProviderName = "Ollama" }, new FakeModelDetection(new List<string> { "llama3.2:latest" }, new List<string>()));

            var result = orch.HandleAction("getmodeloptions", new Dictionary<string, string>(), settings);
            result.Should().NotBeNull();
        }

        [Fact]
        public void HandleAction_testconnection_returns_bool()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };
            var orch = MakeOrchestrator(new FakeProvider(true) { ProviderName = "Ollama" }, new FakeModelDetection(new List<string>(), new List<string>()));
            var result = orch.HandleAction("testconnection", new Dictionary<string, string>(), settings);
            result.Should().BeOfType<bool>().And.Be(true);
        }
    }
}
