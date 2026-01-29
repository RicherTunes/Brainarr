using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Performance;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;
using Xunit;

namespace Brainarr.Tests.Services.Resilience
{
    public class ResiliencePolicyTests
    {
        private sealed class FakeLimiter : IUniversalAdaptiveRateLimiter
        {
            public int WaitCalls;
            public int RecordCalls;
            public HttpResponseMessage? LastResponse;

            public Task<bool> WaitIfNeededAsync(string service, string endpoint, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref WaitCalls);
                return Task.FromResult(true);
            }

            public void RecordResponse(string service, string endpoint, HttpResponseMessage response)
            {
                Interlocked.Increment(ref RecordCalls);
                LastResponse = response;
            }

            public int GetCurrentLimit(string service, string endpoint) => 0;
            public ServiceRateLimitStats GetServiceStats(string service) => new() { ServiceName = service };
            public GlobalRateLimitStats GetGlobalStats() => new();
            public void Dispose() { }
        }

        private static HttpRequest BuildRequest(string url)
        {
            var req = new HttpRequest(url)
            {
                Method = HttpMethod.Get,
                SuppressHttpError = true,
                RequestTimeout = TimeSpan.FromSeconds(2)
            };
            return req;
        }

        [Fact]
        public async Task WithHttpResilienceAsync_429_RecordsLimiter()
        {
            // Use explicit limiter injection for test isolation (no global state)
            var limiter = new FakeLimiter();
            var logger = LogManager.GetCurrentClassLogger();
            var req = BuildRequest("http://svc.test/x");

            async Task<HttpResponse> Send(HttpRequest r, CancellationToken ct)
            {
                var headers = new HttpHeader();
                headers["Retry-After"] = "1";
                return await Task.FromResult(new HttpResponse(r, headers, string.Empty, HttpStatusCode.TooManyRequests));
            }

            var result = await ResiliencePolicy.WithHttpResilienceAsync(
                templateRequest: req,
                send: Send,
                origin: "openai:gpt-4o-mini",
                logger: logger,
                cancellationToken: CancellationToken.None,
                maxRetries: 1,
                shouldRetry: null,
                limiter: limiter,  // Pass explicitly for isolation
                retryBudget: TimeSpan.FromMilliseconds(200),
                maxConcurrencyPerHost: 2,
                perRequestTimeout: TimeSpan.FromMilliseconds(100));

            Assert.NotNull(result);
            Assert.True(limiter.WaitCalls >= 1, $"Expected WaitCalls >= 1, got {limiter.WaitCalls}");
            Assert.True(limiter.RecordCalls >= 1, $"Expected RecordCalls >= 1, got {limiter.RecordCalls}");
            Assert.Equal(HttpStatusCode.TooManyRequests, limiter.LastResponse!.StatusCode);
        }

        [Fact(Skip = "Quarantined: Timing-sensitive test passes in isolation but fails under parallel execution. Tracked for weekly lane review.")]
        public async Task WithHttpResilienceAsync_EnforcesConcurrencyCapPerHost()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var req = BuildRequest("http://cap.test/endpoint");

            int running = 0;
            int maxSeen = 0;

            async Task<HttpResponse> Send(HttpRequest r, CancellationToken ct)
            {
                var now = Interlocked.Increment(ref running);
                int snapshot;
                while (true)
                {
                    snapshot = maxSeen;
                    if (now <= snapshot) break;
                    if (Interlocked.CompareExchange(ref maxSeen, now, snapshot) == snapshot) break;
                }
                try { await Task.Delay(30, ct); } catch { }
                Interlocked.Decrement(ref running);
                return new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.OK);
            }

            var tasks = new Task[8];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = ResiliencePolicy.WithHttpResilienceAsync(
                    templateRequest: req,
                    send: Send,
                    origin: "ollama:tiny",
                    logger: logger,
                    cancellationToken: CancellationToken.None,
                    maxRetries: 0,
                    shouldRetry: null,
                    limiter: null,
                    retryBudget: TimeSpan.FromMilliseconds(200),
                    maxConcurrencyPerHost: 2,
                    perRequestTimeout: TimeSpan.FromMilliseconds(200));
            }

            await Task.WhenAll(tasks);
            Assert.True(maxSeen <= 2, $"max concurrent sends was {maxSeen}, expected <= 2");
        }
    }
}
