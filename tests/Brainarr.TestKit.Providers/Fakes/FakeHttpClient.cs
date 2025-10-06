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
    }
}
