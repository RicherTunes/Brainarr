using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class ModelActionHandlerTests
    {
        private class FakeModelDetection : IModelDetectionService
        {
            public Task<List<string>> GetOllamaModelsAsync(string baseUrl) => Task.FromResult(new List<string> { "qwen2.5:latest", "llama3.2:latest" });
            public Task<List<string>> GetLMStudioModelsAsync(string baseUrl) => Task.FromResult(new List<string> { "local-model" });
            public Task<string> DetectBestModelAsync(AIProvider providerType, string baseUrl) => Task.FromResult("qwen2.5:latest");
        }

        private class FakeProvider : IAIProvider
        {
            public string ProviderName { get; set; } = "OpenAI";
            public Task<List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation>> GetRecommendationsAsync(string prompt) => Task.FromResult(new List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation>());
            public Task<ProviderHealthResult> TestConnectionAsync() => Task.FromResult(ProviderHealthResult.Healthy(responseTime: TimeSpan.FromSeconds(1)));
            public void UpdateModel(string modelName) { }
        }

        private class FakeProviderFactory : IProviderFactory
        {
            private readonly IAIProvider _provider;
            public FakeProviderFactory(IAIProvider provider) { _provider = provider; }
            public IAIProvider CreateProvider(BrainarrSettings settings, IHttpClient httpClient, Logger logger) => _provider;
            public bool IsProviderAvailable(AIProvider providerType, BrainarrSettings settings) => true;
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

        private static Logger L => LogManager.GetCurrentClassLogger();

        [Fact]
        public async Task HandleTestConnectionAsync_success_and_detects_for_local()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                ConfigurationUrl = "http://localhost:11434"
            };
            var handler = new ModelActionHandler(new FakeModelDetection(), new FakeProviderFactory(new FakeProvider { ProviderName = "Ollama" }), new NoopHttpClient(), L);
            var result = await handler.HandleTestConnectionAsync(settings);
            result.Should().StartWith("Success");
        }

        [Fact]
        public async Task HandleGetModelsAsync_ollama_returns_detected_options()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, ConfigurationUrl = "http://localhost:11434" };
            var handler = new ModelActionHandler(new FakeModelDetection(), new FakeProviderFactory(new FakeProvider()), new NoopHttpClient(), L);
            var options = await handler.HandleGetModelsAsync(settings);
            options.Should().NotBeNull();
            options.Should().Contain(o => o.Value.Contains("qwen"));
        }

        [Fact]
        public async Task HandleGetModelsAsync_static_for_openai()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var handler = new ModelActionHandler(new FakeModelDetection(), new FakeProviderFactory(new FakeProvider()), new NoopHttpClient(), L);
            var options = await handler.HandleGetModelsAsync(settings);
            options.Should().NotBeEmpty();
        }
    }
}
