using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    public interface ICollectionDepthAnalyzer
    {
        CollectionDepthAnalysis AnalyzeDepth(List<Artist> artists, List<Album> albums);
        CollectionQualityMetrics AnalyzeQuality(List<Artist> artists, List<Album> albums);
    }
}