using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IRecommendationService
    {
        Task<IList<ImportListItemInfo>> GetRecommendationsAsync(
            string provider,
            int maxRecommendations,
            string libraryFingerprint);
        
        Task<IList<ImportListItemInfo>> GenerateRecommendationsAsync(
            IAIProvider provider,
            int maxRecommendations,
            LibraryProfile libraryProfile);
    }
}