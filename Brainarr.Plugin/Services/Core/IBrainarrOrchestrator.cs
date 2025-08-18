using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IBrainarrOrchestrator
    {
        IList<ImportListItemInfo> FetchRecommendations(BrainarrSettings settings);
        Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings);
        void InitializeProvider(BrainarrSettings settings);
        void UpdateProviderConfiguration(BrainarrSettings settings);
        bool IsProviderHealthy();
        string GetProviderStatus();
    }
}