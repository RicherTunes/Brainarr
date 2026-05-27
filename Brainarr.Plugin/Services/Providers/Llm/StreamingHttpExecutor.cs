using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Errors;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// Bridges <see cref="ILlmProvider.StreamAsync"/> implementations to a raw
    /// <see cref="System.IO.Stream"/> that common's <c>IStreamDecoder</c> implementations
    /// can consume.
    ///
    /// <para>
    /// Lidarr's host <c>NzbDrone.Common.Http.IHttpClient</c> buffers the entire response
    /// body before returning. SSE / NDJSON streaming requires reading the response body
    /// incrementally as bytes arrive, so this executor uses
    /// <see cref="System.Net.Http.HttpClient"/> directly with
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/> to get headers without
    /// buffering the body.
    /// </para>
    ///
    /// <para>
    /// This is an LLM-specific bridge — it does not need Lidarr's IHttpClient delegating
    /// handlers (rate-limit telemetry, etc.) because LLM providers are external HTTPS
    /// endpoints addressed directly. Per-provider rate limits live in brainarr's own
    /// <c>RateLimiter</c> service, not in the host HTTP pipeline.
    /// </para>
    ///
    /// <para>
    /// Tests inject a fake <see cref="HttpMessageHandler"/> via the constructor overload to
    /// emit canned SSE frames without hitting the network.
    /// </para>
    /// </summary>
    public sealed class StreamingHttpExecutor
    {
        private static readonly Lazy<HttpClient> SharedClient = new(static () =>
        {
            // No timeout at the HttpClient level — per-request timeouts arrive via the
            // CancellationToken so the streaming read is not interrupted mid-frame after a
            // fixed wall-clock window.
            var c = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            return c;
        });

        private readonly HttpClient _client;

        /// <summary>
        /// Default singleton — uses a process-wide pooled <see cref="HttpClient"/>.
        /// </summary>
        public static StreamingHttpExecutor Shared { get; } = new();

        /// <summary>
        /// Production constructor. Uses the shared pooled HttpClient.
        /// </summary>
        public StreamingHttpExecutor()
        {
            _client = SharedClient.Value;
        }

        /// <summary>
        /// Test seam — caller supplies its own message handler (typically a fake that
        /// emits SSE frames).
        /// </summary>
        public StreamingHttpExecutor(HttpMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _client = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }

        /// <summary>
        /// Sends the supplied request and returns a <see cref="Stream"/> over the response
        /// body without buffering. The caller owns the stream and must dispose it.
        /// </summary>
        /// <param name="providerId">Provider id for error mapping (e.g. "openai").</param>
        /// <param name="method">HTTP method (typically POST).</param>
        /// <param name="url">Absolute request URL.</param>
        /// <param name="headers">Request headers (including Authorization). Content-Type
        /// belongs on <paramref name="contentType"/>; do not pass it here.</param>
        /// <param name="jsonBody">JSON-serialized request body, or null for no body.</param>
        /// <param name="contentType">Body content type (defaults to application/json).</param>
        /// <param name="cancellationToken">Cancellation token for both connection and read.</param>
        /// <returns>The response body stream. The caller decoder reads incrementally.</returns>
        /// <exception cref="LlmProviderException">When the response status is non-2xx.</exception>
        public async Task<Stream> SendForStreamingAsync(
            string providerId,
            HttpMethod method,
            string url,
            IEnumerable<KeyValuePair<string, string>> headers,
            string? jsonBody,
            string contentType = "application/json",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("providerId required", nameof(providerId));
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("url required", nameof(url));

            using var req = new HttpRequestMessage(method, url);
            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }

            if (jsonBody != null)
            {
                var content = new StringContent(jsonBody);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                req.Content = content;
            }

            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not LlmProviderException)
            {
                throw LlmErrorMapper.MapException(providerId, ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                string? body = null;
                try
                {
                    body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort — keep body null if we cannot read it.
                }

                TimeSpan? retryAfter = null;
                if (response.Headers.RetryAfter is { } ra)
                {
                    if (ra.Delta.HasValue) retryAfter = ra.Delta;
                    else if (ra.Date.HasValue) retryAfter = ra.Date.Value - DateTimeOffset.UtcNow;
                }

                response.Dispose();
                throw LlmErrorMapper.MapHttpError(
                    providerId,
                    (int)response.StatusCode,
                    Truncate(body),
                    retryAfter,
                    inner: null);
            }

            // Hand ownership of the stream to the caller (decoder). The caller must dispose it.
            return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }

        private static string? Truncate(string? body, int max = 500)
        {
            if (string.IsNullOrEmpty(body)) return body;
            return body!.Length <= max ? body : body.Substring(0, max);
        }
    }
}
