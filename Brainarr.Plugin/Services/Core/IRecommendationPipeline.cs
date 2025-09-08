using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IRecommendationPipeline
    {
        Task<List<ImportListItemInfo>> ProcessAsync(
            BrainarrSettings settings,
            List<Recommendation> recommendations,
            LibraryProfile libraryProfile,
            ReviewQueueService reviewQueue,
            IAIProvider currentProvider,
            ILibraryAwarePromptBuilder promptBuilder,
            CancellationToken cancellationToken);
    }
}
