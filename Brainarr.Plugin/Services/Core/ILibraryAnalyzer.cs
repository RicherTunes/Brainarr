using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Models;

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
