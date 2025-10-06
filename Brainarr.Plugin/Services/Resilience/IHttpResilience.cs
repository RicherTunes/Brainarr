using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    public interface IHttpResilience
    {
        Task<HttpResponse> SendAsync(
            HttpRequest templateRequest,
            Func<HttpRequest, CancellationToken, Task<HttpResponse>> send,
            string origin,
            Logger logger,
            CancellationToken cancellationToken,
            int maxRetries = 3,
            int maxConcurrencyPerHost = 8,
            TimeSpan? retryBudget = null,
            TimeSpan? perRequestTimeout = null);
    }
}
