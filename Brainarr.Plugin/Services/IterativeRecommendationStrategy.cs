using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class IterativeRecommendationStrategy
    {
        private readonly Logger _logger;
        private readonly LibraryAwarePromptBuilder _promptBuilder;
        
        private const int MAX_ITERATIONS = 3;
        private const double MIN_SUCCESS_RATE = 0.7; // At least 70% unique recommendations

        public IterativeRecommendationStrategy(Logger logger, LibraryAwarePromptBuilder promptBuilder)
        {
            _logger = logger;
            _promptBuilder = promptBuilder;
        }

        public async Task<List<Recommendation>> GetIterativeRecommendationsAsync(
            IAIProvider provider,
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings)
        {
            var existingAlbums = BuildExistingAlbumsSet(allAlbums);
            var allRecommendations = new List<Recommendation>();
            var rejectedAlbums = new HashSet<string>();
            
            var targetCount = settings.MaxRecommendations;
            var iteration = 1;
            
            while (allRecommendations.Count < targetCount && iteration <= MAX_ITERATIONS)
            {
                _logger.Info($"Iteration {iteration}: Need {targetCount - allRecommendations.Count} more recommendations");
                
                // Adjust request size for this iteration
                var requestSize = CalculateIterationRequestSize(targetCount - allRecommendations.Count, iteration);
                
                // Build context-aware prompt with rejection history
                var prompt = BuildIterativePrompt(
                    profile, 
                    allArtists, 
                    allAlbums, 
                    settings, 
                    requestSize,
                    rejectedAlbums,
                    allRecommendations);
                
                try
                {
                    // Get recommendations from AI
                    var recommendations = await provider.GetRecommendationsAsync(prompt);
                    
                    if (!recommendations.Any())
                    {
                        _logger.Warn($"Iteration {iteration}: No recommendations received");
                        break;
                    }
                    
                    // Filter out duplicates and track rejections
                    var (uniqueRecs, duplicates) = FilterAndTrackDuplicates(
                        recommendations, existingAlbums, allRecommendations, rejectedAlbums);
                    
                    allRecommendations.AddRange(uniqueRecs);
                    
                    // Log iteration results
                    var successRate = recommendations.Any() ? (double)uniqueRecs.Count / recommendations.Count : 0;
                    _logger.Info($"Iteration {iteration}: {uniqueRecs.Count}/{recommendations.Count} unique " +
                                $"(success rate: {successRate:P1})");
                    
                    // Check if we should continue
                    if (ShouldContinueIterating(successRate, allRecommendations.Count, targetCount, iteration))
                    {
                        iteration++;
                        continue;
                    }
                    
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Iteration {iteration} failed: {ex.Message}");
                    break;
                }
            }
            
            _logger.Info($"Iterative strategy completed: {allRecommendations.Count}/{targetCount} " +
                        $"recommendations after {iteration} iterations");
            
            return allRecommendations.Take(targetCount).ToList();
        }

        private HashSet<string> BuildExistingAlbumsSet(List<Album> allAlbums)
        {
            return allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .Select(a => NormalizeAlbumKey(a.ArtistMetadata.Value.Name, a.Title))
                .ToHashSet();
        }

        private int CalculateIterationRequestSize(int needed, int iteration)
        {
            // Request more than needed to account for duplicates, with diminishing over-request
            var multiplier = iteration switch
            {
                1 => 1.5, // 50% more on first try
                2 => 2.0, // 100% more on second try (AI should learn)
                3 => 3.0, // 200% more on final try (desperate)
                _ => 1.0
            };
            
            return Math.Min(50, Math.Max(needed, (int)(needed * multiplier)));
        }

        private string BuildIterativePrompt(
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings,
            int requestSize,
            HashSet<string> rejectedAlbums,
            List<Recommendation> existingRecommendations)
        {
            // Use the base library-aware prompt
            var basePrompt = _promptBuilder.BuildLibraryAwarePrompt(profile, allArtists, allAlbums, settings);
            
            // Add iteration-specific context
            var iterativeContext = BuildIterativeContext(requestSize, rejectedAlbums, existingRecommendations);
            
            return basePrompt + "\n\n" + iterativeContext;
        }

        private string BuildIterativeContext(
            int requestSize,
            HashSet<string> rejectedAlbums,
            List<Recommendation> existingRecommendations)
        {
            var contextBuilder = new System.Text.StringBuilder();
            
            contextBuilder.AppendLine("🔄 ITERATIVE REQUEST CONTEXT:");
            contextBuilder.AppendLine($"• Requesting {requestSize} recommendations");
            
            if (rejectedAlbums.Any())
            {
                contextBuilder.AppendLine($"• Previously rejected {rejectedAlbums.Count} duplicates - avoid these patterns");
                
                // Show some examples of rejected albums to help AI learn
                var rejectedExamples = rejectedAlbums.Take(10).ToList();
                if (rejectedExamples.Any())
                {
                    contextBuilder.AppendLine($"• Recent duplicates to avoid: {string.Join(", ", rejectedExamples)}");
                }
            }
            
            if (existingRecommendations.Any())
            {
                contextBuilder.AppendLine($"• Already recommended {existingRecommendations.Count} albums in this session");
                
                // Show already recommended artists to encourage diversity
                var recommendedArtists = existingRecommendations
                    .Select(r => r.Artist)
                    .Distinct()
                    .Take(15)
                    .ToList();
                
                if (recommendedArtists.Any())
                {
                    contextBuilder.AppendLine($"• Already recommended artists: {string.Join(", ", recommendedArtists)}");
                    contextBuilder.AppendLine("• Try to diversify with different artists where possible");
                }
            }
            
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("💡 OPTIMIZATION HINTS:");
            contextBuilder.AppendLine("• Focus on lesser-known albums by known artists");
            contextBuilder.AppendLine("• Consider B-sides, live albums, or collaborations");
            contextBuilder.AppendLine("• Explore different eras of the same artists");
            
            return contextBuilder.ToString();
        }

        private (List<Recommendation> unique, List<Recommendation> duplicates) FilterAndTrackDuplicates(
            List<Recommendation> recommendations,
            HashSet<string> existingAlbums,
            List<Recommendation> alreadyRecommended,
            HashSet<string> rejectedAlbums)
        {
            var unique = new List<Recommendation>();
            var duplicates = new List<Recommendation>();
            
            var alreadyRecommendedKeys = alreadyRecommended
                .Select(r => NormalizeAlbumKey(r.Artist, r.Album))
                .ToHashSet();
            
            foreach (var rec in recommendations)
            {
                if (string.IsNullOrWhiteSpace(rec.Artist) || string.IsNullOrWhiteSpace(rec.Album))
                {
                    duplicates.Add(rec);
                    continue;
                }
                
                var albumKey = NormalizeAlbumKey(rec.Artist, rec.Album);
                
                // Check if it's a duplicate of existing library
                if (existingAlbums.Contains(albumKey))
                {
                    rejectedAlbums.Add(albumKey);
                    duplicates.Add(rec);
                    continue;
                }
                
                // Check if already recommended in this session
                if (alreadyRecommendedKeys.Contains(albumKey))
                {
                    duplicates.Add(rec);
                    continue;
                }
                
                unique.Add(rec);
                alreadyRecommendedKeys.Add(albumKey);
            }
            
            return (unique, duplicates);
        }

        private bool ShouldContinueIterating(double successRate, int currentCount, int targetCount, int iteration)
        {
            // Don't continue if we have enough recommendations
            if (currentCount >= targetCount)
                return false;
            
            // Don't exceed max iterations
            if (iteration >= MAX_ITERATIONS)
                return false;
            
            // Continue if success rate is too low (AI is giving too many duplicates)
            if (successRate < MIN_SUCCESS_RATE && iteration < MAX_ITERATIONS)
                return true;
            
            // Continue if we're significantly short of target
            var completionRate = (double)currentCount / targetCount;
            if (completionRate < 0.8)
                return true;
            
            return false;
        }

        private string NormalizeAlbumKey(string artist, string album)
        {
            // Consistent normalization for duplicate detection
            var normalizedArtist = artist?.Trim().ToLowerInvariant() ?? "";
            var normalizedAlbum = album?.Trim().ToLowerInvariant() ?? "";
            
            // Remove common variations that might cause false negatives
            normalizedArtist = System.Text.RegularExpressions.Regex.Replace(normalizedArtist, @"\s+", " ");
            normalizedAlbum = System.Text.RegularExpressions.Regex.Replace(normalizedAlbum, @"\s+", " ");
            
            return $"{normalizedArtist}_{normalizedAlbum}";
        }
    }
}