using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IRecommendationOrchestrator
    {
        Task<List<ImportListItemInfo>> GetRecommendationsAsync(
            BrainarrSettings settings, 
            LibraryProfile profile);
        
        void InitializeProvider(BrainarrSettings settings);
    }
}