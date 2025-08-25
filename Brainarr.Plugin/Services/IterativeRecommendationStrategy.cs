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
    /// <summary>
    /// Implements an iterative strategy for obtaining music recommendations from AI providers.
    /// This strategy handles duplicate filtering, library deduplication, and iterative
    /// refinement to achieve the target number of unique recommendations.
    /// </summary>
    /// <remarks>
    /// The strategy works in multiple iterations:
    /// 1. Request initial batch of recommendations
    /// 2. Filter out existing library items and duplicates
    /// 3. If insufficient unique items, request more with context about rejected items
    /// 4. Continue until target count reached or max iterations exhausted
    /// 
    /// This approach significantly improves the quality and uniqueness of recommendations
    /// by providing the AI with feedback about what was already suggested or exists.
    /// </remarks>
    public class IterativeRecommendationStrategy
    {
        private readonly Logger _logger;
        private readonly ILibraryAwarePromptBuilder _promptBuilder;
        
        // Maximum number of iterations to prevent infinite loops
        private const int MAX_ITERATIONS = 3;
        // Minimum success rate to continue iterations (70% unique recommendations)
        private const double MIN_SUCCESS_RATE = 0.7;

        public IterativeRecommendationStrategy(Logger logger, ILibraryAwarePromptBuilder promptBuilder)
        {
            _logger = logger;
            _promptBuilder = promptBuilder;
        }

        /// <summary>
        /// Executes the iterative recommendation strategy to obtain unique music recommendations.
        /// </summary>
        /// <param name="provider">The AI provider to use for generating recommendations</param>
        /// <param name="profile">The user's library profile containing genre and artist preferences</param>
        /// <param name="allArtists">All artists in the user's library for context</param>
        /// <param name="allAlbums">All albums in the library to avoid duplicates</param>
        /// <param name="settings">Configuration settings including target recommendation count</param>
        /// <returns>A list of unique, validated recommendations not in the existing library</returns>
        /// <remarks>
        /// This method implements a feedback loop where each iteration learns from previous
        /// rejections to improve recommendation quality and reduce duplicates.
        /// </remarks>
        public async Task<List<Recommendation>> GetIterativeRecommendationsAsync(
            IAIProvider provider,
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings,
            bool shouldRecommendArtists = false)
        {
            var existingKeys = shouldRecommendArtists ? 
                BuildExistingArtistsSet(allArtists) : 
                BuildExistingAlbumsSet(allAlbums);
            var allRecommendations = new List<Recommendation>();
            var rejectedAlbums = new HashSet<string>();
            
            var targetCount = settings.MaxRecommendations;
            var iteration = 1;
            
            while (allRecommendations.Count < targetCount && iteration <= MAX_ITERATIONS)
            {
                _logger.Info($"Iteration {iteration}: Need {targetCount - allRecommendations.Count} more recommendations");
                
                // Dynamically adjust request size based on iteration and remaining needs
                // Later iterations request more to account for expected duplicates
                var requestSize = CalculateIterationRequestSize(targetCount - allRecommendations.Count, iteration);
                
                _logger.Debug($"Iteration {iteration}: Requesting {requestSize} recommendations from AI provider");
                
                // Build context-aware prompt that includes:
                // - Previous rejections to avoid repeats
                // - Already accepted recommendations for diversity
                // - Library context for better personalization
                var prompt = BuildIterativePrompt(
                    profile, 
                    allArtists, 
                    allAlbums, 
                    settings, 
                    requestSize,
                    rejectedAlbums,
                    allRecommendations,
                    shouldRecommendArtists);
                
                try
                {
                    // Get recommendations from AI
                    _logger.Debug($"Iteration {iteration}: Attempting to connect to AI provider for recommendations...");
                    var recommendations = await provider.GetRecommendationsAsync(prompt);
                    _logger.Debug($"Iteration {iteration}: Received {recommendations?.Count ?? 0} recommendations from AI provider");
                    
                    if (!recommendations.Any())
                    {
                        _logger.Warn($"Iteration {iteration}: No recommendations received");
                        break;
                    }
                    
                    // Filter out duplicates and track rejections
                    var (uniqueRecs, duplicates) = FilterAndTrackDuplicates(
                        recommendations, existingKeys, allRecommendations, rejectedAlbums, shouldRecommendArtists);
                    
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
                    _logger.Debug($"Iteration {iteration}: Exception details - Type: {ex.GetType().Name}, Message: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        _logger.Debug($"Iteration {iteration}: Inner exception - {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
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
        
        private HashSet<string> BuildExistingArtistsSet(List<Artist> allArtists)
        {
            return allArtists
                .Where(a => a.Name != null)
                .Select(a => NormalizeArtistKey(a.Name))
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
            List<Recommendation> existingRecommendations,
            bool shouldRecommendArtists = false)
        {
            // Use the base library-aware prompt with recommendation mode
            var basePrompt = _promptBuilder.BuildLibraryAwarePrompt(profile, allArtists, allAlbums, settings, shouldRecommendArtists);
            
            // Add iteration-specific context
            var iterativeContext = BuildIterativeContext(requestSize, rejectedAlbums, existingRecommendations, shouldRecommendArtists);
            
            return basePrompt + "\n\n" + iterativeContext;
        }

        private string BuildIterativeContext(
            int requestSize,
            HashSet<string> rejectedAlbums,
            List<Recommendation> existingRecommendations,
            bool shouldRecommendArtists = false)
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
            HashSet<string> existingKeys,
            List<Recommendation> alreadyRecommended,
            HashSet<string> rejectedAlbums,
            bool shouldRecommendArtists = false)
        {
            var unique = new List<Recommendation>();
            var duplicates = new List<Recommendation>();
            
            var alreadyRecommendedKeys = alreadyRecommended
                .Select(r => NormalizeAlbumKey(r.Artist, r.Album))
                .ToHashSet();
            
            foreach (var rec in recommendations)
            {
                // Always require artist name
                if (string.IsNullOrWhiteSpace(rec.Artist))
                {
                    duplicates.Add(rec);
                    continue;
                }
                
                // In album mode, require both artist and album
                // In artist mode, allow artist-only recommendations
                if (!shouldRecommendArtists && string.IsNullOrWhiteSpace(rec.Album))
                {
                    duplicates.Add(rec);
                    continue;
                }
                
                // For artist mode, use artist as key; for album mode, use artist+album
                var albumKey = shouldRecommendArtists ? 
                    NormalizeArtistKey(rec.Artist) : 
                    NormalizeAlbumKey(rec.Artist, rec.Album);
                
                // Check if it's a duplicate of existing library
                if (existingKeys.Contains(albumKey))
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
        
        private string NormalizeArtistKey(string artist)
        {
            // Consistent normalization for artist-only recommendations
            var normalizedArtist = artist?.Trim().ToLowerInvariant() ?? "";
            
            // Remove common variations that might cause false negatives
            normalizedArtist = System.Text.RegularExpressions.Regex.Replace(normalizedArtist, @"\s+", " ");
            
            return $"artist_{normalizedArtist}";
        }
    }
}