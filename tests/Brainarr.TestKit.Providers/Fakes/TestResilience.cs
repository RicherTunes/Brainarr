using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace Brainarr.TestKit.Providers.Fakes
{
    public sealed class TestResilience : IHttpResilience
    {
        public int Calls { get; private set; }
        public TimeSpan? PerRequestTimeout { get; private set; }
        public int MaxRetries { get; private set; }
        public int MaxConcurrencyPerHost { get; private set; }

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
            Calls++;
            MaxRetries = maxRetries;
            MaxConcurrencyPerHost = maxConcurrencyPerHost;
            PerRequestTimeout = perRequestTimeout;
            // Forward call without retries to keep tests deterministic
            return send(templateRequest, cancellationToken);
        }
    }
}
