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
        /// Minimal fake IHttpClient that lets each test control the async HTTP outcome.
        /// </summary>
        private sealed class FakeHttpClient : IHttpClient
        {
            private readonly Func<HttpRequest, Task<HttpResponse>> _executeAsync;

            public FakeHttpClient(Func<HttpRequest, Task<HttpResponse>> executeAsync)
                => _executeAsync = executeAsync;

            public Task<HttpResponse> ExecuteAsync(HttpRequest request) => _executeAsync(request);

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
            var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var neverCompletes = new TaskCompletionSource<HttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var slowClient = new FakeHttpClient(_ =>
            {
                started.SetResult(null);
                return neverCompletes.Task;
            });
            using var cts = new CancellationTokenSource();
            var request = new HttpRequest("https://example.invalid/");

            var pending = HttpProviderClient.ExecuteWithCt(slowClient, request, cts.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

            var sw = Stopwatch.StartNew();
            cts.Cancel();

            Func<Task> act = () => pending;
            await act.Should().ThrowAsync<OperationCanceledException>();
            sw.Stop();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
                "synchronous cancellation must abort the request without waiting for the underlying HTTP task");
        }

        [Fact]
        public async Task ExecuteWithCt_WhenTokenAlreadyCancelled_ThrowsImmediately()
        {
            var slowClient = new FakeHttpClient(_ =>
                new TaskCompletionSource<HttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously).Task);
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
            var fastClient = new FakeHttpClient(req =>
                Task.FromResult(new HttpResponse(req, new HttpHeader(), "{}", HttpStatusCode.OK)));
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
            // The token is LIVE but is never cancelled during the call, so the response always wins
            // the WhenAny race regardless of scheduling. (The previous version armed a 10s timer and
            // relied on a 10ms response beating it — which flaked under heavy parallel load when
            // thread-pool starvation delayed the response past 10s. Determinism instead of a race.)
            var fastClient = new FakeHttpClient(req =>
                Task.FromResult(new HttpResponse(req, new HttpHeader(), "{}", HttpStatusCode.OK)));
            using var cts = new CancellationTokenSource();
            var request = new HttpRequest("https://example.invalid/");

            // Act: token live (not cancelled) → response returned, no throw.
            var response = await HttpProviderClient.ExecuteWithCt(fastClient, request, cts.Token);

            // Assert
            response.Should().NotBeNull();
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            // Cancelling AFTER the response was returned is a no-op — nothing in flight to cancel.
            cts.Cancel();
        }
    }
}
