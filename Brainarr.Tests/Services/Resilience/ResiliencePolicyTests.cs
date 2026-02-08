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

        [Fact]
        public async Task WithHttpResilienceAsync_EnforcesConcurrencyCapPerHost()
        {
            var logger = LogManager.GetCurrentClassLogger();
            // Unique host per invocation to avoid HostGateRegistry shared state
            var uniqueHost = $"cap-{Guid.NewGuid():N}.test";
            var req = BuildRequest($"http://{uniqueHost}/endpoint");

            int inflight = 0;
            int maxSeen = 0;
            // Gate blocks all Send delegates until we explicitly release them.
            // This eliminates Task.Delay timing sensitivity — the assertion is
            // purely about the concurrency cap, not scheduling speed.
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Counts how many Send delegates have entered (to know when 2 are blocked).
            var entered = new SemaphoreSlim(0, 8);

            async Task<HttpResponse> Send(HttpRequest r, CancellationToken ct)
            {
                var now = Interlocked.Increment(ref inflight);
                int snapshot;
                while (true)
                {
                    snapshot = maxSeen;
                    if (now <= snapshot) break;
                    if (Interlocked.CompareExchange(ref maxSeen, now, snapshot) == snapshot) break;
                }

                entered.Release(); // signal that we entered the send body
                await gate.Task;   // block until the test releases the gate
                Interlocked.Decrement(ref inflight);
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
                    retryBudget: TimeSpan.FromSeconds(60),
                    maxConcurrencyPerHost: 2,
                    perRequestTimeout: TimeSpan.FromSeconds(60));
            }

            // Wait for exactly 2 sends to enter (the concurrency cap).
            // If the semaphore is correctly enforced, only 2 can reach Send
            // while the other 6 are blocked on the HostGateRegistry semaphore.
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(30)),
                "timed out waiting for first send to enter");
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(30)),
                "timed out waiting for second send to enter");

            // Brief yield to let any racing third task that might have slipped
            // through a broken semaphore actually increment inflight.
            await Task.Delay(200);

            // Core assertion: no more than 2 concurrent sends were observed.
            Assert.True(maxSeen <= 2, $"max concurrent sends was {maxSeen}, expected <= 2");

            // Release the gate so all blocked sends complete and the remaining
            // tasks flow through in batches of 2.
            gate.SetResult(true);
            await Task.WhenAll(tasks);

            // Final assertion after all 8 tasks completed.
            Assert.True(maxSeen <= 2, $"final max concurrent sends was {maxSeen}, expected <= 2");
        }
    }
}
