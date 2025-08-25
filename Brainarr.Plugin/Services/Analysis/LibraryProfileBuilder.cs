using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    public class LibraryProfileBuilder : ILibraryProfileBuilder
    {
        private readonly Logger _logger;

        public LibraryProfileBuilder(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<LibraryProfile> BuildProfileAsync(
            GenreAnalysis genres,
            TemporalAnalysis temporal,
            CollectionDepthAnalysis depth,
            CollectionQualityMetrics quality,
            List<Artist> artists,
            List<Album> albums)
        {
            return await Task.Run(() =>
            {
                var profile = new LibraryProfile
                {
                    TotalArtists = artists.Count,
                    TotalAlbums = albums.Count,
                    TopGenres = genres.GenreCounts,
                    TopArtists = GetTopArtistsByAlbumCount(artists, albums),
                    RecentlyAdded = GetRecentlyAddedArtists(artists)
                };

                // Populate comprehensive metadata
                profile.Metadata["GenreDistribution"] = genres.Distribution;
                profile.Metadata["ReleaseDecades"] = temporal.ReleaseDecades;
                profile.Metadata["PreferredEras"] = temporal.PreferredEras;
                profile.Metadata["NewReleaseRatio"] = temporal.NewReleaseRatio;
                profile.Metadata["MonitoredRatio"] = quality.MonitoredRatio;
                profile.Metadata["CollectionCompleteness"] = quality.Completeness;
                profile.Metadata["AverageAlbumsPerArtist"] = quality.AverageAlbumsPerArtist;
                profile.Metadata["AlbumTypes"] = AnalyzeAlbumTypes(albums);
                profile.Metadata["SecondaryTypes"] = ExtractSecondaryTypes(albums);
                profile.Metadata["DiscoveryTrend"] = DetermineDiscoveryTrend(artists);
                profile.Metadata["CollectionSize"] = GetCollectionSize(artists.Count, albums.Count);
                profile.Metadata["CollectionFocus"] = DetermineCollectionFocus(genres, temporal);
                profile.Metadata["CollectionStyle"] = depth.CollectionStyle;
                profile.Metadata["CompletionistScore"] = depth.CompletionistScore;
                profile.Metadata["PreferredAlbumType"] = depth.PreferredAlbumType;
                profile.Metadata["TopCollectedArtists"] = depth.TopCollectedArtists;

                return profile;
            });
        }

        public string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode)
        {
            var discoveryFocus = GetDiscoveryFocus(discoveryMode);
            var collectionContext = BuildCollectionContext(profile);
            var preferenceContext = BuildPreferenceContext(profile);
            var qualityContext = BuildQualityContext(profile);

            return $@"Analyze this comprehensive music library profile and recommend {maxRecommendations} new albums:

📊 COLLECTION OVERVIEW:
{collectionContext}

🎵 MUSICAL PREFERENCES:
{preferenceContext}

📈 COLLECTION QUALITY:
{qualityContext}

🎯 RECOMMENDATION REQUIREMENTS:
• Provide exactly {maxRecommendations} album recommendations
• Focus on: {discoveryFocus}
• Match the collection's character
• Consider the discovery pattern

Return a JSON array with this exact format:
[
  {{
    ""artist"": ""Artist Name"",
    ""album"": ""Album Title"",
    ""genre"": ""Primary Genre"",
    ""year"": 2024,
    ""confidence"": 0.85,
    ""reason"": ""Matches your progressive rock collection with modern production""
  }}
]

Ensure recommendations are:
1. NOT already in the collection
2. Actually released albums (no fictional/AI hallucinations)
3. Diverse within the specified focus area
4. High quality matches for this specific library profile";
        }

        private List<string> GetTopArtistsByAlbumCount(List<Artist> artists, List<Album> albums)
        {
            return albums
                .GroupBy(a => a.ArtistId)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .Select(g => artists.FirstOrDefault(a => a.Id == g.Key)?.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }

        private List<string> GetRecentlyAddedArtists(List<Artist> artists)
        {
            return artists
                .OrderByDescending(a => a.Added)
                .Take(10)
                .Select(a => a.Name)
                .ToList();
        }

        private Dictionary<string, int> AnalyzeAlbumTypes(List<Album> albums)
        {
            return albums
                .Where(a => !string.IsNullOrEmpty(a.AlbumType))
                .GroupBy(a => a.AlbumType)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());
        }

        private List<string> ExtractSecondaryTypes(List<Album> albums)
        {
            return albums
                .Where(a => a.SecondaryTypes?.Any() == true)
                .SelectMany(a => a.SecondaryTypes)
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key.ToString())
                .ToList();
        }

        private string DetermineDiscoveryTrend(List<Artist> artists)
        {
            var recentThreshold = DateTime.UtcNow.AddMonths(-6);
            var recentAdditions = artists.Count(a => a.Added > recentThreshold);
            var totalArtists = artists.Count;

            if (totalArtists == 0) return "new collection";

            var ratio = (double)recentAdditions / totalArtists;

            if (ratio > 0.3) return "rapidly expanding";
            if (ratio > 0.15) return "actively growing";
            if (ratio > 0.05) return "steady growth";
            return "stable collection";
        }

        private string GetCollectionSize(int artistCount, int albumCount)
        {
            if (artistCount < 50) return "starter";
            if (artistCount < 200) return "growing";
            if (artistCount < 500) return "established";
            if (artistCount < 1000) return "extensive";
            return "massive";
        }

        private string DetermineCollectionFocus(GenreAnalysis genres, TemporalAnalysis temporal)
        {
            var genreFocus = "eclectic";
            if (genres.GenreCounts.Any())
            {
                var topGenreRatio = (double)genres.GenreCounts.First().Value / genres.GenreCounts.Sum(g => g.Value);
                if (topGenreRatio > 0.5) genreFocus = "specialized";
                else if (topGenreRatio > 0.3) genreFocus = "focused";
            }

            var temporalFocus = temporal.NewReleaseRatio > 0.3 ? "current" :
                               temporal.PreferredEras.Contains("Classic") ? "classic" : "mixed";

            return $"{genreFocus}-{temporalFocus}";
        }

        private string GetDiscoveryFocus(DiscoveryMode mode)
        {
            return mode switch
            {
                DiscoveryMode.Similar => "artists very similar to your existing collection",
                DiscoveryMode.Adjacent => "artists in related but unexplored genres",
                DiscoveryMode.Exploratory => "new genres and styles outside your comfort zone",
                _ => "balanced recommendations across familiar and new territories"
            };
        }

        private string BuildCollectionContext(LibraryProfile profile)
        {
            var genreDistribution = profile.Metadata.ContainsKey("GenreDistribution")
                ? profile.Metadata["GenreDistribution"] as Dictionary<string, double>
                : null;

            var genreList = genreDistribution?.Any() == true
                ? string.Join(", ", genreDistribution.Take(5).Select(g => $"{g.Key} ({g.Value}%)"))
                : string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key}"));

            var collectionSize = profile.Metadata.GetValueOrDefault("CollectionSize", "established").ToString();
            var releaseDecades = profile.Metadata.ContainsKey("ReleaseDecades")
                ? string.Join(", ", (List<string>)profile.Metadata["ReleaseDecades"])
                : "mixed eras";
            var collectionFocus = profile.Metadata.GetValueOrDefault("CollectionFocus", "general").ToString();

            return $@"• Size: {collectionSize} ({profile.TotalArtists} artists, {profile.TotalAlbums} albums)
• Genres: {genreList}
• Era focus: {releaseDecades}
• Collection type: {collectionFocus}";
        }

        private string BuildPreferenceContext(LibraryProfile profile)
        {
            var albumTypes = profile.Metadata.ContainsKey("AlbumTypes")
                ? profile.Metadata["AlbumTypes"] as Dictionary<string, int>
                : null;

            var albumTypeStr = albumTypes?.Any() == true
                ? string.Join(", ", albumTypes.Take(3).Select(t => $"{t.Key} ({t.Value})"))
                : "Mixed album types";

            var topArtists = string.Join(", ", profile.TopArtists.Take(8));
            var newReleaseRatio = profile.Metadata.GetValueOrDefault("NewReleaseRatio", 0.1);
            var discoveryTrend = profile.Metadata.GetValueOrDefault("DiscoveryTrend", "stable collection").ToString();

            return $@"• Top artists: {topArtists}
• Album types: {albumTypeStr}
• New release interest: {newReleaseRatio:P0}
• Discovery trend: {discoveryTrend}";
        }

        private string BuildQualityContext(LibraryProfile profile)
        {
            var completeness = Convert.ToDouble(profile.Metadata.GetValueOrDefault("CollectionCompleteness", 0.7));
            var quality = completeness > 0.8 ? "Very High" :
                         completeness > 0.6 ? "High" :
                         completeness > 0.4 ? "Moderate" : "Building";

            var monitoredRatio = Convert.ToDouble(profile.Metadata.GetValueOrDefault("MonitoredRatio", 0.8));
            var avgAlbumsPerArtist = Convert.ToDouble(profile.Metadata.GetValueOrDefault("AverageAlbumsPerArtist", 
                (double)profile.TotalAlbums / Math.Max(1, profile.TotalArtists)));

            return $@"• Collection quality: {quality} ({completeness:P0} complete)
• Monitoring ratio: {monitoredRatio:P0} actively tracked
• Average depth: {avgAlbumsPerArtist:F1} albums per artist";
        }
    }
}