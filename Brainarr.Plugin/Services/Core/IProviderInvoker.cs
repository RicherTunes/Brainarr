using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IProviderInvoker
    {
        Task<List<Recommendation>> InvokeAsync(IAIProvider provider, string prompt, Logger logger, CancellationToken cancellationToken, string operationLabel = "Provider.GetRecommendations");
    }
}
