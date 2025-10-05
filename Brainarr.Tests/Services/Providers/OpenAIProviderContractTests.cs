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
            var fakeClient = new FakeHttpClient(_ => new HttpResponse(System.Net.HttpStatusCode.OK, responseBody, new HttpHeader()));
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
