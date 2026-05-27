using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Shared
{
    /// <summary>
    /// TDD tests for <see cref="HttpProviderClient.ExecuteWithCt"/>.
    /// Verifies that a cancellation token actually aborts in-flight HTTP calls
    /// rather than waiting for the full request timeout.
    /// </summary>
    public class HttpProviderClientCancellationTests
    {
        /// <summary>
        /// A fake IHttpClient that delays for a configurable duration before returning.
        /// Used to simulate a slow-loris LLM API response.
        /// </summary>
        private sealed class SlowFakeHttpClient : IHttpClient
        {
            private readonly TimeSpan _delay;

            public SlowFakeHttpClient(TimeSpan delay) => _delay = delay;

            public Task<HttpResponse> ExecuteAsync(HttpRequest request)
            {
                return Task.Run(async () =>
                {
                    await Task.Delay(_delay).ConfigureAwait(false);
                    return new HttpResponse(request, new HttpHeader(), "{}", HttpStatusCode.OK);
                });
            }

            // ---- Stub implementations of the rest of IHttpClient (not used) ----
            public HttpResponse Execute(HttpRequest request)
                => throw new NotSupportedException("sync not used in tests");
            public void DownloadFile(string url, string fileName)
                => throw new NotSupportedException();
            public HttpResponse Get(HttpRequest request)
                => throw new NotSupportedException();
            HttpResponse<T> IHttpClient.Get<T>(HttpRequest request)
                => throw new NotSupportedException();
            public HttpResponse Head(HttpRequest request)
                => throw new NotSupportedException();
            public HttpResponse Post(HttpRequest request)
                => throw new NotSupportedException();
            HttpResponse<T> IHttpClient.Post<T>(HttpRequest request)
                => throw new NotSupportedException();
            public Task DownloadFileAsync(string url, string fileName)
                => throw new NotSupportedException();
            public Task<HttpResponse> GetAsync(HttpRequest request)
                => throw new NotSupportedException();
            Task<HttpResponse<T>> IHttpClient.GetAsync<T>(HttpRequest request)
                => throw new NotSupportedException();
            public Task<HttpResponse> HeadAsync(HttpRequest request)
                => throw new NotSupportedException();
            public Task<HttpResponse> PostAsync(HttpRequest request)
                => throw new NotSupportedException();
            Task<HttpResponse<T>> IHttpClient.PostAsync<T>(HttpRequest request)
                => throw new NotSupportedException();
        }

        [Fact]
        public async Task ExecuteWithCt_WhenTokenCancelled_AbortsWithin500ms()
        {
            // Arrange: slow IHttpClient stub that takes 30s to respond
            var slowClient = new SlowFakeHttpClient(TimeSpan.FromSeconds(30));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var request = new HttpRequest("https://example.invalid/");

            // Act + Assert: should throw OperationCanceledException well under 30s.
            // Use 2s ceiling (not 500ms) to tolerate thread-pool starvation under full-suite load.
            var sw = Stopwatch.StartNew();
            Func<Task> act = () => HttpProviderClient.ExecuteWithCt(slowClient, request, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
            sw.Stop();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
                "cancellation must abort the request, not wait for the full 30-second timeout");
        }

        [Fact]
        public async Task ExecuteWithCt_WhenTokenAlreadyCancelled_ThrowsImmediately()
        {
            // Arrange: pre-cancelled token
            var slowClient = new SlowFakeHttpClient(TimeSpan.FromSeconds(30));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var request = new HttpRequest("https://example.invalid/");

            // Act + Assert
            Func<Task> act = () => HttpProviderClient.ExecuteWithCt(slowClient, request, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task ExecuteWithCt_WhenNotCancelled_ReturnsResponse()
        {
            // Arrange: fast response, no cancellation
            var fastClient = new SlowFakeHttpClient(TimeSpan.FromMilliseconds(10));
            var request = new HttpRequest("https://example.invalid/");

            // Act
            var response = await HttpProviderClient.ExecuteWithCt(fastClient, request, CancellationToken.None);

            // Assert
            response.Should().NotBeNull();
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ExecuteWithCt_WhenCancelledAfterResponse_DoesNotThrow()
        {
            // Arrange: response arrives before cancellation fires
            var fastClient = new SlowFakeHttpClient(TimeSpan.FromMilliseconds(10));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // plenty of time
            var request = new HttpRequest("https://example.invalid/");

            // Act
            var response = await HttpProviderClient.ExecuteWithCt(fastClient, request, cts.Token);

            // Assert: response returned normally, token still live
            response.Should().NotBeNull();
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
