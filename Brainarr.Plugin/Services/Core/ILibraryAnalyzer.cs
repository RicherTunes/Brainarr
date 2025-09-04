using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service interface for analyzing the user's music library.
    /// </summary>
    public interface ILibraryAnalyzer
    {
        /// <summary>
        /// Analyzes the current music library and creates a profile.
        /// </summary>
        /// <returns>Library profile with statistics and preferences</returns>
        LibraryProfile AnalyzeLibrary();
        
        /// <summary>
        /// Builds a prompt for AI recommendations based on the library profile.
        /// </summary>
        /// <param name="profile">Library profile to base recommendations on</param>
        /// <param name="maxRecommendations">Maximum number of recommendations to request</param>
        /// <param name="discoveryMode">Discovery mode for recommendations</param>
        /// <returns>Formatted prompt string for AI providers</returns>
        string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode);

        /// <summary>
        /// Builds a prompt with an option to request artist-only recommendations.
        /// </summary>
        /// <param name="profile">Library profile</param>
        /// <param name="maxRecommendations">Target recommendations</param>
        /// <param name="discoveryMode">Discovery mode</param>
        /// <param name="artistMode">If true, prompt for artists instead of specific albums</param>
        /// <returns>Formatted prompt string for AI providers</returns>
        string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode, bool artistMode);
        
        /// <summary>
        /// Filters recommendations to remove duplicates already in the library.
        /// </summary>
        /// <param name="recommendations">List of recommendations from AI</param>
        /// <returns>Filtered list without duplicates</returns>
        List<ImportListItemInfo> FilterDuplicates(List<ImportListItemInfo> recommendations);

        /// <summary>
        /// Returns all artists currently in the Lidarr library. Used for robust duplicate detection
        /// and to guide iterative top-up to avoid suggesting existing artists.
        /// </summary>
        System.Collections.Generic.List<NzbDrone.Core.Music.Artist> GetAllArtists();

        /// <summary>
        /// Returns all albums currently in the Lidarr library. Used for robust duplicate detection
        /// and to guide iterative top-up to avoid suggesting existing albums.
        /// </summary>
        System.Collections.Generic.List<NzbDrone.Core.Music.Album> GetAllAlbums();
    }
}
