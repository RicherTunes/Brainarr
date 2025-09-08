using System.Collections.Generic;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface ISafetyGateService
    {
        List<Recommendation> ApplySafetyGates(
            List<Recommendation> enriched,
            BrainarrSettings settings,
            ReviewQueueService reviewQueue,
            RecommendationHistory history,
            Logger logger,
            NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics metrics,
            CancellationToken ct);
    }
}
