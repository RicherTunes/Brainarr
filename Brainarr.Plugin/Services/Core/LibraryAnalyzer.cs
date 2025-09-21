using System;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public sealed class LibraryAnalyzerOptions
    {
        public bool EnableParallelStyleContext { get; init; } = true;
        public int ParallelizationThreshold { get; init; } = 64;
        public int? MaxDegreeOfParallelism { get; init; } = null;
    }
    /// <summary>
    /// Service for analyzing the user's music library with rich metadata extraction.
    /// Provides comprehensive library profiling for intelligent AI recommendations.
    /// </summary>
    public class LibraryAnalyzer : ILibraryAnalyzer
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;
        private readonly IStyleCatalogService _styleCatalog;
        private readonly LibraryAnalyzerOptions _options;

        public LibraryAnalyzer(IArtistService artistService, IAlbumService albumService, IStyleCatalogService styleCatalog, Logger logger, LibraryAnalyzerOptions? options = null)
        {
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            _styleCatalog = styleCatalog ?? throw new ArgumentNullException(nameof(styleCatalog));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? BuildDefaultOptions();
        }

        public List<Artist> GetAllArtists()
        {
            return _artistService.GetAllArtists();
        }

        public List<Album> GetAllAlbums()
        {
            return _albumService.GetAllAlbums();
        }

        /// <summary>
        /// Analyzes the current music library with comprehensive metadata extraction.
        /// </summary>
        public LibraryProfile AnalyzeLibrary()
        {
            try
            {
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();

                // Extract real genre data from metadata
                var realGenres = ExtractRealGenres(artists, albums);

                // Analyze temporal patterns (release dates, eras)
                var temporalAnalysis = AnalyzeTemporalPatterns(albums);

                // Calculate collection quality metrics
                var qualityMetrics = AnalyzeCollectionQuality(artists, albums);

                // Analyze user preferences (album types, monitoring, tags)
                var preferences = AnalyzeUserPreferences(artists, albums);

                // Extract secondary album types
                var secondaryTypes = ExtractSecondaryTypes(albums);

                // Get top artists by album count
                var topArtists = GetTopArtistsByAlbumCount(artists, albums);

                // Analyze collection depth for completionist behavior
                var collectionDepth = AnalyzeCollectionDepth(artists, albums);

                // Build comprehensive profile
                var profile = new LibraryProfile
                {
                    TotalArtists = artists.Count,
                    TotalAlbums = albums.Count,
                    TopGenres = realGenres.Any() ? realGenres : GetFallbackGenres(),
                    TopArtists = topArtists,
                    RecentlyAdded = GetRecentlyAddedArtists(artists),
                    StyleContext = BuildStyleContext(artists, albums)
                };

                // Store additional metadata for enhanced prompt generation
                profile.Metadata["GenreDistribution"] = CalculateGenreDistribution(realGenres);
                profile.Metadata["ReleaseDecades"] = temporalAnalysis.ReleaseDecades;
                profile.Metadata["PreferredEras"] = temporalAnalysis.PreferredEras;
                profile.Metadata["NewReleaseRatio"] = temporalAnalysis.NewReleaseRatio;
                profile.Metadata["MonitoredRatio"] = qualityMetrics.MonitoredRatio;
                profile.Metadata["CollectionCompleteness"] = qualityMetrics.Completeness;
                profile.Metadata["AverageAlbumsPerArtist"] = qualityMetrics.AverageAlbumsPerArtist;
                profile.Metadata["AlbumTypes"] = preferences.AlbumTypes;
                profile.Metadata["SecondaryTypes"] = secondaryTypes;
                profile.Metadata["DiscoveryTrend"] = preferences.DiscoveryTrend;
                profile.Metadata["CollectionSize"] = GetCollectionSize(artists.Count, albums.Count);
                profile.Metadata["CollectionFocus"] = DetermineCollectionFocus(realGenres, temporalAnalysis);

                // Enhanced collection shape metadata
                profile.Metadata["CollectionStyle"] = collectionDepth.CollectionStyle;
                profile.Metadata["CompletionistScore"] = collectionDepth.CompletionistScore;
                profile.Metadata["PreferredAlbumType"] = collectionDepth.PreferredAlbumType;
                profile.Metadata["TopCollectedArtists"] = collectionDepth.TopCollectedArtists;
                // Also store human-readable top collected artist names with album counts for prompt building
                try
                {
                    var nameById = artists.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First().Name);
                    var topCollectedNameCounts = collectionDepth.TopCollectedArtists
                        .Select(a => new KeyValuePair<string, int>(
                            nameById.TryGetValue(a.ArtistId, out var nm) ? nm : $"Artist {a.ArtistId}",
                            a.AlbumCount))
                        .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                    profile.Metadata["TopCollectedArtistNames"] = topCollectedNameCounts;
                }
                catch { /* non-fatal */ }

                return profile;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to analyze library with enhanced metadata: {ex.Message}");
                return GetFallbackProfile();
            }
        }

        private Dictionary<string, int> ExtractRealGenres(List<Artist> artists, List<Album> albums)
        {
            var genres = new List<string>();

            // Extract genres from artist metadata
            foreach (var artist in artists.Where(a => a.Metadata?.Value != null))
            {
                if (artist.Metadata.Value.Genres?.Any() == true)
                {
                    genres.AddRange(artist.Metadata.Value.Genres);
                }
            }

            // Extract genres from album metadata
            foreach (var album in albums.Where(a => a.Genres?.Any() == true))
            {
                genres.AddRange(album.Genres);
            }

            // If no genres found, try to extract from overviews
            if (!genres.Any())
            {
                _logger.Debug("No direct genre data found, using intelligent fallback");
                var overviewGenres = ExtractGenresFromOverviews(artists, albums);
                if (overviewGenres.Any())
                {
                    genres.AddRange(overviewGenres);
                }
            }

            // Group and count genres
            return genres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .GroupBy(g => g.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        private List<string> ExtractGenresFromOverviews(List<Artist> artists, List<Album> albums)
        {
            var commonGenres = new[]
            {
                "rock", "pop", "jazz", "electronic", "hip hop", "r&b", "soul", "funk",
                "metal", "punk", "indie", "alternative", "country", "folk", "blues",
                "classical", "reggae", "dance", "house", "techno", "ambient", "experimental"
            };

            var extractedGenres = new List<string>();

            foreach (var artist in artists.Where(a => a.Metadata?.Value?.Overview != null))
            {
                var overview = artist.Metadata.Value.Overview.ToLower();
                foreach (var genre in commonGenres)
                {
                    if (overview.Contains(genre))
                    {
                        extractedGenres.Add(char.ToUpper(genre[0]) + genre.Substring(1));
                    }
                }
            }

            return extractedGenres;
        }

        private (List<string> ReleaseDecades, List<string> PreferredEras, double NewReleaseRatio)
            AnalyzeTemporalPatterns(List<Album> albums)
        {
            var albumsWithDates = albums.Where(a => a.ReleaseDate.HasValue).ToList();

            if (!albumsWithDates.Any())
            {
                return (new List<string>(), new List<string>(), 0.0);
            }

            // Group by decade
            var decadeGroups = albumsWithDates
                .GroupBy(a => (a.ReleaseDate.Value.Year / 10) * 10)
                .OrderByDescending(g => g.Count())
                .ToList();

            var releaseDecades = decadeGroups
                .Take(3)
                .Select(g => $"{g.Key}s")
                .ToList();

            // Determine preferred eras
            var preferredEras = new List<string>();
            foreach (var decade in decadeGroups.Take(2))
            {
                var year = decade.Key;
                if (year < 1970) preferredEras.Add("Classic");
                else if (year < 1990) preferredEras.Add("Golden Age");
                else if (year < 2010) preferredEras.Add("Modern");
                else preferredEras.Add("Contemporary");
            }
            preferredEras = preferredEras.Distinct().ToList();

            // Calculate new release ratio (albums from last 2 years)
            var recentThreshold = DateTime.UtcNow.AddYears(-2);
            var recentAlbums = albumsWithDates.Count(a => a.ReleaseDate.Value > recentThreshold);
            var newReleaseRatio = albumsWithDates.Any() ? (double)recentAlbums / albumsWithDates.Count : 0.0;

            return (releaseDecades, preferredEras, newReleaseRatio);
        }

        private (double MonitoredRatio, double Completeness, double AverageAlbumsPerArtist)
            AnalyzeCollectionQuality(List<Artist> artists, List<Album> albums)
        {
            // Calculate monitoring ratio
            var monitoredArtists = artists.Count(a => a.Monitored);
            var monitoredAlbums = albums.Count(a => a.Monitored);
            var monitoredRatio = artists.Any() ? (double)monitoredArtists / artists.Count : 0.0;

            // Calculate completeness (ratio of monitored albums to total)
            var completeness = albums.Any() ? (double)monitoredAlbums / albums.Count : 0.0;

            // Calculate average albums per artist
            var averageAlbumsPerArtist = artists.Any() ? (double)albums.Count / artists.Count : 0.0;

            // Enhanced: Analyze collection depth patterns
            var collectionDepth = AnalyzeCollectionDepth(artists, albums);

            return (monitoredRatio, completeness, averageAlbumsPerArtist);
        }

        private CollectionDepthAnalysis AnalyzeCollectionDepth(List<Artist> artists, List<Album> albums)
        {
            var analysis = new CollectionDepthAnalysis();

            // Group albums by artist
            var albumsByArtist = albums.GroupBy(a => a.ArtistId).ToList();

            // Analyze completionist behavior
            var artistsWithManyAlbums = albumsByArtist.Where(g => g.Count() >= 5).ToList();
            var artistsWithFewAlbums = albumsByArtist.Where(g => g.Count() <= 2).ToList();

            // Calculate metrics
            analysis.CompletionistScore = artistsWithManyAlbums.Count * 100.0 / Math.Max(1, albumsByArtist.Count);
            analysis.CasualCollectorScore = artistsWithFewAlbums.Count * 100.0 / Math.Max(1, albumsByArtist.Count);

            // Determine collection style
            if (analysis.CompletionistScore > 40)
            {
                analysis.CollectionStyle = "Completionist - Collects full discographies";
            }
            else if (analysis.CasualCollectorScore > 60)
            {
                analysis.CollectionStyle = "Casual - Collects select albums";
            }
            else
            {
                analysis.CollectionStyle = "Balanced - Mix of deep and shallow collections";
            }

            // Analyze studio vs compilation preference
            var studioAlbums = albums.Count(a => a.AlbumType == "Studio");
            var compilations = albums.Count(a => a.AlbumType == "Compilation" || a.AlbumType == "Greatest Hits");
            analysis.PreferredAlbumType = studioAlbums > compilations * 2 ? "Studio Albums" : "Mixed";

            // Identify top collected artists (for completionist behavior)
            analysis.TopCollectedArtists = albumsByArtist
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new ArtistDepth
                {
                    ArtistId = g.Key,
                    AlbumCount = g.Count(),
                    IsComplete = g.Count() >= 8 // Threshold for "complete" collection
                })
                .ToList();

            return analysis;
        }

        private (Dictionary<string, int> AlbumTypes, string DiscoveryTrend)
            AnalyzeUserPreferences(List<Artist> artists, List<Album> albums)
        {
            // Album type distribution
            var albumTypes = albums
                .Where(a => !string.IsNullOrEmpty(a.AlbumType))
                .GroupBy(a => a.AlbumType)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());

            // Discovery trend based on recent additions
            var recentThreshold = DateTime.UtcNow.AddMonths(-6);
            var recentAdditions = artists.Count(a => a.Added > recentThreshold);
            var discoveryTrend = DetermineDiscoveryTrend(recentAdditions, artists.Count);

            return (albumTypes, discoveryTrend);
        }

        private List<string> ExtractSecondaryTypes(List<Album> albums)
        {
            // Extract and analyze secondary album types
            var secondaryTypes = albums
                .Where(a => a.SecondaryTypes?.Any() == true)
                .SelectMany(a => a.SecondaryTypes)
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key.ToString())
                .ToList();

            _logger.Debug($"Found {secondaryTypes.Count} secondary album types in collection");
            return secondaryTypes;
        }

        private string DetermineDiscoveryTrend(int recentAdditions, int totalArtists)
        {
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

        private string DetermineCollectionFocus(Dictionary<string, int> genres,
            (List<string> ReleaseDecades, List<string> PreferredEras, double NewReleaseRatio) temporal)
        {
            // Determine if collection is genre-focused or eclectic
            var genreFocus = "eclectic";
            if (genres.Any())
            {
                var topGenreRatio = (double)genres.First().Value / genres.Sum(g => g.Value);
                if (topGenreRatio > 0.5) genreFocus = "specialized";
                else if (topGenreRatio > 0.3) genreFocus = "focused";
            }

            // Determine temporal focus
            var temporalFocus = temporal.NewReleaseRatio > 0.3 ? "current" :
                               temporal.PreferredEras.Contains("Classic") ? "classic" : "mixed";

            return $"{genreFocus}-{temporalFocus}";
        }

        private Dictionary<string, double> CalculateGenreDistribution(Dictionary<string, int> genres)
        {
            if (!genres.Any()) return new Dictionary<string, double>();

            var total = genres.Sum(g => g.Value);
            var distribution = new Dictionary<string, double>();

            // Calculate weighted percentages with significance levels
            foreach (var genre in genres.OrderByDescending(g => g.Value))
            {
                var percentage = Math.Round((double)genre.Value / total * 100, 1);
                distribution[genre.Key] = percentage;

                // Add significance level metadata
                var significanceKey = $"{genre.Key}_significance";
                if (percentage >= 30)
                    distribution[significanceKey] = 3.0; // Core genre
                else if (percentage >= 15)
                    distribution[significanceKey] = 2.0; // Major genre
                else if (percentage >= 5)
                    distribution[significanceKey] = 1.0; // Minor genre
                else
                    distribution[significanceKey] = 0.5; // Occasional genre
            }

            // Add diversity metrics
            distribution["genre_diversity_score"] = CalculateGenreDiversity(genres, total);
            distribution["dominant_genre_percentage"] = genres.Values.Max() * 100.0 / total;
            distribution["genre_count"] = genres.Count;

            return distribution;
        }

        private double CalculateGenreDiversity(Dictionary<string, int> genres, int total)
        {
            // Shannon entropy for diversity measurement
            double entropy = 0;
            foreach (var count in genres.Values)
            {
                if (count > 0)
                {
                    double probability = (double)count / total;
                    entropy -= probability * Math.Log(probability, 2);
                }
            }

            // Normalize to 0-1 scale
            double maxEntropy = Math.Log(genres.Count, 2);
            return maxEntropy > 0 ? Math.Round(entropy / maxEntropy, 2) : 0;
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

        private Dictionary<string, int> GetFallbackGenres()
        {
            var genreCounts = new Dictionary<string, int>();
            for (int i = 0; i < Math.Min(5, BrainarrConstants.FallbackGenres.Length); i++)
            {
                genreCounts[BrainarrConstants.FallbackGenres[i]] = 20 - (i * 3);
            }
            return genreCounts;
        }

        /// <summary>
        /// Builds an enhanced prompt with rich library context for AI recommendations.
        /// </summary>
        public string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode)
        {
            return BuildPrompt(profile, maxRecommendations, discoveryMode, artistMode: false);
        }

        public string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode, bool artistMode)
        {
            var discoveryFocus = GetDiscoveryFocus(discoveryMode);
            var collectionContext = BuildCollectionContext(profile);
            var preferenceContext = BuildPreferenceContext(profile);
            var qualityContext = BuildQualityContext(profile);

            var promptHeader = artistMode ?
                $"Analyze this comprehensive music library profile and recommend {maxRecommendations} new artists:" :
                $"Analyze this comprehensive music library profile and recommend {maxRecommendations} new albums:";

            var prompt = $@"{promptHeader}

📊 COLLECTION OVERVIEW:
{collectionContext}

🎵 MUSICAL PREFERENCES:
{preferenceContext}

📈 COLLECTION QUALITY:
{qualityContext}

🎯 RECOMMENDATION REQUIREMENTS:
• Provide exactly {maxRecommendations} {(artistMode ? "artist" : "album")} recommendations
• Focus on: {discoveryFocus}
• Match the collection's character
• Consider the discovery pattern

Return a JSON array with this exact format:
[
  {{
    ""artist"": ""Artist Name"",{(artistMode ? "" : "\n    \"album\": \"Album Title\",\n    \"year\": 2024,")}
    ""genre"": ""Primary Genre"",
    ""confidence"": 0.85,
    ""reason"": ""{(artistMode ? "New artist that matches your collection's style" : "Specific album that matches your collection's character and preferences")}""
  }}
]

Ensure recommendations are:
1. NOT already in the collection
2. Actually real {(artistMode ? "artists" : "albums")} (no fictional/AI hallucinations)
3. Diverse within the specified focus area
4. High quality matches for this specific library profile";

            return prompt;
        }

        private string BuildCollectionContext(LibraryProfile profile)
        {
            var genreDistribution = profile.Metadata.ContainsKey("GenreDistribution")
                ? profile.Metadata["GenreDistribution"] as Dictionary<string, double>
                : null;

            var genreList = genreDistribution?.Any() == true
                ? string.Join(", ", genreDistribution.Take(5).Select(g => $"{g.Key} ({g.Value}%)"))
                : string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key}"));

            var collectionSize = profile.Metadata.ContainsKey("CollectionSize")
                ? profile.Metadata["CollectionSize"].ToString()
                : "established";

            var releaseDecades = profile.Metadata.ContainsKey("ReleaseDecades")
                ? string.Join(", ", (List<string>)profile.Metadata["ReleaseDecades"])
                : "mixed eras";

            var collectionFocus = profile.Metadata.ContainsKey("CollectionFocus")
                ? profile.Metadata["CollectionFocus"].ToString()
                : "general";

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

            var newReleaseRatio = profile.Metadata.ContainsKey("NewReleaseRatio")
                ? (double)profile.Metadata["NewReleaseRatio"]
                : 0.1;

            var discoveryTrend = profile.Metadata.ContainsKey("DiscoveryTrend")
                ? profile.Metadata["DiscoveryTrend"].ToString()
                : "stable collection";

            return $@"• Top artists: {topArtists}
• Album types: {albumTypeStr}
• New release interest: {newReleaseRatio:P0}
• Discovery trend: {discoveryTrend}";
        }

        private string BuildQualityContext(LibraryProfile profile)
        {
            var completeness = profile.Metadata.ContainsKey("CollectionCompleteness")
                ? (double)profile.Metadata["CollectionCompleteness"]
                : 0.7;

            var quality = completeness > 0.8 ? "Very High" :
                         completeness > 0.6 ? "High" :
                         completeness > 0.4 ? "Moderate" : "Building";

            var monitoredRatio = profile.Metadata.ContainsKey("MonitoredRatio")
                ? (double)profile.Metadata["MonitoredRatio"]
                : 0.8;

            var avgAlbumsPerArtist = profile.Metadata.ContainsKey("AverageAlbumsPerArtist")
                ? (double)profile.Metadata["AverageAlbumsPerArtist"]
                : (double)profile.TotalAlbums / Math.Max(1, profile.TotalArtists);

            return $@"• Collection quality: {quality} ({completeness:P0} complete)
• Monitoring ratio: {monitoredRatio:P0} actively tracked
• Average depth: {avgAlbumsPerArtist:F1} albums per artist";
        }

        /// <summary>
        /// Filters recommendations with enhanced duplicate detection.
        /// </summary>
        public List<ImportListItemInfo> FilterDuplicates(List<ImportListItemInfo> recommendations)
        {
            var existingAlbums = _albumService.GetAllAlbums();
            var existingArtists = _artistService.GetAllArtists();

            // Build fast-lookup sets for robust duplicate detection
            var albumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var artistKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var artist in existingArtists)
            {
                if (!string.IsNullOrWhiteSpace(artist.Name))
                {
                    // Normalize artist key variants
                    artistKeys.Add(artist.Name);
                    artistKeys.Add(artist.Name.Replace(" ", ""));
                    if (artist.Name.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                    {
                        artistKeys.Add(artist.Name.Substring(4));
                    }
                }
            }

            // Build a quick lookup for artistId -> artistName
            var artistNameById = existingArtists
                .Where(a => a != null)
                .GroupBy(a => a.Id)
                .ToDictionary(g => g.Key, g => g.First().Name);

            foreach (var album in existingAlbums)
            {
                if (!artistNameById.TryGetValue(album.ArtistId, out var artistName) || string.IsNullOrWhiteSpace(artistName))
                    continue;

                // Multiple key formats for matching existing albums
                albumKeys.Add($"{artistName}_{album.Title}");
                albumKeys.Add($"{artistName.Replace(" ", "")}_{album.Title?.Replace(" ", "")}");

                // Handle "The" prefix variations
                if (artistName.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                {
                    var nameWithoutThe = artistName.Substring(4);
                    albumKeys.Add($"{nameWithoutThe}_{album.Title}");
                }
            }

            var uniqueItems = new List<ImportListItemInfo>();
            var duplicatesFound = 0;

            foreach (var item in recommendations)
            {
                bool isDuplicate = false;

                var decodedArtist = DecodeComparisonValue(item.Artist);
                var decodedAlbum = DecodeComparisonValue(item.Album);

                if (!string.IsNullOrWhiteSpace(decodedArtist))
                {
                    item.Artist = decodedArtist;
                }

                if (!string.IsNullOrWhiteSpace(decodedAlbum))
                {
                    item.Album = decodedAlbum;
                }

                if (string.IsNullOrWhiteSpace(decodedAlbum))
                {
                    if (!string.IsNullOrWhiteSpace(decodedArtist))
                    {
                        var artistCandidates = new List<string?>
                        {
                            decodedArtist,
                            decodedArtist.Replace(" ", string.Empty),
                            decodedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? decodedArtist.Substring(4) : null
                        }.Where(k => !string.IsNullOrWhiteSpace(k));

                        foreach (var candidate in artistCandidates)
                        {
                            if (artistKeys.Contains(candidate))
                            {
                                isDuplicate = true;
                                duplicatesFound++;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    var normalizedArtist = decodedArtist;
                    var normalizedAlbum = decodedAlbum;

                    var keys = new[]
                    {
                        $"{normalizedArtist}_{normalizedAlbum}",
                        $"{normalizedArtist.Replace(" ", string.Empty)}_{normalizedAlbum.Replace(" ", string.Empty)}",
                        normalizedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase)
                            ? $"{normalizedArtist.Substring(4)}_{normalizedAlbum}"
                            : null
                    }.Where(k => !string.IsNullOrWhiteSpace(k));

                    foreach (var key in keys)
                    {
                        if (albumKeys.Contains(key))
                        {
                            isDuplicate = true;
                            duplicatesFound++;
                            break;
                        }
                    }
                }

                if (!isDuplicate)
                {
                    uniqueItems.Add(item);
                }
            }

            if (duplicatesFound > 0)
            {
                _logger.Info($"Filtered out {duplicatesFound} duplicate recommendation(s) using enhanced matching");
            }

            return uniqueItems;
        }


        public List<Recommendation> FilterExistingRecommendations(List<Recommendation> recommendations, bool artistMode)
        {
            if (recommendations == null || recommendations.Count == 0)
            {
                return recommendations ?? new List<Recommendation>();
            }

            var existingAlbums = _albumService.GetAllAlbums();
            var existingArtists = _artistService.GetAllArtists();

            var albumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var artistKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var artist in existingArtists)
            {
                if (!string.IsNullOrWhiteSpace(artist.Name))
                {
                    artistKeys.Add(artist.Name);
                    artistKeys.Add(artist.Name.Replace(" ", ""));
                    if (artist.Name.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                    {
                        artistKeys.Add(artist.Name.Substring(4));
                    }
                }
            }

            var artistNameById = existingArtists
                .Where(a => a != null)
                .GroupBy(a => a.Id)
                .ToDictionary(g => g.Key, g => g.First().Name);

            foreach (var album in existingAlbums)
            {
                if (!artistNameById.TryGetValue(album.ArtistId, out var artistName) || string.IsNullOrWhiteSpace(artistName))
                    continue;

                albumKeys.Add($"{artistName}_{album.Title}");
                albumKeys.Add($"{artistName.Replace(" ", "")}_{album.Title?.Replace(" ", "")}");

                if (artistName.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                {
                    var nameWithoutThe = artistName.Substring(4);
                    albumKeys.Add($"{nameWithoutThe}_{album.Title}");
                }
            }

            var filtered = new List<Recommendation>();
            var duplicatesFound = 0;

            foreach (var rec in recommendations)
            {
                if (rec == null)
                {
                    continue;
                }

                bool isDuplicate = false;

                var decodedArtist = DecodeComparisonValue(rec.Artist);
                var decodedAlbum = DecodeComparisonValue(rec.Album);



                if (artistMode)
                {
                    var candidates = new List<string?>
                    {
                        decodedArtist,
                        decodedArtist.Replace(" ", string.Empty),
                        decodedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? decodedArtist.Substring(4) : null
                    }.Where(k => !string.IsNullOrWhiteSpace(k));

                    if (candidates.Any(artistKeys.Contains))
                    {
                        duplicatesFound++;
                        isDuplicate = true;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(decodedAlbum))
                    {
                        if (!string.IsNullOrWhiteSpace(decodedArtist))
                        {
                            var artistCandidates = new List<string?>
                            {
                                decodedArtist,
                                decodedArtist.Replace(" ", string.Empty),
                                decodedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? decodedArtist.Substring(4) : null
                            }.Where(k => !string.IsNullOrWhiteSpace(k));

                            if (artistCandidates.Any(artistKeys.Contains))
                            {
                                duplicatesFound++;
                                isDuplicate = true;
                            }
                        }
                    }
                    else
                    {
                        var keys = new List<string?>
                        {
                            $"{decodedArtist}_{decodedAlbum}",
                            $"{decodedArtist.Replace(" ", string.Empty)}_{decodedAlbum.Replace(" ", string.Empty)}",
                            decodedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? $"{decodedArtist.Substring(4)}_{decodedAlbum}" : null
                        }.Where(k => !string.IsNullOrWhiteSpace(k));

                        if (keys.Any(albumKeys.Contains))
                        {
                            duplicatesFound++;
                            isDuplicate = true;
                        }
                    }
                }

                if (!isDuplicate)
                {
                    filtered.Add(rec);
                }
            }

            if (duplicatesFound > 0)
            {
                _logger.Info($"Filtered out {duplicatesFound} recommendation(s) already present in the library prior to validation");
            }

            return filtered;
        }


        private static LibraryAnalyzerOptions BuildDefaultOptions()
        {
            var enable = true;
            var enableEnv = Environment.GetEnvironmentVariable("BRAINARR_STYLE_CONTEXT_PARALLEL");
            if (!string.IsNullOrWhiteSpace(enableEnv))
            {
                if (bool.TryParse(enableEnv, out var parsedBool))
                {
                    enable = parsedBool;
                }
                else if (int.TryParse(enableEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                {
                    enable = numeric != 0;
                }
            }

            var threshold = 64;
            var thresholdEnv = Environment.GetEnvironmentVariable("BRAINARR_STYLE_CONTEXT_PARALLEL_THRESHOLD");
            if (!string.IsNullOrWhiteSpace(thresholdEnv) && int.TryParse(thresholdEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedThreshold) && parsedThreshold > 0)
            {
                threshold = parsedThreshold;
            }

            int? maxDegree = null;
            var maxEnv = Environment.GetEnvironmentVariable("BRAINARR_STYLE_CONTEXT_PARALLEL_MAXDOP");
            if (!string.IsNullOrWhiteSpace(maxEnv) && int.TryParse(maxEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMax) && parsedMax > 0)
            {
                maxDegree = parsedMax;
            }

            return new LibraryAnalyzerOptions
            {
                EnableParallelStyleContext = enable,
                ParallelizationThreshold = threshold,
                MaxDegreeOfParallelism = maxDegree
            };
        }

        private LibraryStyleContext BuildStyleContext(List<Artist> artists, List<Album> albums)
        {
            var context = new LibraryStyleContext();

            artists ??= new List<Artist>();
            albums ??= new List<Album>();

            if (_styleCatalog == null)
            {
                return context;
            }

            try
            {
                var total = artists.Count + albums.Count;
                var useParallel = _options.EnableParallelStyleContext &&
                                  total >= Math.Max(1, _options.ParallelizationThreshold) &&
                                  total > 1;

                if (useParallel)
                {
                    PopulateStyleContextParallel(context, artists, albums);
                }
                else
                {
                    PopulateStyleContextSequential(context, artists, albums);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Style context extraction failed; continuing without detailed style data");
                return new LibraryStyleContext();
            }

            return context;
        }

        private void PopulateStyleContextSequential(LibraryStyleContext context, List<Artist> artists, List<Album> albums)
        {
            var coverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var artistIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var albumIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var artist in artists)
            {
                var styles = ExtractArtistStyles(artist);
                if (styles.Count == 0)
                {
                    continue;
                }

                context.ArtistStyles[artist.Id] = styles;
                IncrementCoverage(coverage, styles);
                AddToIndex(artistIndex, styles, artist.Id);
            }

            foreach (var album in albums)
            {
                var styles = ExtractAlbumStyles(album);
                if (styles.Count == 0 && album.ArtistId != 0 && context.ArtistStyles.TryGetValue(album.ArtistId, out var fallback))
                {
                    styles = new HashSet<string>(fallback, StringComparer.OrdinalIgnoreCase);
                }

                if (styles.Count == 0)
                {
                    continue;
                }

                context.AlbumStyles[album.Id] = styles;
                IncrementCoverage(coverage, styles);
                AddToIndex(albumIndex, styles, album.Id);
            }

            FinalizeStyleContext(context, coverage, artistIndex, albumIndex);
        }

        private void PopulateStyleContextParallel(
            LibraryStyleContext context,
            List<Artist> artists,
            List<Album> albums,
            CancellationToken cancellationToken = default)
        {
            // 1) Touch LazyLoaded<T> instances only on the caller thread so Lidarr metadata stays thread-safe.
            var artistPairs = new List<(int Id, HashSet<string> Styles)>(artists.Count);
            foreach (var artist in artists)
            {
                cancellationToken.ThrowIfCancellationRequested();
                artistPairs.Add((artist.Id, ExtractArtistStyles(artist)));
            }

            var albumPairs = new List<(int Id, int ArtistId, HashSet<string> Styles)>(albums.Count);
            foreach (var album in albums)
            {
                cancellationToken.ThrowIfCancellationRequested();
                albumPairs.Add((album.Id, album.ArtistId, ExtractAlbumStyles(album)));
            }

            // 2) Seed shared artist style lookups sequentially before we start the parallel fan-out work.
            var coverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var artistIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var albumIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var artistStyles = new Dictionary<int, HashSet<string>>();
            var albumStyles = new Dictionary<int, HashSet<string>>();

            foreach (var pair in artistPairs)
            {
                if (pair.Styles.Count > 0)
                {
                    artistStyles[pair.Id] = pair.Styles;
                }
            }

            var coverageLock = new object();
            var artistIndexLock = new object();
            var albumIndexLock = new object();
            var albumStylesLock = new object();

            var options = CreateParallelOptions();

            // 3) Parallelize pure set aggregation with thread-local accumulators and merge under locks.
            Parallel.ForEach(
                artistPairs,
                options,
                () => (
                    localCoverage: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    localIndex: new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase)
                ),
                (pair, _, state) =>
                {
                    if (pair.Styles.Count == 0)
                    {
                        return state;
                    }

                    IncrementCoverage(state.localCoverage, pair.Styles);
                    AddToIndex(state.localIndex, pair.Styles, pair.Id);

                    return state;
                },
                state =>
                {
                    lock (coverageLock)
                    {
                        foreach (var kvp in state.localCoverage)
                        {
                            coverage[kvp.Key] = coverage.TryGetValue(kvp.Key, out var current)
                                ? current + kvp.Value
                                : kvp.Value;
                        }
                    }

                    lock (artistIndexLock)
                    {
                        foreach (var kvp in state.localIndex)
                        {
                            if (!artistIndex.TryGetValue(kvp.Key, out var list))
                            {
                                list = new List<int>(kvp.Value.Count);
                                artistIndex[kvp.Key] = list;
                            }

                            list.AddRange(kvp.Value);
                        }
                    }
                });

            // 4) Repeat the same approach for albums, reusing artist styles as a fallback when needed.
            Parallel.ForEach(
                albumPairs,
                options,
                () => (
                    localCoverage: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    localIndex: new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase),
                    assigned: new List<(int Id, HashSet<string> Styles)>()
                ),
                (pair, _, state) =>
                {
                    var styles = pair.Styles;
                    if (styles.Count == 0 && pair.ArtistId != 0 && artistStyles.TryGetValue(pair.ArtistId, out var fallback))
                    {
                        styles = new HashSet<string>(fallback, StringComparer.OrdinalIgnoreCase);
                    }

                    if (styles.Count == 0)
                    {
                        return state;
                    }

                    state.assigned.Add((pair.Id, styles));
                    IncrementCoverage(state.localCoverage, styles);
                    AddToIndex(state.localIndex, styles, pair.Id);

                    return state;
                },
                state =>
                {
                    lock (albumStylesLock)
                    {
                        foreach (var (id, styles) in state.assigned)
                        {
                            albumStyles[id] = styles;
                        }
                    }

                    lock (coverageLock)
                    {
                        foreach (var kvp in state.localCoverage)
                        {
                            coverage[kvp.Key] = coverage.TryGetValue(kvp.Key, out var current)
                                ? current + kvp.Value
                                : kvp.Value;
                        }
                    }

                    lock (albumIndexLock)
                    {
                        foreach (var kvp in state.localIndex)
                        {
                            if (!albumIndex.TryGetValue(kvp.Key, out var list))
                            {
                                list = new List<int>(kvp.Value.Count);
                                albumIndex[kvp.Key] = list;
                            }

                            list.AddRange(kvp.Value);
                        }
                    }
                });

            // 5) Publish accumulated state back onto the context once all workers have finished.
            foreach (var pair in artistStyles)
            {
                context.ArtistStyles[pair.Key] = pair.Value;
            }

            foreach (var pair in albumStyles)
            {
                context.AlbumStyles[pair.Key] = pair.Value;
            }

            FinalizeStyleContext(context, coverage, artistIndex, albumIndex);
        }

        private ParallelOptions CreateParallelOptions()
        {
            if (_options.MaxDegreeOfParallelism.HasValue && _options.MaxDegreeOfParallelism.Value > 0)
            {
                return new ParallelOptions { MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism.Value };
            }

            return new ParallelOptions();
        }

        private void FinalizeStyleContext(
            LibraryStyleContext context,
            IDictionary<string, int> coverage,
            IDictionary<string, List<int>> artistIndex,
            IDictionary<string, List<int>> albumIndex)
        {
            var coverageDict = new Dictionary<string, int>(coverage, StringComparer.OrdinalIgnoreCase);
            context.SetCoverage(coverageDict);

            var dominant = coverageDict
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .Select(kvp => kvp.Key);

            context.SetDominantStyles(dominant);

            var artistReadOnly = ConvertIndexToReadOnly(artistIndex);
            var albumReadOnly = ConvertIndexToReadOnly(albumIndex);

            context.SetStyleIndex(new LibraryStyleIndex(artistReadOnly, albumReadOnly));
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<int>> ConvertIndexToReadOnly(IDictionary<string, List<int>> source)
        {
            var result = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in source)
            {
                if (kvp.Value == null || kvp.Value.Count == 0)
                {
                    continue;
                }

                var ordered = kvp.Value.Distinct().ToList();
                ordered.Sort();
                result[kvp.Key] = ordered.ToArray();
            }

            return result;
        }

        private static void IncrementCoverage(IDictionary<string, int> coverage, IEnumerable<string> styles)
        {
            foreach (var style in styles)
            {
                if (string.IsNullOrWhiteSpace(style))
                {
                    continue;
                }

                if (coverage.TryGetValue(style, out var count))
                {
                    coverage[style] = count + 1;
                }
                else
                {
                    coverage[style] = 1;
                }
            }
        }

        private static void AddToIndex(IDictionary<string, List<int>> index, IEnumerable<string> styles, int id)
        {
            foreach (var style in styles)
            {
                if (string.IsNullOrWhiteSpace(style))
                {
                    continue;
                }

                if (!index.TryGetValue(style, out var list))
                {
                    list = new List<int>();
                    index[style] = list;
                }

                list.Add(id);
            }
        }

        private HashSet<string> ExtractArtistStyles(Artist artist)
        {
            if (artist == null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var candidates = new List<string>();

            try
            {
                var metadata = artist.Metadata?.Value;
                if (metadata?.Genres?.Any() == true)
                {
                    candidates.AddRange(metadata.Genres);
                }
            }
            catch
            {
                // Ignore metadata access issues
            }

            return NormalizeStyles(candidates);
        }

        private HashSet<string> ExtractAlbumStyles(Album album)
        {
            if (album == null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var candidates = album.Genres ?? new List<string>();
            return NormalizeStyles(candidates);
        }

        private HashSet<string> NormalizeStyles(IEnumerable<string> values)
        {
            var normalized = _styleCatalog.Normalize(values ?? Array.Empty<string>());
            if (normalized == null || normalized.Count == 0)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(normalized, StringComparer.OrdinalIgnoreCase);
        }


        private LibraryProfile GetFallbackProfile()
        {
            var profile = new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = GetFallbackGenres(),
                TopArtists = new List<string>
                {
                    "Radiohead",
                    "Pink Floyd",
                    "Miles Davis"
                },
                RecentlyAdded = new List<string>()
            };

            // Add default metadata for enhanced context
            profile.Metadata["GenreDistribution"] = new Dictionary<string, double>
            {
                { "Rock", 30.0 },
                { "Electronic", 20.0 },
                { "Jazz", 15.0 }
            };
            profile.Metadata["CollectionSize"] = "growing";
            profile.Metadata["CollectionFocus"] = "eclectic-mixed";
            profile.Metadata["DiscoveryTrend"] = "stable collection";
            profile.Metadata["ReleaseDecades"] = new List<string> { "2010s", "2000s", "1990s" };
            profile.Metadata["PreferredEras"] = new List<string> { "Modern", "Contemporary" };
            profile.Metadata["NewReleaseRatio"] = 0.15;
            profile.Metadata["MonitoredRatio"] = 0.8;
            profile.Metadata["CollectionCompleteness"] = 0.7;
            profile.Metadata["AverageAlbumsPerArtist"] = 5.0;
            profile.Metadata["AlbumTypes"] = new Dictionary<string, int>
            {
                { "Album", 400 },
                { "EP", 50 },
                { "Single", 50 }
            };

            return profile;
        }


        private static string DecodeComparisonValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return WebUtility.HtmlDecode(value).Trim();
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
    }

    // Supporting classes for enhanced collection analysis
    internal class CollectionDepthAnalysis
    {
        public double CompletionistScore { get; set; }
        public double CasualCollectorScore { get; set; }
        public string CollectionStyle { get; set; } = string.Empty;
        public string PreferredAlbumType { get; set; } = string.Empty;
        public List<ArtistDepth> TopCollectedArtists { get; set; } = new List<ArtistDepth>();
    }

    internal class ArtistDepth
    {
        public int ArtistId { get; set; }
        public int AlbumCount { get; set; }
        public bool IsComplete { get; set; }
    }
}
