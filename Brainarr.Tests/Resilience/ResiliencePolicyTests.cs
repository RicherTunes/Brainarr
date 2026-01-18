using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;
using Xunit;

namespace Brainarr.Tests.Resilience
{
    /// <summary>
    /// Tests for <see cref="ResiliencePolicy"/>. Uses <see cref="IClassFixture{T}"/>
    /// to reset static state before each test class run, preventing cross-test pollution.
    /// </summary>
    public class ResiliencePolicyTests : IDisposable
    {
        private static Logger L => LogManager.GetCurrentClassLogger();

        public ResiliencePolicyTests()
        {
            // Reset static state before each test to prevent pollution from other tests
            // that may have configured the adaptive rate limiter.
            ResiliencePolicy.ResetForTesting();
        }

        public void Dispose()
        {
            // Clean up after tests
            ResiliencePolicy.ResetForTesting();
        }

        [Fact]
        public async Task RunWithRetriesAsync_succeeds_on_second_attempt()
        {
            int attempts = 0;
            var result = await ResiliencePolicy.RunWithRetriesAsync<int>(async ct =>
            {
                attempts++;
                await Task.Yield();
                if (attempts == 1) throw new InvalidOperationException("boom");
                return 99;
            }, L, "op.retry", maxAttempts: 3, initialDelay: TimeSpan.Zero, cancellationToken: CancellationToken.None);

            attempts.Should().Be(2);
            result.Should().Be(99);
        }

        [Fact]
        public async Task WithHttpResilienceAsync_retries_transient_then_returns_ok()
        {
            int attempts = 0;
            var request = new HttpRequest("http://example") { Method = System.Net.Http.HttpMethod.Post };
            var headers = new HttpHeader();

            var resp = await ResiliencePolicy.WithHttpResilienceAsync(
                request,
                async (req, ct) =>
                {
                    attempts++;
                    await Task.Yield();
                    if (attempts < 2)
                    {
                        return new HttpResponse(req, headers, "fail", HttpStatusCode.InternalServerError);
                    }
                    return new HttpResponse(req, headers, "ok", HttpStatusCode.OK);
                },
                origin: "test",
                logger: L,
                cancellationToken: CancellationToken.None,
                maxRetries: 3);

            attempts.Should().BeGreaterThanOrEqualTo(2);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            resp.Content.Should().Be("ok");
        }

        [Fact]
        public async Task WithHttpResilienceAsync_returns_last_response_after_exhausting_retries()
        {
            int attempts = 0;
            var request = new HttpRequest("http://example") { Method = System.Net.Http.HttpMethod.Post };
            var headers = new HttpHeader();

            var resp = await ResiliencePolicy.WithHttpResilienceAsync(
                request,
                async (req, ct) =>
                {
                    attempts++;
                    await Task.Yield();
                    return new HttpResponse(req, headers, "fail", HttpStatusCode.BadGateway);
                },
                origin: "test",
                logger: L,
                cancellationToken: CancellationToken.None,
                maxRetries: 2);

            attempts.Should().Be(2);
            resp.Should().NotBeNull();
            resp.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        }
    }
}
