using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    public class GeminiProviderTests
    {
        private class NullHttpClient : IHttpClient
        {
            public Task<HttpResponse> ExecuteAsync(HttpRequest request) => Task.FromResult<HttpResponse>(null);
            public HttpResponse Execute(HttpRequest request) => null;
            public void DownloadFile(string url, string fileName) => throw new NotImplementedException();
            public Task DownloadFileAsync(string url, string fileName) => throw new NotImplementedException();
            public HttpResponse Get(HttpRequest request) => null;
            public Task<HttpResponse> GetAsync(HttpRequest request) => Task.FromResult<HttpResponse>(null);
            public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public HttpResponse Head(HttpRequest request) => null;
            public Task<HttpResponse> HeadAsync(HttpRequest request) => Task.FromResult<HttpResponse>(null);
            public HttpResponse Post(HttpRequest request) => null;
            public Task<HttpResponse> PostAsync(HttpRequest request) => Task.FromResult<HttpResponse>(null);
            public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
        }

        private class FixedHttpClient : IHttpClient
        {
            private readonly Func<HttpRequest, HttpResponse> _res;
            public FixedHttpClient(Func<HttpRequest, HttpResponse> res) { _res = res; }
            public Task<HttpResponse> ExecuteAsync(HttpRequest request) => Task.FromResult(_res(request));
            public HttpResponse Execute(HttpRequest request) => _res(request);
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
        public async Task TestConnectionAsync_handles_null_response_gracefully()
        {
            var http = new NullHttpClient();
            var provider = new NzbDrone.Core.ImportLists.Brainarr.Services.GeminiProvider(http, L, apiKey: "AIza-TEST", model: BrainarrConstants.DefaultGeminiModel);
            var ok = await provider.TestConnectionAsync();
            ok.Should().BeFalse();
        }

        [Fact]
        public async Task TestConnectionAsync_service_disabled_sets_hint()
        {
            var json = @"{
  ""error"": {
    ""code"": 403,
    ""message"": ""Generative Language API has not been used in project 123 before or it is disabled. Enable it by visiting https://console.developers.google.com/apis/api/generativelanguage.googleapis.com/overview?project=123 then retry."",
    ""status"": ""PERMISSION_DENIED"",
    ""details"": [
      {""@type"": ""type.googleapis.com/google.rpc.ErrorInfo"", ""reason"": ""SERVICE_DISABLED"", ""domain"": ""googleapis.com"", ""metadata"": {""activationUrl"": ""https://console.developers.google.com/apis/api/generativelanguage.googleapis.com/overview?project=123"", ""consumer"": ""projects/123""}}
    ]
  }
}";
            var http = new FixedHttpClient(r => new HttpResponse(r, new HttpHeader(), json, HttpStatusCode.Forbidden));
            var provider = new NzbDrone.Core.ImportLists.Brainarr.Services.GeminiProvider(http, L, apiKey: "AIza-TEST", model: BrainarrConstants.DefaultGeminiModel);
            var ok = await provider.TestConnectionAsync();
            ok.Should().BeFalse();
            var hint = provider.GetLastUserMessage();
            hint.Should().NotBeNull();
            hint.Should().Contain("Enable the Generative Language API");
            hint.Should().Contain("generativelanguage.googleapis.com/overview?project=123");
        }
    }
}
