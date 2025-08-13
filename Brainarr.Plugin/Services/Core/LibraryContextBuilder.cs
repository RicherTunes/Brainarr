using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Builds library context and profiles for AI recommendations
    /// </summary>
    public class LibraryContextBuilder : ILibraryContextBuilder
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;

        public LibraryContextBuilder(
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger)
        {
            _artistService = artistService;
            _albumService = albumService;
            _logger = logger;
        }

        public async Task<LibraryProfile> BuildLibraryProfileAsync()
        {
            return await Task.Run(() => BuildLibraryProfile());
        }

        private LibraryProfile BuildLibraryProfile()
        {
            try
            {
                // Get ACTUAL data from Lidarr database
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();

                _logger.Debug($"Building library profile from {artists.Count} artists and {albums.Count} albums");

                // Build profile from available data
                var artistAlbumCounts = albums
                    .GroupBy(a => a.ArtistId)
                    .Select(g => new { ArtistId = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(20)
                    .ToList();

                var topArtistNames = artistAlbumCounts
                    .Select(ac => artists.FirstOrDefault(a => a.Id == ac.ArtistId)?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                // Create genre list (simplified since Lidarr doesn't expose genres directly)
                var genreCounts = GenerateGenreDistribution();

                var recentlyAddedArtists = artists
                    .OrderByDescending(a => a.Added)
                    .Take(10)
                    .Select(a => a.Name)
                    .ToList();

                return new LibraryProfile
                {
                    TotalArtists = artists.Count,
                    TotalAlbums = albums.Count,
                    TopGenres = genreCounts,
                    TopArtists = topArtistNames,
                    RecentlyAdded = recentlyAddedArtists
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to get real library data, using fallback: {ex.Message}");
                return GetFallbackLibraryProfile();
            }
        }

        public string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            // Create a detailed fingerprint that changes when library composition changes significantly
            var topArtistsHash = string.Join(",", profile.TopArtists.Take(10)).GetHashCode();
            var topGenresHash = string.Join(",", profile.TopGenres.Take(5).Select(g => g.Key)).GetHashCode();
            var recentlyAddedHash = string.Join(",", profile.RecentlyAdded.Take(5)).GetHashCode();
            
            // Use absolute values to ensure positive numbers
            return $"{profile.TotalArtists}_{profile.TotalAlbums}_{Math.Abs(topArtistsHash)}_{Math.Abs(topGenresHash)}_{Math.Abs(recentlyAddedHash)}";
        }

        public string GetDiscoveryFocus(DiscoveryMode mode)
        {
            return mode switch
            {
                DiscoveryMode.Similar => "artists very similar to the library",
                DiscoveryMode.Adjacent => "artists in related genres",
                DiscoveryMode.Exploratory => "new genres and styles to explore",
                _ => "balanced recommendations"
            };
        }

        private Dictionary<string, int> GenerateGenreDistribution()
        {
            // Since Lidarr doesn't expose genres directly, use a reasonable default distribution
            var genreCounts = new Dictionary<string, int>();
            var fallbackGenres = BrainarrConstants.FallbackGenres;
            
            for (int i = 0; i < Math.Min(5, fallbackGenres.Length); i++)
            {
                genreCounts[fallbackGenres[i]] = 20 - (i * 3);
            }
            
            return genreCounts;
        }

        private LibraryProfile GetFallbackLibraryProfile()
        {
            // Fallback profile when Lidarr services aren't available
            return new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = new Dictionary<string, int> 
                { 
                    { "Rock", 30 }, 
                    { "Electronic", 20 }, 
                    { "Jazz", 15 },
                    { "Hip Hop", 10 },
                    { "Classical", 5 }
                },
                TopArtists = new List<string> 
                { 
                    "Radiohead", 
                    "Pink Floyd", 
                    "Miles Davis",
                    "The Beatles",
                    "Led Zeppelin"
                },
                RecentlyAdded = new List<string>()
            };
        }
    }
}