using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Http;

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
            _syncHandler = req => handler(req, CancellationToken.None).GetAwaiter().GetResult();
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

        // IHttpClient surface compatibility
        public void DownloadFile(string url, string fileName) => throw new NotImplementedException();
        public Task DownloadFileAsync(string url, string fileName) => throw new NotImplementedException();
        public HttpResponse Get(HttpRequest request) => Execute(request);
        public Task<HttpResponse> GetAsync(HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
        public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
        public HttpResponse Head(HttpRequest request) => throw new NotImplementedException();
        public Task<HttpResponse> HeadAsync(HttpRequest request) => throw new NotImplementedException();
        public HttpResponse Post(HttpRequest request) => Execute(request);
        public Task<HttpResponse> PostAsync(HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
        public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
    }
}
