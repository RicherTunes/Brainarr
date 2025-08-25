using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    public interface ITemporalAnalyzer
    {
        TemporalAnalysis AnalyzeTemporalPatterns(List<Album> albums);
    }
}