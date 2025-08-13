using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Builds library context and profiles for AI recommendations
    /// </summary>
    public interface ILibraryContextBuilder
    {
        /// <summary>
        /// Builds a complete library profile from Lidarr data
        /// </summary>
        /// <returns>Library profile containing statistics and sample data</returns>
        Task<LibraryProfile> BuildLibraryProfileAsync();

        /// <summary>
        /// Generates a fingerprint for cache key generation
        /// </summary>
        /// <param name="profile">The library profile to fingerprint</param>
        /// <returns>A unique fingerprint string for the library state</returns>
        string GenerateLibraryFingerprint(LibraryProfile profile);

        /// <summary>
        /// Gets the discovery focus based on the mode
        /// </summary>
        /// <param name="mode">The discovery mode</param>
        /// <returns>A description of the discovery focus</returns>
        string GetDiscoveryFocus(DiscoveryMode mode);
    }

    /// <summary>
    /// Represents a profile of the user's music library
    /// </summary>
    public class LibraryProfile
    {
        public int TotalArtists { get; set; }
        public int TotalAlbums { get; set; }
        public Dictionary<string, int> TopGenres { get; set; }
        public List<string> TopArtists { get; set; }
        public List<string> RecentlyAdded { get; set; }

        public LibraryProfile()
        {
            TopGenres = new Dictionary<string, int>();
            TopArtists = new List<string>();
            RecentlyAdded = new List<string>();
        }
    }

    /// <summary>
    /// Defines the discovery mode for recommendations
    /// </summary>
    public enum DiscoveryMode
    {
        Similar,
        Adjacent,
        Exploratory,
        Balanced
    }
}