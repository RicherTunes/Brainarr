using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service for analyzing the user's music library and generating recommendations.
    /// Extracts library analysis logic from the main import list class.
    /// </summary>
    public class LibraryAnalyzer : ILibraryAnalyzer
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;

        public LibraryAnalyzer(IArtistService artistService, IAlbumService albumService, Logger logger)
        {
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Analyzes the current music library and creates a profile.
        /// </summary>
        public LibraryProfile AnalyzeLibrary()
        {
            try
            {
                // Get ACTUAL data from Lidarr database
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();

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
                var genreCounts = new Dictionary<string, int>();
                for (int i = 0; i < Math.Min(5, BrainarrConstants.FallbackGenres.Length); i++)
                {
                    genreCounts[BrainarrConstants.FallbackGenres[i]] = 20 - (i * 3);
                }

                return new LibraryProfile
                {
                    TotalArtists = artists.Count,
                    TotalAlbums = albums.Count,
                    TopGenres = genreCounts,
                    TopArtists = topArtistNames,
                    RecentlyAdded = artists
                        .OrderByDescending(a => a.Added)
                        .Take(10)
                        .Select(a => a.Name)
                        .ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to analyze library, using fallback data: {ex.Message}");

                // Fallback to sample data if Lidarr services aren't available
                return GetFallbackProfile();
            }
        }

        /// <summary>
        /// Builds a prompt for AI recommendations based on the library profile.
        /// </summary>
        public string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode)
        {
            var discoveryFocus = GetDiscoveryFocus(discoveryMode);

            var prompt = $@"Based on this music library, recommend {maxRecommendations} new albums to discover:

Library: {profile.TotalArtists} artists, {profile.TotalAlbums} albums
Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", profile.TopArtists.Take(10))}

Return a JSON array with exactly {maxRecommendations} recommendations.
Each item must have: artist, album, genre, confidence (0.0-1.0), reason (brief).

Focus on: {discoveryFocus}

Example format:
[
  {{""artist"": ""Artist Name"", ""album"": ""Album Title"", ""genre"": ""Genre"", ""confidence"": 0.8, ""reason"": ""Similar to your jazz collection""}}
]";

            return prompt;
        }

        /// <summary>
        /// Filters recommendations to remove duplicates already in the library.
        /// </summary>
        public List<ImportListItemInfo> FilterDuplicates(List<ImportListItemInfo> recommendations)
        {
            // Get existing albums for duplicate detection
            var existingAlbums = _albumService.GetAllAlbums()
                .Select(a => $"{a.ArtistMetadataId}_{a.Title?.ToLower()}")
                .ToHashSet();

            var uniqueItems = recommendations
                .Where(i => !existingAlbums.Contains($"{i.Artist?.ToLower()}_{i.Album?.ToLower()}"))
                .ToList();

            if (uniqueItems.Count < recommendations.Count)
            {
                _logger.Info($"Filtered out {recommendations.Count - uniqueItems.Count} duplicate recommendations");
            }

            return uniqueItems;
        }

        private LibraryProfile GetFallbackProfile()
        {
            return new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = new Dictionary<string, int>
                {
                    { "Rock", 30 },
                    { "Electronic", 20 },
                    { "Jazz", 15 }
                },
                TopArtists = new List<string>
                {
                    "Radiohead",
                    "Pink Floyd",
                    "Miles Davis"
                },
                RecentlyAdded = new List<string>()
            };
        }

        private string GetDiscoveryFocus(DiscoveryMode mode)
        {
            return mode switch
            {
                DiscoveryMode.Similar => "artists very similar to the library",
                DiscoveryMode.Adjacent => "artists in related genres",
                DiscoveryMode.Exploratory => "new genres and styles to explore",
                _ => "balanced recommendations"
            };
        }
    }
}