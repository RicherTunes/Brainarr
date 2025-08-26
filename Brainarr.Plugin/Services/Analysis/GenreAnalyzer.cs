using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    /// <summary>
    /// Analyzes genre patterns and relationships in music libraries.
    /// Handles genre clustering, relationships, and trend analysis.
    /// </summary>
    public interface IGenreAnalyzer
    {
        GenreAnalysisResult AnalyzeGenres(Dictionary<string, int> genreDistribution, List<Artist> artists);
        List<string> IdentifyDominantGenres(Dictionary<string, int> genreDistribution, int topN = 5);
        Dictionary<string, List<string>> FindGenreRelationships(List<Artist> artists);
        double CalculateGenreDiversity(Dictionary<string, int> genreDistribution);
    }
    
    public class GenreAnalyzer : IGenreAnalyzer
    {
        private readonly Logger _logger;
        
        // Common genre families for relationship mapping
        private readonly Dictionary<string, string[]> _genreFamilies = new()
        {
            ["rock"] = new[] { "rock", "alternative", "indie", "punk", "metal", "grunge", "post-rock" },
            ["electronic"] = new[] { "electronic", "techno", "house", "ambient", "idm", "synthwave", "edm" },
            ["jazz"] = new[] { "jazz", "fusion", "bebop", "swing", "smooth jazz", "free jazz" },
            ["classical"] = new[] { "classical", "orchestral", "symphony", "baroque", "romantic" },
            ["hip-hop"] = new[] { "hip-hop", "rap", "trap", "boom bap", "conscious hip hop" },
            ["folk"] = new[] { "folk", "acoustic", "singer-songwriter", "americana", "bluegrass" },
            ["metal"] = new[] { "metal", "heavy metal", "death metal", "black metal", "doom", "thrash" }
        };
        
        public GenreAnalyzer(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public GenreAnalysisResult AnalyzeGenres(Dictionary<string, int> genreDistribution, List<Artist> artists)
        {
            var result = new GenreAnalysisResult
            {
                TotalGenres = genreDistribution?.Count ?? 0,
                GenreDistribution = genreDistribution ?? new Dictionary<string, int>()
            };
            
            if (genreDistribution == null || !genreDistribution.Any())
            {
                _logger.Warn("No genre distribution provided for analysis");
                return result;
            }
            
            // Identify dominant genres
            result.DominantGenres = IdentifyDominantGenres(genreDistribution);
            
            // Calculate diversity
            result.DiversityScore = CalculateGenreDiversity(genreDistribution);
            
            // Find genre families
            result.GenreFamilies = IdentifyGenreFamilies(genreDistribution);
            
            // Find genre relationships if artists provided
            if (artists != null && artists.Any())
            {
                result.GenreRelationships = FindGenreRelationships(artists);
            }
            
            // Identify niche genres (low frequency but present)
            result.NicheGenres = genreDistribution
                .Where(g => g.Value == 1)
                .Select(g => g.Key)
                .Take(10)
                .ToList();
            
            _logger.Debug($"Genre analysis complete: {result.TotalGenres} genres, diversity score: {result.DiversityScore:F2}");
            
            return result;
        }
        
        public List<string> IdentifyDominantGenres(Dictionary<string, int> genreDistribution, int topN = 5)
        {
            if (genreDistribution == null || !genreDistribution.Any())
            {
                return new List<string>();
            }
            
            return genreDistribution
                .OrderByDescending(g => g.Value)
                .Take(topN)
                .Select(g => g.Key)
                .ToList();
        }
        
        public Dictionary<string, List<string>> FindGenreRelationships(List<Artist> artists)
        {
            var relationships = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            
            if (artists == null || !artists.Any())
            {
                return relationships;
            }
            
            // Build co-occurrence map
            var coOccurrence = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var artist in artists.Where(a => a.Genres != null && a.Genres.Count > 1))
            {
                var genres = artist.Genres.Select(g => g.ToLowerInvariant()).Distinct().ToList();
                
                for (int i = 0; i < genres.Count; i++)
                {
                    if (!coOccurrence.ContainsKey(genres[i]))
                    {
                        coOccurrence[genres[i]] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    }
                    
                    for (int j = 0; j < genres.Count; j++)
                    {
                        if (i != j)
                        {
                            coOccurrence[genres[i]][genres[j]] = 
                                coOccurrence[genres[i]].GetValueOrDefault(genres[j]) + 1;
                        }
                    }
                }
            }
            
            // Convert co-occurrence to relationships (top 3 related genres for each)
            foreach (var genre in coOccurrence)
            {
                relationships[genre.Key] = genre.Value
                    .OrderByDescending(r => r.Value)
                    .Take(3)
                    .Select(r => r.Key)
                    .ToList();
            }
            
            _logger.Debug($"Found genre relationships for {relationships.Count} genres");
            
            return relationships;
        }
        
        public double CalculateGenreDiversity(Dictionary<string, int> genreDistribution)
        {
            if (genreDistribution == null || !genreDistribution.Any())
            {
                return 0.0;
            }
            
            // Shannon entropy for diversity calculation
            var total = genreDistribution.Values.Sum();
            if (total == 0) return 0.0;
            
            double entropy = 0.0;
            foreach (var count in genreDistribution.Values)
            {
                if (count > 0)
                {
                    double probability = (double)count / total;
                    entropy -= probability * Math.Log(probability, 2);
                }
            }
            
            // Normalize to 0-1 scale
            double maxEntropy = Math.Log(genreDistribution.Count, 2);
            return maxEntropy > 0 ? entropy / maxEntropy : 0.0;
        }
        
        private Dictionary<string, List<string>> IdentifyGenreFamilies(Dictionary<string, int> genreDistribution)
        {
            var families = new Dictionary<string, List<string>>();
            
            foreach (var family in _genreFamilies)
            {
                var matchingGenres = genreDistribution.Keys
                    .Where(g => family.Value.Any(f => g.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                
                if (matchingGenres.Any())
                {
                    families[family.Key] = matchingGenres;
                }
            }
            
            return families;
        }
    }
    
    /// <summary>
    /// Results from genre analysis
    /// </summary>
    public class GenreAnalysisResult
    {
        public int TotalGenres { get; set; }
        public Dictionary<string, int> GenreDistribution { get; set; } = new();
        public List<string> DominantGenres { get; set; } = new();
        public List<string> NicheGenres { get; set; } = new();
        public Dictionary<string, List<string>> GenreRelationships { get; set; } = new();
        public Dictionary<string, List<string>> GenreFamilies { get; set; } = new();
        public double DiversityScore { get; set; }
    }
}