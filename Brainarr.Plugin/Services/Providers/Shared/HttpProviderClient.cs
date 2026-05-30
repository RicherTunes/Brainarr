using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Newtonsoft.Json;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared
{
    internal static class HttpProviderClient
    {
        /// <summary>
        /// Executes an HTTP request while honouring the provided <paramref name="cancellationToken"/>.
        /// </summary>
        /// <remarks>
        /// Lidarr's <see cref="IHttpClient.ExecuteAsync"/> does not accept a <see cref="CancellationToken"/>,
        /// and <see cref="HttpRequest"/> exposes no <c>Cancel()</c> method. The bridge is
        /// <c>Task.WhenAny</c>: if the token fires first we throw <see cref="OperationCanceledException"/>;
        /// the underlying HTTP Task is then abandoned (Lidarr's HttpClient will eventually time out
        /// per <c>request.RequestTimeout</c>, but the calling thread is unblocked immediately).
        /// </remarks>
        internal static async Task<HttpResponse> ExecuteWithCt(
            IHttpClient client,
            HttpRequest request,
            CancellationToken cancellationToken)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (request == null) throw new ArgumentNullException(nameof(request));

            cancellationToken.ThrowIfCancellationRequested();

            var executeTask = client.ExecuteAsync(request);

            // IHttpClient.ExecuteAsync has no CancellationToken overload, so we race
            // the execute task against a task that completes when the token fires.
            var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);

            var winner = await Task.WhenAny(executeTask, cancelTask).ConfigureAwait(false);

            // Prefer a completed HTTP call: if the response already arrived (or the call faulted),
            // honor it even when the token ALSO fired. Task.WhenAny may return cancelTask although
            // executeTask is already complete (both ready at once), and a cancellation that lands
            // after the response is moot — discarding a completed response would be wrong. Only treat
            // it as cancelled when the response genuinely hasn't arrived yet.
            if (winner == cancelTask && !executeTask.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Fallback in case ThrowIfCancellationRequested doesn't throw (shouldn't happen).
                throw new OperationCanceledException(cancellationToken);
            }

            // executeTask completed (success or fault) — propagate its result/exception.
            return await executeTask.ConfigureAwait(false);
        }

        public static Task<HttpResponse> SendJsonAsync(
            IHttpClient http,
            HttpRequest template,
            object body,
            string origin,
            Logger logger,
            CancellationToken ct,
            int maxRetries = 2,
            int maxConcurrencyPerHost = 8,
            TimeSpan? perRequestTimeout = null)
        {
            if (http == null) throw new ArgumentNullException(nameof(http));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            template.SuppressHttpError = true;
            if (perRequestTimeout.HasValue)
            {
                template.RequestTimeout = perRequestTimeout.Value;
            }

            return ResiliencePolicy.WithHttpResilienceAsync(
                templateRequest: template,
                send: (req, token) =>
                {
                    var jsonLocal = JsonConvert.SerializeObject(body);
                    req.SetContent(jsonLocal);
                    return ExecuteWithCt(http, req, token);
                },
                origin: origin,
                logger: logger,
                cancellationToken: ct,
                maxRetries: maxRetries,
                shouldRetry: null,
                limiter: null,
                retryBudget: null,
                maxConcurrencyPerHost: maxConcurrencyPerHost,
                perRequestTimeout: perRequestTimeout ?? TimeSpan.FromSeconds(30));
        }

        public static Task<HttpResponse> SendGetAsync(
            IHttpClient http,
            HttpRequest template,
            string origin,
            Logger logger,
            CancellationToken ct,
            int maxRetries = 2,
            int maxConcurrencyPerHost = 8,
            TimeSpan? perRequestTimeout = null)
        {
            template.SuppressHttpError = true;
            if (perRequestTimeout.HasValue)
            {
                template.RequestTimeout = perRequestTimeout.Value;
            }

            return ResiliencePolicy.WithHttpResilienceAsync(
                templateRequest: template,
                send: (req, token) => ExecuteWithCt(http, req, token),
                origin: origin,
                logger: logger,
                cancellationToken: ct,
                maxRetries: maxRetries,
                shouldRetry: null,
                limiter: null,
                retryBudget: null,
                maxConcurrencyPerHost: maxConcurrencyPerHost,
                perRequestTimeout: perRequestTimeout ?? TimeSpan.FromSeconds(30));
        }
    }
}
