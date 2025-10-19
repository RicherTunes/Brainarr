using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace Brainarr.TestKit.Providers.Fakes
{
    public sealed class FakeHttpClient : IHttpClient
    {
        private readonly Func<HttpRequest, HttpResponse> _syncHandler;
        private readonly Func<HttpRequest, CancellationToken, Task<HttpResponse>> _asyncHandler;

        public List<HttpRequest> Requests { get; } = new();

        public FakeHttpClient(Func<HttpRequest, HttpResponse> handler)
        {
            _syncHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            _asyncHandler = (req, ct) => Task.FromResult(handler(req));
        }

        public FakeHttpClient(Func<HttpRequest, CancellationToken, Task<HttpResponse>> handler)
        {
            _asyncHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            // Bridge async handler to sync safely to avoid deadlocks in tests
            _syncHandler = req => SafeAsyncHelper.RunSafeSync(() => handler(req, CancellationToken.None));
        }

        public HttpResponse Execute(HttpRequest request)
        {
            Requests.Add(request);
            return _syncHandler(request);
        }

        public Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return _asyncHandler(request, cancellationToken);
        }
        public Task<HttpResponse> ExecuteAsync(HttpRequest request)
        {
            Requests.Add(request);
            return _asyncHandler(request, CancellationToken.None);
        }

        // IHttpClient surface compatibility (cover both sync/async and typed variants)
        public void DownloadFile(string url, string fileName) => throw new NotImplementedException();
        public Task DownloadFileAsync(string url, string fileName) => Task.FromException(new NotImplementedException());

        public HttpResponse Get(HttpRequest request) => Execute(request);
        public Task<HttpResponse> GetAsync(HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> GetAsync(HttpRequest request, CancellationToken cancellationToken) => ExecuteAsync(request, cancellationToken);
        public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotSupportedException("Typed responses are not used in TestKit.");
        public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => Task.FromException<HttpResponse<T>>(new NotSupportedException("Typed responses are not used in TestKit."));
        public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request, CancellationToken cancellationToken) where T : new() => Task.FromException<HttpResponse<T>>(new NotSupportedException("Typed responses are not used in TestKit."));

        public HttpResponse Head(HttpRequest request) => Execute(request);
        public Task<HttpResponse> HeadAsync(HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> HeadAsync(HttpRequest request, CancellationToken cancellationToken) => ExecuteAsync(request, cancellationToken);

        public HttpResponse Post(HttpRequest request) => Execute(request);
        public Task<HttpResponse> PostAsync(HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> PostAsync(HttpRequest request, CancellationToken cancellationToken) => ExecuteAsync(request, cancellationToken);
        public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotSupportedException("Typed responses are not used in TestKit.");
        public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => Task.FromException<HttpResponse<T>>(new NotSupportedException("Typed responses are not used in TestKit."));
        public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request, CancellationToken cancellationToken) where T : new() => Task.FromException<HttpResponse<T>>(new NotSupportedException("Typed responses are not used in TestKit."));

        public HttpResponse Put(HttpRequest request) => Execute(request);
        public Task<HttpResponse> PutAsync(HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> PutAsync(HttpRequest request, CancellationToken cancellationToken) => ExecuteAsync(request, cancellationToken);
        public HttpResponse<T> Put<T>(HttpRequest request) where T : new() => throw new NotSupportedException("Typed responses are not used in TestKit.");
        public Task<HttpResponse<T>> PutAsync<T>(HttpRequest request) where T : new() => Task.FromException<HttpResponse<T>>(new NotSupportedException("Typed responses are not used in TestKit."));
        public Task<HttpResponse<T>> PutAsync<T>(HttpRequest request, CancellationToken cancellationToken) where T : new() => Task.FromException<HttpResponse<T>>(new NotSupportedException("Typed responses are not used in TestKit."));

        public HttpResponse Delete(HttpRequest request) => Execute(request);
        public Task<HttpResponse> DeleteAsync(HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> DeleteAsync(HttpRequest request, CancellationToken cancellationToken) => ExecuteAsync(request, cancellationToken);
        public HttpResponse<T> Delete<T>(HttpRequest request) where T : new() => throw new NotSupportedException("Typed responses are not used in TestKit.");
        public Task<HttpResponse<T>> DeleteAsync<T>(HttpRequest request) where T : new() => Task.FromException<HttpResponse<T>>(new NotSupportedException("Typed responses are not used in TestKit."));
        public Task<HttpResponse<T>> DeleteAsync<T>(HttpRequest request, CancellationToken cancellationToken) where T : new() => Task.FromException<HttpResponse<T>>(new NotSupportedException("Typed responses are not used in TestKit."));
    }
}
