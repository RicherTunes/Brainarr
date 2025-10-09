using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    public sealed class OpenAIProviderContractTests
    {
        private sealed class FakeHttpClient : IHttpClient
        {
            private readonly Func<HttpRequest, HttpResponse> _handler;
            public FakeHttpClient(Func<HttpRequest, HttpResponse> handler) { _handler = handler; }

            public HttpResponse Execute(HttpRequest request) => _handler(request);
            public Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken) => Task.FromResult(_handler(request));
            public Task<HttpResponse> ExecuteAsync(HttpRequest request) => Task.FromResult(_handler(request));

            // IHttpClient surface required by tests; unused members throw to fail fast if invoked unexpectedly
            public void DownloadFile(string url, string fileName) => throw new NotImplementedException();
            public Task DownloadFileAsync(string url, string fileName) => throw new NotImplementedException();
            public HttpResponse Get(HttpRequest request) => Execute(request);
            public Task<HttpResponse> GetAsync(HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
            public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request, CancellationToken cancellationToken) where T : new() => throw new NotImplementedException();
            public HttpResponse Head(HttpRequest request) => throw new NotImplementedException();
            public Task<HttpResponse> HeadAsync(HttpRequest request) => throw new NotImplementedException();
            public Task<HttpResponse> HeadAsync(HttpRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
            public HttpResponse Post(HttpRequest request) => Execute(request);
            public Task<HttpResponse> PostAsync(HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
            public Task<HttpResponse> PostAsync(HttpRequest request, CancellationToken cancellationToken) => ExecuteAsync(request, cancellationToken);
            public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request, CancellationToken cancellationToken) where T : new() => throw new NotImplementedException();
        }

        private sealed class FakeResilience : NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience
        {
            public int Calls { get; private set; }
            public Task<HttpResponse> SendAsync(HttpRequest templateRequest, Func<HttpRequest, CancellationToken, Task<HttpResponse>> send, string origin, Logger logger, CancellationToken cancellationToken, int maxRetries = 3, int maxConcurrencyPerHost = 8, TimeSpan? retryBudget = null, TimeSpan? perRequestTimeout = null)
            {
                Calls++;
                return send(templateRequest, cancellationToken);
            }
        }

        [Fact]
        public async Task UsesInjectedResilience_ForSendAsync()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var responseBody = "{\n  \"choices\": [ { \"message\": { \"content\": \"[{\\\"artist\\\":\\\"Pink Floyd\\\", \\\"album\\\":\\\"The Dark Side of the Moon\\\", \\\"genre\\\":\\\"Progressive Rock\\\", \\\"year\\\":1973, \\\"confidence\\\":0.98, \\\"reason\\\":\\\"Seminal prog rock classic\\\"}]\" } } ]\n}";
            var fakeClient = new FakeHttpClient(req => new HttpResponse(req, new HttpHeader(), responseBody, System.Net.HttpStatusCode.OK));
            var exec = new FakeResilience();
            var provider = new OpenAIProvider(fakeClient, logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: exec);

            var recs = await provider.GetRecommendationsAsync("Recommend exactly 1 album");
            Assert.NotNull(recs);
            Assert.True(exec.Calls >= 1);
            Assert.True(recs.Count >= 1);
            Assert.Contains(recs, r => r.Artist?.Length > 0);
        }
    }
}
