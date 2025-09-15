using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class ProviderInvoker : IProviderInvoker
    {
        public async Task<List<Recommendation>> InvokeAsync(IAIProvider provider, string prompt, Logger logger, CancellationToken cancellationToken, string operationLabel = "Provider.GetRecommendations")
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            // Prefer CT-aware provider method, gracefully fall back to non-CT overload
            var result = await ResiliencePolicy.RunWithRetriesAsync<List<Recommendation>>(
                async ct =>
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
                },
                logger,
                operationName: operationLabel,
                maxAttempts: 1,
                initialDelay: TimeSpan.FromMilliseconds(250),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result ?? new List<Recommendation>();
        }
    }
}
