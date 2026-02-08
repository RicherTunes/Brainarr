using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using HttpMethod = System.Net.Http.HttpMethod;

namespace Brainarr.TestKit.Providers.Http
{
    /// <summary>
    /// A real IHttpClient implementation that bridges NzbDrone.Common.Http types
    /// to System.Net.Http.HttpClient for live-service E2E tests.
    /// <para>
    /// <b>Important:</b> This bypasses Lidarr's HTTP pipeline (proxy settings,
    /// certificate handling, custom user-agent). Live test results may differ from
    /// plugin runtime behavior in environments that rely on those features.
    /// This is intentional — live tests verify provider API connectivity and
    /// response parsing, not Lidarr HTTP infrastructure.
    /// </para>
    /// <para>
    /// <b>Rate-limit handling:</b> Transparently retries HTTP 429 responses up to
    /// <see cref="MaxRetries"/> times, honoring the <c>Retry-After</c> header
    /// (capped at 60 s per wait). Tracks throttled requests in
    /// <see cref="ThrottledRequestCount"/> so tests can distinguish rate-limit
    /// failures from real failures (inconclusive vs fail classification).
    /// </para>
    /// </summary>
    public sealed class LiveHttpClient : IHttpClient, IDisposable
    {
        private const int MaxRetries = 2;
        private static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan DefaultRetryWait = TimeSpan.FromSeconds(5);

        private readonly System.Net.Http.HttpClient _inner;
        private int _throttledRequestCount;

        /// <summary>
        /// Number of HTTP 429 responses received during this client's lifetime.
        /// Used by E2E tests to classify failures as inconclusive (rate-limited)
        /// vs genuine (auth/API error).
        /// </summary>
        public int ThrottledRequestCount => _throttledRequestCount;

        public LiveHttpClient()
        {
            _inner = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        public HttpResponse Execute(NzbDrone.Common.Http.HttpRequest request)
        {
            return ExecuteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<HttpResponse> ExecuteAsync(NzbDrone.Common.Http.HttpRequest request)
        {
            return await ExecuteAsync(request, CancellationToken.None);
        }

        public async Task<HttpResponse> ExecuteAsync(NzbDrone.Common.Http.HttpRequest request, CancellationToken cancellationToken)
        {
            System.Net.Http.HttpResponseMessage? response = null;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                var msg = BuildRequestMessage(request);
                response = await _inner.SendAsync(msg, cancellationToken);

                if ((int)response.StatusCode != 429 || attempt == MaxRetries)
                    break;

                // 429 — parse Retry-After and wait before retrying
                Interlocked.Increment(ref _throttledRequestCount);
                var delay = ParseRetryAfter(response) ?? DefaultRetryWait * (attempt + 1);
                if (delay > MaxRetryWait) delay = MaxRetryWait;
                await Task.Delay(delay, cancellationToken);
                response.Dispose();
            }

            if ((int)response!.StatusCode == 429)
                Interlocked.Increment(ref _throttledRequestCount);

            var body = await response.Content.ReadAsByteArrayAsync();
            return new HttpResponse(request, new HttpHeader(), body, (HttpStatusCode)response.StatusCode);
        }

        private static HttpRequestMessage BuildRequestMessage(NzbDrone.Common.Http.HttpRequest request)
        {
            var msg = new HttpRequestMessage
            {
                RequestUri = new Uri(request.Url.FullUri),
                Method = request.Method == System.Net.Http.HttpMethod.Post ? HttpMethod.Post
                       : request.Method == System.Net.Http.HttpMethod.Put ? HttpMethod.Put
                       : request.Method == System.Net.Http.HttpMethod.Delete ? HttpMethod.Delete
                       : request.Method == System.Net.Http.HttpMethod.Head ? HttpMethod.Head
                       : HttpMethod.Get
            };

            if (request.Headers != null)
            {
                foreach (var header in request.Headers)
                {
                    if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        continue;
                    msg.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (request.ContentData != null && request.ContentData.Length > 0)
            {
                var content = new ByteArrayContent(request.ContentData);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                msg.Content = content;
            }

            return msg;
        }

        private static TimeSpan? ParseRetryAfter(System.Net.Http.HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter == null) return null;

            if (retryAfter.Delta.HasValue)
                return retryAfter.Delta.Value;

            if (retryAfter.Date.HasValue)
            {
                var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delta > TimeSpan.Zero ? delta : TimeSpan.FromSeconds(1);
            }

            return null;
        }

        // IHttpClient surface compatibility
        public void DownloadFile(string url, string fileName) => throw new NotImplementedException();
        public Task DownloadFileAsync(string url, string fileName) => Task.FromException(new NotImplementedException());

        public HttpResponse Get(NzbDrone.Common.Http.HttpRequest request) => Execute(request);
        public Task<HttpResponse> GetAsync(NzbDrone.Common.Http.HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> GetAsync(NzbDrone.Common.Http.HttpRequest request, CancellationToken ct) => ExecuteAsync(request, ct);
        public HttpResponse<T> Get<T>(NzbDrone.Common.Http.HttpRequest request) where T : new() => throw new NotSupportedException();
        public Task<HttpResponse<T>> GetAsync<T>(NzbDrone.Common.Http.HttpRequest request) where T : new() => throw new NotSupportedException();
        public Task<HttpResponse<T>> GetAsync<T>(NzbDrone.Common.Http.HttpRequest request, CancellationToken ct) where T : new() => throw new NotSupportedException();

        public HttpResponse Head(NzbDrone.Common.Http.HttpRequest request) => Execute(request);
        public Task<HttpResponse> HeadAsync(NzbDrone.Common.Http.HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> HeadAsync(NzbDrone.Common.Http.HttpRequest request, CancellationToken ct) => ExecuteAsync(request, ct);

        public HttpResponse Post(NzbDrone.Common.Http.HttpRequest request) => Execute(request);
        public Task<HttpResponse> PostAsync(NzbDrone.Common.Http.HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> PostAsync(NzbDrone.Common.Http.HttpRequest request, CancellationToken ct) => ExecuteAsync(request, ct);
        public HttpResponse<T> Post<T>(NzbDrone.Common.Http.HttpRequest request) where T : new() => throw new NotSupportedException();
        public Task<HttpResponse<T>> PostAsync<T>(NzbDrone.Common.Http.HttpRequest request) where T : new() => throw new NotSupportedException();
        public Task<HttpResponse<T>> PostAsync<T>(NzbDrone.Common.Http.HttpRequest request, CancellationToken ct) where T : new() => throw new NotSupportedException();

        public HttpResponse Put(NzbDrone.Common.Http.HttpRequest request) => Execute(request);
        public Task<HttpResponse> PutAsync(NzbDrone.Common.Http.HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> PutAsync(NzbDrone.Common.Http.HttpRequest request, CancellationToken ct) => ExecuteAsync(request, ct);
        public HttpResponse<T> Put<T>(NzbDrone.Common.Http.HttpRequest request) where T : new() => throw new NotSupportedException();
        public Task<HttpResponse<T>> PutAsync<T>(NzbDrone.Common.Http.HttpRequest request) where T : new() => throw new NotSupportedException();
        public Task<HttpResponse<T>> PutAsync<T>(NzbDrone.Common.Http.HttpRequest request, CancellationToken ct) where T : new() => throw new NotSupportedException();

        public HttpResponse Delete(NzbDrone.Common.Http.HttpRequest request) => Execute(request);
        public Task<HttpResponse> DeleteAsync(NzbDrone.Common.Http.HttpRequest request) => ExecuteAsync(request, CancellationToken.None);
        public Task<HttpResponse> DeleteAsync(NzbDrone.Common.Http.HttpRequest request, CancellationToken ct) => ExecuteAsync(request, ct);
        public HttpResponse<T> Delete<T>(NzbDrone.Common.Http.HttpRequest request) where T : new() => throw new NotSupportedException();
        public Task<HttpResponse<T>> DeleteAsync<T>(NzbDrone.Common.Http.HttpRequest request) where T : new() => throw new NotSupportedException();
        public Task<HttpResponse<T>> DeleteAsync<T>(NzbDrone.Common.Http.HttpRequest request, CancellationToken ct) where T : new() => throw new NotSupportedException();

        public void Dispose() => _inner.Dispose();
    }
}
