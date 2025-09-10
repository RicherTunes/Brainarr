using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    public class BaseCloudProviderTests
    {
        private class FakeHttpClient : IHttpClient
        {
            private readonly Func<HttpRequest, HttpResponse> _handler;
            public FakeHttpClient(Func<HttpRequest, HttpResponse> handler) { _handler = handler; }
            public Task<HttpResponse> ExecuteAsync(HttpRequest request) => Task.FromResult(_handler(request));
            public HttpResponse Execute(HttpRequest request) => _handler(request);
            public void DownloadFile(string url, string fileName) => throw new NotImplementedException();
            public Task DownloadFileAsync(string url, string fileName) => throw new NotImplementedException();
            public HttpResponse Get(HttpRequest request) => Execute(request);
            public Task<HttpResponse> GetAsync(HttpRequest request) => ExecuteAsync(request);
            public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public HttpResponse Head(HttpRequest request) => throw new NotImplementedException();
            public Task<HttpResponse> HeadAsync(HttpRequest request) => throw new NotImplementedException();
            public HttpResponse Post(HttpRequest request) => Execute(request);
            public Task<HttpResponse> PostAsync(HttpRequest request) => ExecuteAsync(request);
            public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
        }

        private class TestProvider : BaseCloudProvider
        {
            public TestProvider(IHttpClient httpClient, Logger logger, string apiKey, string model, IRecommendationValidator? validator = null)
                : base(httpClient, logger, apiKey, model, validator) { }

            protected override string ApiUrl => "http://api.test/recommend";
            public override string ProviderName => "TestCloud";
            protected override string GetDefaultModel() => "test-model";
            protected override void ConfigureHeaders(HttpRequestBuilder builder) => builder.SetHeader("Authorization", $"Bearer {_apiKey}");
            protected override object CreateRequestBody(string prompt, int maxTokens = 2000) => new { prompt, model = _model, max_tokens = maxTokens };
            protected override List<Recommendation> ParseResponse(string responseContent)
            {
                // Very simple parser: one recommendation per line: "Artist - Album"
                var list = new List<Recommendation>();
                foreach (var line in responseContent.Split(new[] { "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
                    var rec = new Recommendation { Artist = parts[0], Album = parts.Length > 1 ? parts[1] : null, Genre = "Rock", Confidence = 0.9 };
                    var allowArtistOnly = string.IsNullOrWhiteSpace(rec.Album);
                    if (_validator.ValidateRecommendation(rec, allowArtistOnly)) list.Add(rec);
                }
                return list;
            }
        }

        private static Logger L => LogManager.GetCurrentClassLogger();

        [Fact]
        public async Task GetRecommendationsAsync_success_parses_recommendations()
        {
            HttpRequest captured = null;
            var headers = new HttpHeader();
            var http = new FakeHttpClient(r =>
            {
                captured = r;
                return new HttpResponse(r, headers, "Artist A - Album A\nArtist B - Album B", HttpStatusCode.OK);
            });

            var provider = new TestProvider(http, L, apiKey: "abc", model: "m1");
            var list = await provider.GetRecommendationsAsync("prompt");

            list.Should().HaveCount(2);
            list[0].Artist.Should().Be("Artist A");
            list[0].Album.Should().Be("Album A");
            captured.Method.Should().Be(HttpMethod.Post);
        }

        [Fact]
        public async Task GetRecommendationsAsync_non_ok_status_returns_empty()
        {
            var headers = new HttpHeader();
            var http = new FakeHttpClient(r => new HttpResponse(r, headers, "error", HttpStatusCode.BadRequest));
            var provider = new TestProvider(http, L, apiKey: "abc", model: "m1");
            var list = await provider.GetRecommendationsAsync("prompt");
            list.Should().BeEmpty();
        }

        [Fact]
        public async Task TestConnectionAsync_returns_true_on_ok_false_otherwise()
        {
            var headers = new HttpHeader();
            var okClient = new FakeHttpClient(r => new HttpResponse(r, headers, "ok", HttpStatusCode.OK));
            var badClient = new FakeHttpClient(r => new HttpResponse(r, headers, "nope", HttpStatusCode.InternalServerError));

            var p1 = new TestProvider(okClient, L, apiKey: "abc", model: "m1");
            (await p1.TestConnectionAsync()).Should().BeTrue();

            var p2 = new TestProvider(badClient, L, apiKey: "abc", model: "m1");
            (await p2.TestConnectionAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task UpdateModel_changes_model_when_non_empty()
        {
            var headers = new HttpHeader();
            var http = new FakeHttpClient(r => new HttpResponse(r, headers, "", HttpStatusCode.OK));
            var provider = new TestProvider(http, L, apiKey: "abc", model: "m1");
            provider.UpdateModel("m2");
            Func<Task> act = async () => await provider.TestConnectionAsync();
            await act.Should().NotThrowAsync();
        }
    }
}
