using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    public interface ILibraryProfileBuilder
    {
        Task<LibraryProfile> BuildProfileAsync(
            GenreAnalysis genres,
            TemporalAnalysis temporal,
            CollectionDepthAnalysis depth,
            CollectionQualityMetrics quality,
            List<Artist> artists,
            List<Album> albums);
            
        string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode);
    }
}