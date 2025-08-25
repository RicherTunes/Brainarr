using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    public interface ILibraryMetadataAnalyzer
    {
        Task<GenreAnalysis> AnalyzeGenresAsync(List<Artist> artists, List<Album> albums);
        Dictionary<string, double> CalculateGenreDistribution(Dictionary<string, int> genres);
    }
}