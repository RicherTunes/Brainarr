using System;

using System.Collections.Generic;

using System.Threading.Tasks;

using FluentAssertions;

using NLog;

using NzbDrone.Common.Http;

using NzbDrone.Core.ImportLists.Brainarr;

using NzbDrone.Core.ImportLists.Brainarr.Configuration;

using NzbDrone.Core.ImportLists.Brainarr.Models;

using NzbDrone.Core.ImportLists.Brainarr.Services;

using NzbDrone.Core.ImportLists.Brainarr.Services.Core;

using Xunit;


namespace Brainarr.Tests.Services.Core

{

    public class ModelActionHandlerDetailsTests

    {

        private class FakeProviderWithHint : IAIProvider

        {

            public string ProviderName => "Google Gemini";

            public Task<List<Recommendation>> GetRecommendationsAsync(string prompt) => Task.FromResult(new List<Recommendation>());

            public Task<bool> TestConnectionAsync() => Task.FromResult(false);

            public void UpdateModel(string modelName) { }

            public string? GetLastUserMessage() => "Gemini API disabled for this key's Google Cloud project. Enable the Generative Language API: https://console.developers.google.com/apis/api/generativelanguage.googleapis.com/overview?project=123";

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

        public async Task HandleTestConnectionDetailsAsync_surfaces_hint_on_failure()

        {

            var settings = new BrainarrSettings { Provider = AIProvider.Gemini };

            var provider = new FakeProviderWithHint();

            var handler = new ModelActionHandler(

                new ModelDetectionService(new NoopHttpClient(), L),

                new FakeProviderFactory(provider),

                new NoopHttpClient(),

                L);


            var result = await handler.HandleTestConnectionDetailsAsync(settings);


            result.Success.Should().BeFalse();

            result.Provider.Should().Be("Google Gemini");

            result.Hint.Should().NotBeNull();

            result.Hint.Should().Contain("Enable the Generative Language API");

        }

    }

}

