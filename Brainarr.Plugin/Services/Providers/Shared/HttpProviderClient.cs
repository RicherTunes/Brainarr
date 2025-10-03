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

            // Prefer DI-driven IHttpResilience when available; fall back to static policy if not.
            var exec = NzbDrone.Core.ImportLists.Brainarr.Services.Core.ServiceLocator.TryGet<IHttpResilience>();
            if (exec != null)
            {
                return exec.SendAsync(
                    templateRequest: template,
                    send: (req, token) =>
                    {
                        var jsonLocal = JsonConvert.SerializeObject(body);
                        req.SetContent(jsonLocal);
                        return http.ExecuteAsync(req);
                    },
                    origin: origin,
                    logger: logger,
                    cancellationToken: ct,
                    maxRetries: maxRetries,
                    maxConcurrencyPerHost: maxConcurrencyPerHost,
                    retryBudget: null,
                    perRequestTimeout: perRequestTimeout ?? TimeSpan.FromSeconds(30));
            }

            return ResiliencePolicy.WithHttpResilienceAsync(
                templateRequest: template,
                send: (req, token) =>
                {
                    var jsonLocal = JsonConvert.SerializeObject(body);
                    req.SetContent(jsonLocal);
                    return http.ExecuteAsync(req);
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

            var exec = NzbDrone.Core.ImportLists.Brainarr.Services.Core.ServiceLocator.TryGet<IHttpResilience>();
            if (exec != null)
            {
                return exec.SendAsync(
                    templateRequest: template,
                    send: (req, token) => http.ExecuteAsync(req),
                    origin: origin,
                    logger: logger,
                    cancellationToken: ct,
                    maxRetries: maxRetries,
                    maxConcurrencyPerHost: maxConcurrencyPerHost,
                    retryBudget: null,
                    perRequestTimeout: perRequestTimeout ?? TimeSpan.FromSeconds(30));
            }

            return ResiliencePolicy.WithHttpResilienceAsync(
                templateRequest: template,
                send: (req, token) => http.ExecuteAsync(req),
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
