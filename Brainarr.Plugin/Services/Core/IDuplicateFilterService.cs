using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service for filtering duplicate recommendations against the user's existing library.
    /// Extracted from LibraryAnalyzer to separate library analysis from duplicate detection.
    /// </summary>
    public interface IDuplicateFilterService
    {
        List<ImportListItemInfo> FilterDuplicates(List<ImportListItemInfo> recommendations);
        List<Recommendation> FilterExistingRecommendations(List<Recommendation> recommendations, bool artistMode);
    }
}
