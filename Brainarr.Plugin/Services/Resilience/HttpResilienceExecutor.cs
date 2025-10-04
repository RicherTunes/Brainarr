using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Performance;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    public sealed class HttpResilienceExecutor : IHttpResilience
    {
        private readonly IUniversalAdaptiveRateLimiter _limiter;

        public HttpResilienceExecutor(IUniversalAdaptiveRateLimiter limiter)
        {
            _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        }

        public Task<HttpResponse> SendAsync(
            HttpRequest templateRequest,
            Func<HttpRequest, CancellationToken, Task<HttpResponse>> send,
            string origin,
            Logger logger,
            CancellationToken cancellationToken,
            int maxRetries = 3,
            int maxConcurrencyPerHost = 8,
            TimeSpan? retryBudget = null,
            TimeSpan? perRequestTimeout = null)
        {
            return ResiliencePolicy.WithHttpResilienceAsync(
                templateRequest: templateRequest,
                send: send,
                origin: origin,
                logger: logger,
                cancellationToken: cancellationToken,
                maxRetries: maxRetries,
                shouldRetry: null,
                limiter: _limiter,
                retryBudget: retryBudget,
                maxConcurrencyPerHost: maxConcurrencyPerHost,
                perRequestTimeout: perRequestTimeout);
        }
    }
}
