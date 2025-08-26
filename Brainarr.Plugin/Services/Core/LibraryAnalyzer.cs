using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service for analyzing the user's music library with rich metadata extraction.
    /// Provides comprehensive library profiling for intelligent AI recommendations.
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
                    RecentlyAdded = GetRecentlyAddedArtists(artists)
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
            
            // Decade Grouping Algorithm: Convert years to decade boundaries
            // Example: 1987 -> (1987/10)*10 = 198*10 = 1980
            // This efficiently groups all years within the same decade together
            var decadeGroups = albumsWithDates
                .GroupBy(a => (a.ReleaseDate.Value.Year / 10) * 10)
                .OrderByDescending(g => g.Count())
                .ToList();
            
            // Extract top 3 most represented decades for AI prompting
            var releaseDecades = decadeGroups
                .Take(3)
                .Select(g => $"{g.Key}s")
                .ToList();
            
            // Era Classification Algorithm: Map decades to musical eras
            // This provides more meaningful context than raw decades for AI recommendations
            // Based on commonly recognized music industry periods
            var preferredEras = new List<string>();
            foreach (var decade in decadeGroups.Take(2))
            {
                var year = decade.Key;
                if (year < 1970) preferredEras.Add("Classic");        // Pre-rock era
                else if (year < 1990) preferredEras.Add("Golden Age");  // Classic rock/punk/new wave
                else if (year < 2010) preferredEras.Add("Modern");      // Grunge/alt-rock/early digital
                else preferredEras.Add("Contemporary");                 // Streaming era
            }
            preferredEras = preferredEras.Distinct().ToList();
            
            // New Release Interest Calculation: Measures user's preference for recent music
            // Uses 2-year sliding window to capture current listening trends
            // Higher ratio = user actively seeks new releases, Lower = prefers catalog music
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
            
            // Collection Depth Analysis Algorithm: Determines user's collecting behavior patterns
            // Groups albums by artist to analyze depth vs breadth preferences
            var albumsByArtist = albums.GroupBy(a => a.ArtistId).ToList();
            
            // Behavioral Pattern Detection: Classify collectors by album-per-artist distribution
            // Completionists: Collect many albums from fewer artists (deep collections)
            // Casual Collectors: Collect few albums from many artists (broad collections)
            var artistsWithManyAlbums = albumsByArtist.Where(g => g.Count() >= 5).ToList();  // 5+ albums = deep collection
            var artistsWithFewAlbums = albumsByArtist.Where(g => g.Count() <= 2).ToList();   // 1-2 albums = casual sampling
            
            // Calculate percentage-based scores for behavior classification
            // Math.Max(1, ...) prevents division by zero in edge cases
            analysis.CompletionistScore = artistsWithManyAlbums.Count * 100.0 / Math.Max(1, albumsByArtist.Count);
            analysis.CasualCollectorScore = artistsWithFewAlbums.Count * 100.0 / Math.Max(1, albumsByArtist.Count);
            
            // Collection Style Classification Algorithm: Threshold-based categorization
            // These thresholds were determined through analysis of typical collector patterns
            if (analysis.CompletionistScore > 40)  // 40%+ artists have deep collections
            {
                analysis.CollectionStyle = "Completionist - Collects full discographies";
            }
            else if (analysis.CasualCollectorScore > 60)  // 60%+ artists have shallow collections
            {
                analysis.CollectionStyle = "Casual - Collects select albums";
            }
            else
            {
                analysis.CollectionStyle = "Balanced - Mix of deep and shallow collections";
            }
            
            // Album Type Preference Analysis: Determines if user prefers originals vs compilations
            // 2x multiplier reflects that studio albums are generally preferred over compilations
            var studioAlbums = albums.Count(a => a.AlbumType == "Studio");
            var compilations = albums.Count(a => a.AlbumType == "Compilation" || a.AlbumType == "Greatest Hits");
            analysis.PreferredAlbumType = studioAlbums > compilations * 2 ? "Studio Albums" : "Mixed";
            
            // Deep Collection Identification: Find artists with extensive album collections
            // 8-album threshold represents a "complete" artist collection based on typical discography sizes
            // Used to identify artists the user is deeply invested in collecting
            analysis.TopCollectedArtists = albumsByArtist
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new ArtistDepth
                {
                    ArtistId = g.Key,
                    AlbumCount = g.Count(),
                    IsComplete = g.Count() >= 8 // 8+ albums typically indicates comprehensive collection
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
            // Shannon Entropy Algorithm for measuring genre diversity in music collection
            // Higher entropy = more diverse collection, Lower entropy = focused on fewer genres
            // Formula: H(X) = -Σ P(xi) * log2(P(xi)) where P(xi) = probability of genre i
            
            double entropy = 0;
            foreach (var count in genres.Values)
            {
                if (count > 0)
                {
                    // Calculate probability of this genre appearing (count/total)
                    double probability = (double)count / total;
                    
                    // Apply Shannon entropy formula: -P * log2(P)
                    // The negative sign is because log of probability (0-1) is negative
                    entropy -= probability * Math.Log(probability, 2);
                }
            }
            
            // Normalize entropy to 0-1 scale for easier interpretation
            // Maximum possible entropy = log2(number of distinct genres)
            // This occurs when all genres have equal representation
            double maxEntropy = Math.Log(genres.Count, 2);
            
            // Return normalized diversity score: 0 = no diversity, 1 = maximum diversity
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
            var discoveryFocus = GetDiscoveryFocus(discoveryMode);
            var collectionContext = BuildCollectionContext(profile);
            var preferenceContext = BuildPreferenceContext(profile);
            var qualityContext = BuildQualityContext(profile);
            
            var prompt = $@"Analyze this comprehensive music library profile and recommend {maxRecommendations} new albums:

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
            
            // Create multiple matching strategies for robust duplicate detection
            var albumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var album in existingAlbums)
            {
                var artist = existingArtists.FirstOrDefault(a => a.Id == album.ArtistId);
                if (artist != null)
                {
                    // Multiple key formats for matching
                    albumKeys.Add($"{artist.Name}_{album.Title}");
                    albumKeys.Add($"{artist.Name.Replace(" ", "")}_{album.Title?.Replace(" ", "")}");
                    
                    // Handle "The" prefix variations
                    if (artist.Name.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                    {
                        var nameWithoutThe = artist.Name.Substring(4);
                        albumKeys.Add($"{nameWithoutThe}_{album.Title}");
                    }
                }
            }
            
            var uniqueItems = new List<ImportListItemInfo>();
            var duplicatesFound = 0;
            
            foreach (var item in recommendations)
            {
                var isDuplicate = false;
                
                // Check multiple key formats
                var keys = new[]
                {
                    $"{item.Artist}_{item.Album}",
                    $"{item.Artist?.Replace(" ", "")}_{item.Album?.Replace(" ", "")}",
                    item.Artist?.StartsWith("The ", StringComparison.OrdinalIgnoreCase) == true
                        ? $"{item.Artist.Substring(4)}_{item.Album}"
                        : null
                }.Where(k => k != null);
                
                foreach (var key in keys)
                {
                    if (albumKeys.Contains(key))
                    {
                        isDuplicate = true;
                        duplicatesFound++;
                        break;
                    }
                }
                
                if (!isDuplicate)
                {
                    uniqueItems.Add(item);
                }
            }
            
            if (duplicatesFound > 0)
            {
                _logger.Info($"Filtered out {duplicatesFound} duplicate recommendations using enhanced matching");
            }
            
            return uniqueItems;
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