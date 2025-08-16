using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Implements an iterative recommendation strategy that progressively refines results
    /// through multiple AI provider calls to overcome duplicate recommendations and
    /// achieve the target recommendation count.
    /// </summary>
    /// <remarks>
    /// The strategy addresses the challenge where AI providers often suggest albums
    /// already in the user's library. By tracking rejected duplicates and providing
    /// this context back to the AI in subsequent iterations, we achieve higher
    /// success rates and better diversity.
    /// </remarks>
    public class IterativeRecommendationStrategy
    {
        private readonly Logger _logger;
        private readonly LibraryAwarePromptBuilder _promptBuilder;
        
        // Maximum iterations to prevent infinite loops while allowing refinement
        private const int MAX_ITERATIONS = 3;
        
        // Minimum acceptable success rate (unique recommendations / total received)
        // Below 70% indicates the AI is giving too many duplicates and needs more context
        private const double MIN_SUCCESS_RATE = 0.7; // At least 70% unique recommendations

        public IterativeRecommendationStrategy(Logger logger, LibraryAwarePromptBuilder promptBuilder)
        {
            _logger = logger;
            _promptBuilder = promptBuilder;
        }

        /// <summary>
        /// Executes an iterative recommendation strategy that progressively builds a list
        /// of unique recommendations by learning from rejected duplicates.
        /// </summary>
        /// <param name="provider">The AI provider to use for recommendations</param>
        /// <param name="profile">Statistical profile of the user's music library</param>
        /// <param name="allArtists">Complete list of artists in the library</param>
        /// <param name="allAlbums">Complete list of albums in the library</param>
        /// <param name="settings">Configuration settings including target count</param>
        /// <returns>List of unique recommendations up to MaxRecommendations count</returns>
        /// <remarks>
        /// Algorithm:
        /// 1. First iteration: Request 1.5x needed count to account for duplicates
        /// 2. Track all duplicates found and provide them as context
        /// 3. Subsequent iterations: Increase over-request factor as AI learns
        /// 4. Stop when target reached, max iterations hit, or success rate too low
        /// </remarks>
        public async Task<List<Recommendation>> GetIterativeRecommendationsAsync(
            IAIProvider provider,
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings)
        {
            // Build normalized set of existing albums for O(1) duplicate detection
            var existingAlbums = BuildExistingAlbumsSet(allAlbums);
            var allRecommendations = new List<Recommendation>();
            var rejectedAlbums = new HashSet<string>();  // Track duplicates for context
            
            var targetCount = settings.MaxRecommendations;
            var iteration = 1;
            
            // Iterative refinement loop
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
                    
                    // Evaluate whether another iteration would be beneficial
                    // based on success rate, progress toward target, and iteration count
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

        /// <summary>
        /// Calculates the number of recommendations to request in each iteration,
        /// progressively increasing the over-request factor to compensate for duplicates.
        /// </summary>
        /// <param name="needed">Number of recommendations still needed</param>
        /// <param name="iteration">Current iteration number (1-based)</param>
        /// <returns>Number of recommendations to request from AI</returns>
        /// <remarks>
        /// The multiplier increases with each iteration based on empirical observations:
        /// - Iteration 1: 1.5x (AI typically has 30-40% duplicates on first try)
        /// - Iteration 2: 2.0x (With context, duplicates drop but still present)
        /// - Iteration 3: 3.0x (Final attempt with maximum over-request)
        /// Capped at 50 to prevent token limit issues and excessive processing.
        /// </remarks>
        private int CalculateIterationRequestSize(int needed, int iteration)
        {
            // Request more than needed to account for duplicates, with increasing multiplier
            var multiplier = iteration switch
            {
                1 => 1.5, // 50% more on first try
                2 => 2.0, // 100% more on second try (AI should learn)
                3 => 3.0, // 200% more on final try (desperate)
                _ => 1.0
            };
            
            // Cap at 50 to avoid token limits, ensure at least 'needed' count
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

        /// <summary>
        /// Determines whether to continue iterating based on multiple factors including
        /// success rate, completion percentage, and iteration count.
        /// </summary>
        /// <param name="successRate">Ratio of unique recommendations to total received (0-1)</param>
        /// <param name="currentCount">Current number of unique recommendations collected</param>
        /// <param name="targetCount">Target number of recommendations needed</param>
        /// <param name="iteration">Current iteration number</param>
        /// <returns>True if another iteration should be attempted</returns>
        /// <remarks>
        /// Decision logic:
        /// 1. Stop if target reached (success)
        /// 2. Stop if max iterations reached (prevent infinite loops)
        /// 3. Continue if success rate < 70% (AI needs more context about duplicates)
        /// 4. Continue if completion < 80% (significantly short of target)
        /// 5. Otherwise stop (good enough results)
        /// </remarks>
        private bool ShouldContinueIterating(double successRate, int currentCount, int targetCount, int iteration)
        {
            // Don't continue if we have enough recommendations
            if (currentCount >= targetCount)
                return false;
            
            // Don't exceed max iterations to prevent infinite loops
            if (iteration >= MAX_ITERATIONS)
                return false;
            
            // Continue if success rate is too low (AI is giving too many duplicates)
            // This indicates the AI needs more context about what to avoid
            if (successRate < MIN_SUCCESS_RATE && iteration < MAX_ITERATIONS)
                return true;
            
            // Continue if we're significantly short of target (< 80% complete)
            // This threshold balances getting enough results vs. diminishing returns
            var completionRate = (double)currentCount / targetCount;
            if (completionRate < 0.8)
                return true;
            
            return false;
        }

        /// <summary>
        /// Normalizes artist and album names into a consistent key format for
        /// reliable duplicate detection across different string variations.
        /// </summary>
        /// <param name="artist">Artist name to normalize</param>
        /// <param name="album">Album name to normalize</param>
        /// <returns>Normalized key in format "artist_album"</returns>
        /// <remarks>
        /// Normalization process:
        /// 1. Convert to lowercase for case-insensitive comparison
        /// 2. Trim whitespace from start/end
        /// 3. Collapse multiple spaces to single space (handles formatting variations)
        /// 4. Combine with underscore separator
        /// This prevents false negatives from minor formatting differences while
        /// maintaining enough specificity to avoid false positives.
        /// </remarks>
        private string NormalizeAlbumKey(string artist, string album)
        {
            // Consistent normalization for duplicate detection
            var normalizedArtist = artist?.Trim().ToLowerInvariant() ?? "";
            var normalizedAlbum = album?.Trim().ToLowerInvariant() ?? "";
            
            // Remove common variations that might cause false negatives
            // Collapse multiple spaces/tabs/newlines to single space
            normalizedArtist = System.Text.RegularExpressions.Regex.Replace(normalizedArtist, @"\s+", " ");
            normalizedAlbum = System.Text.RegularExpressions.Regex.Replace(normalizedAlbum, @"\s+", " ");
            
            return $"{normalizedArtist}_{normalizedAlbum}";
        }
    }
}