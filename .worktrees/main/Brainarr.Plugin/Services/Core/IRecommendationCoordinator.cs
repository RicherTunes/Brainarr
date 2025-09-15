using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IRecommendationCoordinator
    {
        Task<List<ImportListItemInfo>> RunAsync(
            BrainarrSettings settings,
            Func<LibraryProfile, CancellationToken, Task<List<Recommendation>>> fetchRecommendations,
            ReviewQueueService reviewQueue,
            IAIProvider currentProvider,
            ILibraryAwarePromptBuilder promptBuilder,
            CancellationToken cancellationToken);
    }
}
