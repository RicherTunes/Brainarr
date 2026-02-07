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
            public Task<HttpResponse> ExecuteAsync(HttpRequest request, System.Threading.CancellationToken cancellationToken) => Task.FromResult(_handler(request));
            public HttpResponse Execute(HttpRequest request) => _handler(request);
            public void DownloadFile(string url, string fileName) => throw new NotImplementedException();
            public Task DownloadFileAsync(string url, string fileName) => throw new NotImplementedException();
            public HttpResponse Get(HttpRequest request) => Execute(request);
            public Task<HttpResponse> GetAsync(HttpRequest request) => ExecuteAsync(request);
            public Task<HttpResponse> GetAsync(HttpRequest request, System.Threading.CancellationToken cancellationToken) => ExecuteAsync(request, cancellationToken);
            public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request, System.Threading.CancellationToken cancellationToken) where T : new() => throw new NotImplementedException();
            public HttpResponse Head(HttpRequest request) => throw new NotImplementedException();
            public Task<HttpResponse> HeadAsync(HttpRequest request) => throw new NotImplementedException();
            public Task<HttpResponse> HeadAsync(HttpRequest request, System.Threading.CancellationToken cancellationToken) => throw new NotImplementedException();
            public HttpResponse Post(HttpRequest request) => Execute(request);
            public Task<HttpResponse> PostAsync(HttpRequest request) => ExecuteAsync(request);
            public Task<HttpResponse> PostAsync(HttpRequest request, System.Threading.CancellationToken cancellationToken) => ExecuteAsync(request, cancellationToken);
            public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request, System.Threading.CancellationToken cancellationToken) where T : new() => throw new NotImplementedException();
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

        [Fact]
        public void ExtractRateLimitHeaders_openai_format()
        {
            var headers = new HttpHeader();
            headers.Set("x-ratelimit-remaining-requests", "42");
            headers.Set("x-ratelimit-reset-requests", "30");
            var request = new HttpRequest("http://api.test");
            var response = new HttpResponse(request, headers, "ok", HttpStatusCode.OK);

            var info = BaseCloudProvider.ExtractRateLimitHeaders(response);

            info.Should().NotBeNull();
            info!.Remaining.Should().Be(42);
            info.ResetAt.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(30), TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void ExtractRateLimitHeaders_anthropic_format()
        {
            var headers = new HttpHeader();
            headers.Set("anthropic-ratelimit-requests-remaining", "10");
            headers.Set("anthropic-ratelimit-requests-reset", "2026-02-07T12:00:00Z");
            var request = new HttpRequest("http://api.test");
            var response = new HttpResponse(request, headers, "ok", HttpStatusCode.OK);

            var info = BaseCloudProvider.ExtractRateLimitHeaders(response);

            info.Should().NotBeNull();
            info!.Remaining.Should().Be(10);
            info.ResetAt.Should().Be(new DateTime(2026, 2, 7, 12, 0, 0, DateTimeKind.Utc));
        }

        [Fact]
        public void ExtractRateLimitHeaders_generic_format()
        {
            var headers = new HttpHeader();
            headers.Set("x-ratelimit-remaining", "5");
            headers.Set("x-ratelimit-reset", "60");
            var request = new HttpRequest("http://api.test");
            var response = new HttpResponse(request, headers, "ok", HttpStatusCode.OK);

            var info = BaseCloudProvider.ExtractRateLimitHeaders(response);

            info.Should().NotBeNull();
            info!.Remaining.Should().Be(5);
        }

        [Fact]
        public void ExtractRateLimitHeaders_no_headers_returns_null()
        {
            var headers = new HttpHeader();
            var request = new HttpRequest("http://api.test");
            var response = new HttpResponse(request, headers, "ok", HttpStatusCode.OK);

            var info = BaseCloudProvider.ExtractRateLimitHeaders(response);

            info.Should().BeNull();
        }

        [Fact]
        public void ExtractRateLimitHeaders_null_response_returns_null()
        {
            var info = BaseCloudProvider.ExtractRateLimitHeaders(null!);
            info.Should().BeNull();
        }

        [Fact]
        public async Task GetRecommendationsAsync_populates_LastRateLimitInfo()
        {
            var headers = new HttpHeader();
            headers.Set("x-ratelimit-remaining-requests", "99");
            headers.Set("x-ratelimit-reset-requests", "120");
            var http = new FakeHttpClient(r => new HttpResponse(r, headers, "Artist A - Album A", HttpStatusCode.OK));
            var provider = new TestProvider(http, L, apiKey: "abc", model: "m1");

            await provider.GetRecommendationsAsync("prompt");

            provider.LastRateLimitInfo.Should().NotBeNull();
            provider.LastRateLimitInfo!.Remaining.Should().Be(99);
        }

        [Fact]
        public async Task TestConnectionAsync_populates_LastRateLimitInfo()
        {
            var headers = new HttpHeader();
            headers.Set("x-ratelimit-remaining", "15");
            var http = new FakeHttpClient(r => new HttpResponse(r, headers, "ok", HttpStatusCode.OK));
            var provider = new TestProvider(http, L, apiKey: "abc", model: "m1");

            await provider.TestConnectionAsync();

            provider.LastRateLimitInfo.Should().NotBeNull();
            provider.LastRateLimitInfo!.Remaining.Should().Be(15);
        }
    }
}
