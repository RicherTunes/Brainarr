using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    /// <summary>
    /// Interface for advanced duplicate detection with fuzzy matching capabilities.
    /// </summary>
    public interface IAdvancedDuplicateDetector
    {
        Task<bool> IsAlreadyInLibraryAsync(Recommendation recommendation);
        Task<List<DuplicateMatch>> FindPotentialDuplicatesAsync(Recommendation recommendation, double threshold = 0.8);
        Task<double> CalculateSimilarityScoreAsync(Recommendation recommendation, Artist existingArtist, Album existingAlbum);
    }

    /// <summary>
    /// Advanced duplicate detection system with fuzzy matching, normalization, and similarity scoring.
    /// </summary>
    public class AdvancedDuplicateDetector : IAdvancedDuplicateDetector
    {
        private readonly Logger _logger;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        
        // Cached normalized data for performance
        private Dictionary<int, string> _normalizedArtistNames;
        private Dictionary<int, string> _normalizedAlbumTitles;
        private DateTime _cacheLastUpdated;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1);

        public AdvancedDuplicateDetector(Logger logger, IArtistService artistService, IAlbumService albumService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            _normalizedArtistNames = new Dictionary<int, string>();
            _normalizedAlbumTitles = new Dictionary<int, string>();
        }

        public async Task<bool> IsAlreadyInLibraryAsync(Recommendation recommendation)
        {
            var potentialDuplicates = await FindPotentialDuplicatesAsync(recommendation, 0.85);
            return potentialDuplicates.Any(d => d.IsHighConfidenceMatch);
        }

        public async Task<List<DuplicateMatch>> FindPotentialDuplicatesAsync(Recommendation recommendation, double threshold = 0.8)
        {
            try
            {
                _logger.Debug($"Finding potential duplicates for: {recommendation.Artist} - {recommendation.Album}");

                await RefreshCacheIfNeeded();

                var matches = new List<DuplicateMatch>();
                var normalizedRecommendation = NormalizeRecommendation(recommendation);

                // Get all artists and albums for comparison
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();

                // Pre-filter by artist similarity to improve performance
                var candidateArtists = FindSimilarArtists(normalizedRecommendation.Artist, artists, 0.6);

                foreach (var artist in candidateArtists)
                {
                    var artistAlbums = albums.Where(a => a.ArtistId == artist.Id);
                    
                    foreach (var album in artistAlbums)
                    {
                        var similarity = await CalculateSimilarityScoreAsync(recommendation, artist, album);
                        
                        if (similarity >= threshold)
                        {
                            matches.Add(new DuplicateMatch
                            {
                                ExistingArtist = artist,
                                ExistingAlbum = album,
                                SimilarityScore = similarity,
                                MatchType = DetermineMatchType(similarity),
                                MatchDetails = CreateMatchDetails(normalizedRecommendation, artist, album, similarity)
                            });
                        }
                    }
                }

                // Sort by similarity score (highest first)
                matches = matches.OrderByDescending(m => m.SimilarityScore).ToList();

                _logger.Debug($"Found {matches.Count} potential duplicates for: {recommendation.Artist} - {recommendation.Album}");
                return matches;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error finding duplicates for: {recommendation.Artist} - {recommendation.Album}");
                return new List<DuplicateMatch>();
            }
        }

        public async Task<double> CalculateSimilarityScoreAsync(Recommendation recommendation, Artist existingArtist, Album existingAlbum)
        {
            var normalizedRec = NormalizeRecommendation(recommendation);
            var normalizedArtist = NormalizeArtistName(existingArtist.Name);
            var normalizedAlbum = NormalizeAlbumTitle(existingAlbum.Title);

            // Calculate individual similarity scores
            var artistSimilarity = CalculateStringSimilarity(normalizedRec.Artist, normalizedArtist);
            var albumSimilarity = CalculateStringSimilarity(normalizedRec.Album, normalizedAlbum);
            
            // Year similarity (if available)
            var yearSimilarity = CalculateYearSimilarity(recommendation.Year, existingAlbum.ReleaseDate?.Year);

            // Weighted combination (artist and album are most important)
            var weightedScore = (artistSimilarity * 0.4) + (albumSimilarity * 0.5) + (yearSimilarity * 0.1);

            // Apply bonus for exact matches
            if (artistSimilarity > 0.95) weightedScore += 0.05;
            if (albumSimilarity > 0.95) weightedScore += 0.05;

            return Math.Min(1.0, weightedScore);
        }

        private async Task RefreshCacheIfNeeded()
        {
            if (DateTime.UtcNow - _cacheLastUpdated > _cacheExpiry)
            {
                _logger.Debug("Refreshing duplicate detection cache");
                
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();
                
                _normalizedArtistNames = artists.ToDictionary(a => a.Id, a => NormalizeArtistName(a.Name));
                _normalizedAlbumTitles = albums.ToDictionary(a => a.Id, a => NormalizeAlbumTitle(a.Title));
                
                _cacheLastUpdated = DateTime.UtcNow;
                _logger.Debug($"Cache refreshed: {artists.Count} artists, {albums.Count} albums");
            }
        }

        private NormalizedRecommendation NormalizeRecommendation(Recommendation recommendation)
        {
            return new NormalizedRecommendation
            {
                Artist = NormalizeArtistName(recommendation.Artist),
                Album = NormalizeAlbumTitle(recommendation.Album),
                Genre = NormalizeGenre(recommendation.Genre)
            };
        }

        private string NormalizeArtistName(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName)) return string.Empty;

            var normalized = artistName.ToLowerInvariant();

            // Handle "The" prefix variations
            normalized = Regex.Replace(normalized, @"^the\s+", "", RegexOptions.IgnoreCase);
            
            // Remove common punctuation and special characters
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");
            
            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            
            // Handle common abbreviations and variations
            var abbreviations = new Dictionary<string, string>
            {
                ["ft"] = "featuring",
                ["feat"] = "featuring",
                ["w/"] = "with",
                ["&"] = "and",
                ["+"] = "and"
            };
            
            foreach (var abbrev in abbreviations)
            {
                normalized = normalized.Replace(abbrev.Key, abbrev.Value);
            }

            return normalized;
        }

        private string NormalizeAlbumTitle(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle)) return string.Empty;

            var normalized = albumTitle.ToLowerInvariant();

            // Remove edition/remaster suffixes for better matching
            var suffixPatterns = new[]
            {
                @"\s*\((deluxe|special|limited|expanded|collector's?|anniversary)(\s+edition)?\).*$",
                @"\s*\((re)?master(ed)?\s*(\d{4})?\).*$",
                @"\s*\((\d{4}\s+)?(re)?master(ed)?\).*$",
                @"\s*\((bonus\s+)?(tracks?|material)\).*$",
                @"\s*\[.*\].*$", // Remove anything in square brackets
                @"\s*-\s*(deluxe|special|remastered).*$"
            };

            foreach (var pattern in suffixPatterns)
            {
                normalized = Regex.Replace(normalized, pattern, "", RegexOptions.IgnoreCase);
            }

            // Remove common punctuation
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");
            
            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // Handle Roman numerals (convert to numbers)
            var romanNumerals = new Dictionary<string, string>
            {
                ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5",
                ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10"
            };

            foreach (var roman in romanNumerals)
            {
                normalized = Regex.Replace(normalized, $@"\b{roman.Key}\b", roman.Value, RegexOptions.IgnoreCase);
            }

            return normalized;
        }

        private string NormalizeGenre(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre)) return string.Empty;
            
            return genre.ToLowerInvariant()
                .Replace("-", " ")
                .Replace("&", "and")
                .Trim();
        }

        private List<Artist> FindSimilarArtists(string normalizedArtistName, List<Artist> allArtists, double threshold)
        {
            var similarArtists = new List<Artist>();

            foreach (var artist in allArtists)
            {
                var normalizedExisting = NormalizeArtistName(artist.Name);
                var similarity = CalculateStringSimilarity(normalizedArtistName, normalizedExisting);
                
                if (similarity >= threshold)
                {
                    similarArtists.Add(artist);
                }
            }

            return similarArtists;
        }

        private double CalculateStringSimilarity(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
                return 0.0;

            if (str1 == str2)
                return 1.0;

            // Use multiple similarity algorithms and take the best result
            var levenshteinSimilarity = CalculateLevenshteinSimilarity(str1, str2);
            var jaroWinklerSimilarity = CalculateJaroWinklerSimilarity(str1, str2);
            var tokenSimilarity = CalculateTokenSimilarity(str1, str2);

            // Return the highest similarity score
            return Math.Max(Math.Max(levenshteinSimilarity, jaroWinklerSimilarity), tokenSimilarity);
        }

        private double CalculateLevenshteinSimilarity(string str1, string str2)
        {
            if (str1.Length == 0) return str2.Length == 0 ? 1.0 : 0.0;
            if (str2.Length == 0) return 0.0;

            var distance = CalculateLevenshteinDistance(str1, str2);
            var maxLength = Math.Max(str1.Length, str2.Length);
            
            return 1.0 - (double)distance / maxLength;
        }

        private int CalculateLevenshteinDistance(string str1, string str2)
        {
            var matrix = new int[str1.Length + 1, str2.Length + 1];

            for (int i = 0; i <= str1.Length; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= str2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= str1.Length; i++)
            {
                for (int j = 1; j <= str2.Length; j++)
                {
                    var cost = str1[i - 1] == str2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[str1.Length, str2.Length];
        }

        private double CalculateJaroWinklerSimilarity(string str1, string str2)
        {
            // Simplified Jaro-Winkler implementation
            if (str1 == str2) return 1.0;
            if (str1.Length == 0 || str2.Length == 0) return 0.0;

            var matchWindow = Math.Max(str1.Length, str2.Length) / 2 - 1;
            if (matchWindow < 0) matchWindow = 0;

            var str1Matches = new bool[str1.Length];
            var str2Matches = new bool[str2.Length];

            var matches = 0;
            var transpositions = 0;

            // Find matches
            for (int i = 0; i < str1.Length; i++)
            {
                var start = Math.Max(0, i - matchWindow);
                var end = Math.Min(i + matchWindow + 1, str2.Length);

                for (int j = start; j < end; j++)
                {
                    if (str2Matches[j] || str1[i] != str2[j]) continue;
                    str1Matches[i] = true;
                    str2Matches[j] = true;
                    matches++;
                    break;
                }
            }

            if (matches == 0) return 0.0;

            // Count transpositions
            var k = 0;
            for (int i = 0; i < str1.Length; i++)
            {
                if (!str1Matches[i]) continue;
                while (!str2Matches[k]) k++;
                if (str1[i] != str2[k]) transpositions++;
                k++;
            }

            var jaro = ((double)matches / str1.Length + (double)matches / str2.Length + 
                       (matches - transpositions / 2.0) / matches) / 3.0;

            // Apply Winkler prefix bonus
            var prefix = 0;
            for (int i = 0; i < Math.Min(str1.Length, str2.Length) && i < 4; i++)
            {
                if (str1[i] == str2[i]) prefix++;
                else break;
            }

            return jaro + 0.1 * prefix * (1 - jaro);
        }

        private double CalculateTokenSimilarity(string str1, string str2)
        {
            var tokens1 = str1.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var tokens2 = str2.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            if (!tokens1.Any() || !tokens2.Any()) return 0.0;

            var intersection = tokens1.Intersect(tokens2).Count();
            var union = tokens1.Union(tokens2).Count();

            return (double)intersection / union; // Jaccard similarity
        }

        private double CalculateYearSimilarity(int? year1, int? year2)
        {
            if (!year1.HasValue || !year2.HasValue) return 0.0;
            
            var difference = Math.Abs(year1.Value - year2.Value);
            
            // Perfect match
            if (difference == 0) return 1.0;
            
            // Close years get high similarity
            if (difference <= 2) return 0.8;
            if (difference <= 5) return 0.5;
            
            // Distant years get low similarity
            return Math.Max(0.0, 1.0 - difference / 50.0);
        }

        private DuplicateMatchType DetermineMatchType(double similarity)
        {
            if (similarity >= 0.95) return DuplicateMatchType.ExactMatch;
            if (similarity >= 0.85) return DuplicateMatchType.HighConfidence;
            if (similarity >= 0.70) return DuplicateMatchType.MediumConfidence;
            return DuplicateMatchType.LowConfidence;
        }

        private Dictionary<string, object> CreateMatchDetails(NormalizedRecommendation recommendation, Artist artist, Album album, double similarity)
        {
            return new Dictionary<string, object>
            {
                ["RecommendedArtist"] = recommendation.Artist,
                ["RecommendedAlbum"] = recommendation.Album,
                ["ExistingArtist"] = artist.Name,
                ["ExistingAlbum"] = album.Title,
                ["SimilarityScore"] = similarity,
                ["ArtistSimilarity"] = CalculateStringSimilarity(recommendation.Artist, NormalizeArtistName(artist.Name)),
                ["AlbumSimilarity"] = CalculateStringSimilarity(recommendation.Album, NormalizeAlbumTitle(album.Title))
            };
        }
    }

    /// <summary>
    /// Normalized recommendation for comparison.
    /// </summary>
    public class NormalizedRecommendation
    {
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Genre { get; set; }
    }

    /// <summary>
    /// Result of duplicate matching with detailed information.
    /// </summary>
    public class DuplicateMatch
    {
        public Artist ExistingArtist { get; set; }
        public Album ExistingAlbum { get; set; }
        public double SimilarityScore { get; set; }
        public DuplicateMatchType MatchType { get; set; }
        public Dictionary<string, object> MatchDetails { get; set; } = new Dictionary<string, object>();
        
        public bool IsHighConfidenceMatch => MatchType == DuplicateMatchType.ExactMatch || 
                                           MatchType == DuplicateMatchType.HighConfidence;
    }

    /// <summary>
    /// Types of duplicate matches based on confidence level.
    /// </summary>
    public enum DuplicateMatchType
    {
        ExactMatch,       // 95%+ similarity
        HighConfidence,   // 85-94% similarity
        MediumConfidence, // 70-84% similarity
        LowConfidence     // Below 70% similarity
    }
}