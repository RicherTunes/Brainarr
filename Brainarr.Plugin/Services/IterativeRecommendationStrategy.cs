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
    public class IterativeRecommendationStrategy
    {
        private readonly Logger _logger;
        private readonly ILibraryAwarePromptBuilder _promptBuilder;

        private const int MAX_ITERATIONS = 3;
        private const double MIN_SUCCESS_RATE = 0.7; // At least 70% unique recommendations

        public IterativeRecommendationStrategy(Logger logger, ILibraryAwarePromptBuilder promptBuilder)
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
            var existingArtists = BuildExistingArtistsSet(allArtists);
            var allRecommendations = new List<Recommendation>();
            var rejectedAlbums = new HashSet<string>();
            var rejectedArtists = new HashSet<string>();

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
                    allRecommendations,
                    iteration);

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
                        recommendations, existingAlbums, existingArtists, allRecommendations, rejectedAlbums, rejectedArtists);

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

        private HashSet<string> BuildExistingArtistsSet(List<Artist> allArtists)
        {
            var existingArtists = new HashSet<string>();

            foreach (var artist in allArtists)
            {
                if (!string.IsNullOrWhiteSpace(artist.Name))
                {
                    var normalizedName = NormalizeArtistName(artist.Name);
                    existingArtists.Add(normalizedName);

                    // Also add artist metadata name variants if available
                    if (artist.Metadata?.Value?.Name != null && artist.Metadata.Value.Name != artist.Name)
                    {
                        var metadataName = NormalizeArtistName(artist.Metadata.Value.Name);
                        existingArtists.Add(metadataName);
                    }
                }
            }

            // Always include variations of "Various Artists" to prevent recommendations that map to it
            var variousArtistsVariants = new[]
            {
                "various artists",
                "various",
                "compilation",
                "soundtrack",
                "ost",
                "original soundtrack",
                "multiple artists",
                "mixed artists",
                "va"
            };

            foreach (var variant in variousArtistsVariants)
            {
                existingArtists.Add(NormalizeArtistName(variant));
            }

            _logger.Debug($"Built existing artists set with {existingArtists.Count} artists (including Various Artists variants)");
            return existingArtists;
        }

        private int CalculateIterationRequestSize(int needed, int iteration)
        {
            // Be VERY aggressive to avoid multiple iterations
            var multiplier = iteration switch
            {
                1 => 2.5,  // Request 2.5x on first try (75 for 30 needed)
                2 => 2.0,  // 100% more on second try 
                3 => 1.5,  // 50% more on final try
                _ => 1.2
            };

            // Don't cap at 50 - modern models can handle more
            var requestSize = (int)(needed * multiplier);
            _logger.Info($"Iteration {iteration}: Requesting {requestSize} items (need {needed}, multiplier {multiplier:F1}x)");
            return requestSize;
        }

        private string BuildIterativePrompt(
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings,
            int requestSize,
            HashSet<string> rejectedAlbums,
            List<Recommendation> existingRecommendations,
            int iteration)
        {
            // Use the base library-aware prompt
            var basePrompt = _promptBuilder.BuildLibraryAwarePrompt(profile, allArtists, allAlbums, settings);

            // Add iteration-specific context
            var iterativeContext = BuildIterativeContext(requestSize, rejectedAlbums, existingRecommendations, iteration);

            return basePrompt + "\n\n" + iterativeContext;
        }

        private string BuildIterativeContext(
            int requestSize,
            HashSet<string> rejectedAlbums,
            List<Recommendation> existingRecommendations,
            int iteration = 1)
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
            contextBuilder.AppendLine("💡 CRITICAL ITERATION HINTS:");
            contextBuilder.AppendLine($"• This is attempt {iteration} of {MAX_ITERATIONS} - be more creative!");
            contextBuilder.AppendLine("• AVOID generic artist names like 'Luna Shadows', 'The Velvet [anything]'");
            contextBuilder.AppendLine("• Use REAL, VERIFIABLE artists that exist on MusicBrainz");
            contextBuilder.AppendLine("• If unsure about an artist, pick a different one");
            contextBuilder.AppendLine("• Focus on actual albums released by real artists");

            if (iteration > 1)
            {
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("⚠️ PREVIOUS ATTEMPT FAILED - TRY DIFFERENT APPROACH:");
                contextBuilder.AppendLine("• Switch to different sub-genres");
                contextBuilder.AppendLine("• Try artists from different decades");
                contextBuilder.AppendLine("• Consider international artists");
            }

            return contextBuilder.ToString();
        }

        private (List<Recommendation> unique, List<Recommendation> duplicates) FilterAndTrackDuplicates(
            List<Recommendation> recommendations,
            HashSet<string> existingAlbums,
            HashSet<string> existingArtists,
            List<Recommendation> alreadyRecommended,
            HashSet<string> rejectedAlbums,
            HashSet<string> rejectedArtists)
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

                var normalizedArtist = NormalizeArtistName(rec.Artist);
                var albumKey = NormalizeAlbumKey(rec.Artist, rec.Album);

                // Check if artist already exists in library (prevents duplicate artists)
                if (existingArtists.Contains(normalizedArtist))
                {
                    _logger.Debug($"Rejecting '{rec.Artist} - {rec.Album}': Artist already exists in library");
                    rejectedArtists.Add(normalizedArtist);
                    duplicates.Add(rec);
                    continue;
                }

                // Check if it's a duplicate album of existing library
                if (existingAlbums.Contains(albumKey))
                {
                    _logger.Debug($"Rejecting '{rec.Artist} - {rec.Album}': Album already exists in library");
                    rejectedAlbums.Add(albumKey);
                    duplicates.Add(rec);
                    continue;
                }

                // Check if already recommended in this session
                if (alreadyRecommendedKeys.Contains(albumKey))
                {
                    _logger.Debug($"Rejecting '{rec.Artist} - {rec.Album}': Already recommended in this session");
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
            var normalizedArtist = NormalizeArtistName(artist);
            var normalizedAlbum = album?.Trim().ToLowerInvariant() ?? "";

            // Remove common variations that might cause false negatives
            normalizedAlbum = System.Text.RegularExpressions.Regex.Replace(normalizedAlbum, @"\s+", " ");

            return $"{normalizedArtist}_{normalizedAlbum}";
        }

        private string NormalizeArtistName(string artist)
        {
            if (string.IsNullOrWhiteSpace(artist)) return "";

            var normalized = artist.Trim().ToLowerInvariant();

            // Remove common variations
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");

            // Remove "The" prefix for better matching (e.g., "The Beatles" -> "beatles")
            if (normalized.StartsWith("the "))
                normalized = normalized.Substring(4);

            // Remove special characters that might cause mismatches
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[''""´`]", "");

            return normalized;
        }
    }
}