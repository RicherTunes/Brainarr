using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Resilience;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Logging;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class ProviderInvoker : IProviderInvoker
    {
        public async Task<List<Recommendation>> InvokeAsync(IAIProvider provider, string prompt, Logger logger, CancellationToken cancellationToken, string operationLabel = "Provider.GetRecommendations")
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            // Single-attempt policy: provider has its own timeout/retry logic upstream.
            // Migrated from ResiliencePolicy.RunWithRetriesAsync (Wave 16C).
            var policy = RetryPolicyFactory.Create(
                new RetryPolicyOptions { MaxRetries = 1, InitialDelay = TimeSpan.FromMilliseconds(250) },
                new NLogToILoggerAdapter(logger));
            List<Recommendation> result;
            try
            {
                // Prefer CT-aware provider method, gracefully fall back to non-CT overload
                result = await policy.ExecuteAsync(async ct =>
                {
                    List<Recommendation> r = null;
                    var tryCt = true;
                    try
                    {
                        r = await provider.GetRecommendationsAsync(prompt, ct);
                    }
                    catch (NotImplementedException)
                    {
                        tryCt = false;
                    }
                    catch (MissingMethodException)
                    {
                        tryCt = false;
                    }
                    catch (NotSupportedException)
                    {
                        tryCt = false;
                    }
                    if (!tryCt || r == null)
                    {
                        r = await provider.GetRecommendationsAsync(prompt);
                    }
                    return r;
                }, operationLabel, cancellationToken).ConfigureAwait(false);
            }
            catch (RetryExhaustedException)
            {
                result = null;
            }
            return result ?? new List<Recommendation>();
        }
    }
}
