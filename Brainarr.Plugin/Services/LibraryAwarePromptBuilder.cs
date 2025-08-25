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
    public class LibraryAwarePromptBuilder : ILibraryAwarePromptBuilder
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
            _logger.Debug("LibraryAwarePromptBuilder instance created");
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
            BrainarrSettings settings,
            bool shouldRecommendArtists = false)
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
                
                var prompt = BuildPromptWithLibraryContext(profile, librarySample, settings, shouldRecommendArtists);
                
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

        private string BuildPromptWithLibraryContext(LibraryProfile profile, LibrarySample sample, BrainarrSettings settings, bool shouldRecommendArtists = false)
        {
            var promptBuilder = new StringBuilder();
            
            // Add sampling strategy context preamble
            var strategyPreamble = GetSamplingStrategyPreamble(settings.SamplingStrategy);
            if (!string.IsNullOrEmpty(strategyPreamble))
            {
                promptBuilder.AppendLine(strategyPreamble);
                promptBuilder.AppendLine();
            }
            
            // Use discovery mode-specific prompt template
            var modeTemplate = GetDiscoveryModeTemplate(settings.DiscoveryMode, settings.MaxRecommendations);
            promptBuilder.AppendLine(modeTemplate);
            promptBuilder.AppendLine();
            
            // Enhanced library overview with metadata
            promptBuilder.AppendLine($"📊 COLLECTION OVERVIEW:");
            promptBuilder.AppendLine(BuildEnhancedCollectionContext(profile));
            promptBuilder.AppendLine();
            
            // Musical preferences from metadata
            promptBuilder.AppendLine($"🎵 MUSICAL DNA:");
            promptBuilder.AppendLine(BuildMusicalDnaContext(profile));
            promptBuilder.AppendLine();
            
            // Collection patterns from metadata
            if (profile.Metadata != null && profile.Metadata.Any())
            {
                promptBuilder.AppendLine($"📈 COLLECTION PATTERNS:");
                promptBuilder.AppendLine(BuildCollectionPatterns(profile));
                promptBuilder.AppendLine();
            }
            
            // Existing artists (for context and avoidance)
            if (sample.Artists.Any())
            {
                promptBuilder.AppendLine($"🎶 LIBRARY ARTISTS ({sample.Artists.Count} sampled):");
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
            
            // Enhanced instructions with metadata context
            promptBuilder.AppendLine("🎯 RECOMMENDATION REQUIREMENTS:");
            
            if (shouldRecommendArtists)
            {
                promptBuilder.AppendLine($"1. DO NOT recommend any artists from the library shown above");
                promptBuilder.AppendLine($"2. Return EXACTLY {settings.MaxRecommendations} NEW ARTIST recommendations as JSON");
                promptBuilder.AppendLine($"3. Each must have: artist, genre, confidence (0.0-1.0), reason");
                promptBuilder.AppendLine($"4. Focus on ARTISTS (not specific albums) - Lidarr will import all their albums");
                promptBuilder.AppendLine($"5. Prefer studio album artists over live/compilation specialists");
            }
            else
            {
                promptBuilder.AppendLine($"1. DO NOT recommend any albums from the library shown above");
                promptBuilder.AppendLine($"2. Return EXACTLY {settings.MaxRecommendations} specific album recommendations as JSON");
                promptBuilder.AppendLine($"3. Each must have: artist, album, genre, year, confidence (0.0-1.0), reason");
                promptBuilder.AppendLine($"4. Prefer studio albums over live/compilation versions");
            }
            
            promptBuilder.AppendLine($"6. Match the collection's {GetCollectionCharacter(profile)} character");
            promptBuilder.AppendLine($"7. Align with {GetTemporalPreference(profile)} preferences");
            promptBuilder.AppendLine($"8. Consider {GetDiscoveryTrend(profile)} discovery pattern");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("JSON Response Format:");
            promptBuilder.AppendLine("[");
            promptBuilder.AppendLine("  {");
            promptBuilder.AppendLine("    \"artist\": \"Artist Name\",");
            
            if (!shouldRecommendArtists)
            {
                promptBuilder.AppendLine("    \"album\": \"Album Title\",");
                promptBuilder.AppendLine("    \"year\": 2024,");
            }
            
            promptBuilder.AppendLine("    \"genre\": \"Primary Genre\",");
            promptBuilder.AppendLine("    \"confidence\": 0.85,");
            
            if (shouldRecommendArtists)
            {
                promptBuilder.AppendLine("    \"reason\": \"New artist that matches your collection's style - Lidarr will import all their albums\"");
            }
            else
            {
                promptBuilder.AppendLine("    \"reason\": \"Specific album that matches your collection's character and preferences\"");
            }
            
            promptBuilder.AppendLine("  }");
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
        
        private string GetDiscoveryModeTemplate(DiscoveryMode mode, int maxRecommendations)
        {
            return mode switch
            {
                DiscoveryMode.Similar => 
                    $@"You are a music connoisseur tasked with finding {maxRecommendations} albums that perfectly match this user's established taste.
                    
OBJECTIVE: Recommend artists from the EXACT SAME subgenres and styles already in the collection.
- Look for artists frequently mentioned alongside their favorites
- Match production styles, era, and sonic characteristics precisely
- Prioritize artists who have collaborated with or influenced their collection
- NO genre-hopping; stay within their comfort zone
Example: If they love 80s synth-pop (Depeche Mode, New Order), recommend more 80s synth-pop, NOT modern synthwave or general electronic.",

                DiscoveryMode.Adjacent => 
                    $@"You are a music discovery expert helping expand this user's horizons into ADJACENT musical territories.
                    
OBJECTIVE: Recommend {maxRecommendations} albums from related but unexplored genres.
- Find the bridges between their current genres
- Recommend fusion genres that combine their interests
- Look for artists who started in their genres but evolved differently
- Include ""gateway"" albums that ease the transition
Example: If they love prog rock, suggest jazz fusion albums that prog fans typically enjoy (like Return to Forever or Mahavishnu Orchestra).",

                DiscoveryMode.Exploratory => 
                    $@"You are a bold music curator introducing this user to completely NEW musical experiences.
                    
OBJECTIVE: Recommend {maxRecommendations} albums from genres OUTSIDE their current collection.
- Choose critically acclaimed albums from unexplored genres
- Find ""entry points"" - accessible albums in new genres
- Consider opposites: If they're heavy on electronic, suggest acoustic
- Include world music, experimental, or niche genres they've never tried
Example: For a rock/metal fan, suggest Afrobeat (Fela Kuti), Bossa Nova (João Gilberto), or Ambient (Brian Eno).",

                _ => $"Analyze this music library and recommend {maxRecommendations} NEW albums that would enhance the collection:"
            };
        }
        
        private string GetSamplingStrategyPreamble(SamplingStrategy strategy)
        {
            return strategy switch
            {
                SamplingStrategy.Minimal => 
                    @"CONTEXT SCOPE: You have been provided with a brief summary of the user's top artists and genres. 
Based on this limited information, provide broad recommendations that align with their core tastes.",

                SamplingStrategy.Comprehensive => 
                    @"CONTEXT SCOPE: You have been provided with a highly detailed and comprehensive analysis of the user's music library. 
Use ALL available details (genre distributions, collection patterns, temporal preferences, completionist behavior) to generate deeply personalized recommendations.",

                SamplingStrategy.Balanced => 
                    @"CONTEXT SCOPE: You have been provided with a balanced overview of the user's library including key artists, genre preferences, and collection patterns. 
Use this information to provide well-informed recommendations that respect their established taste while offering meaningful discovery.",

                _ => string.Empty
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
                
                // Budget providers - configurable token usage
                (SamplingStrategy.Minimal, _) => MINIMAL_TOKEN_LIMIT,
                (SamplingStrategy.Balanced, _) => BALANCED_TOKEN_LIMIT,
                (SamplingStrategy.Comprehensive, _) => COMPREHENSIVE_TOKEN_LIMIT,
                
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
        
        private string BuildEnhancedCollectionContext(LibraryProfile profile)
        {
            var context = new StringBuilder();
            
            var collectionSize = profile.Metadata?.ContainsKey("CollectionSize") == true 
                ? profile.Metadata["CollectionSize"].ToString() 
                : "established";
            
            var collectionFocus = profile.Metadata?.ContainsKey("CollectionFocus") == true
                ? profile.Metadata["CollectionFocus"].ToString()
                : "general";
            
            context.AppendLine($"• Size: {collectionSize} ({profile.TotalArtists} artists, {profile.TotalAlbums} albums)");
            
            // Enhanced genre display with distribution
            if (profile.Metadata?.ContainsKey("GenreDistribution") == true)
            {
                var genreDistribution = profile.Metadata["GenreDistribution"] as Dictionary<string, double>;
                if (genreDistribution?.Any() == true)
                {
                    var topGenres = string.Join(", ", genreDistribution.Take(5).Select(g => $"{g.Key} ({g.Value:F1}%)"));
                    context.AppendLine($"• Genres: {topGenres}");
                }
            }
            else
            {
                context.AppendLine($"• Genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => g.Key))}");
            }
            
            // Collection style and completionist behavior
            if (profile.Metadata?.ContainsKey("CollectionStyle") == true)
            {
                var collectionStyle = profile.Metadata["CollectionStyle"].ToString();
                var completionistScore = profile.Metadata?.ContainsKey("CompletionistScore") == true 
                    ? profile.Metadata["CompletionistScore"] 
                    : null;
                
                context.AppendLine($"• Collection style: {collectionStyle}");
                if (completionistScore != null && double.TryParse(completionistScore.ToString(), out var score))
                {
                    context.AppendLine($"• Completionist score: {score:F1}% (avg {profile.Metadata?["AverageAlbumsPerArtist"]:F1} albums per artist)");
                }
            }
            
            context.AppendLine($"• Collection type: {collectionFocus}");
            
            return context.ToString().TrimEnd();
        }
        
        private string BuildMusicalDnaContext(LibraryProfile profile)
        {
            var context = new StringBuilder();
            
            // Release decades
            if (profile.Metadata?.ContainsKey("ReleaseDecades") == true)
            {
                var decades = profile.Metadata["ReleaseDecades"] as List<string>;
                if (decades?.Any() == true)
                {
                    context.AppendLine($"• Era focus: {string.Join(", ", decades)}");
                }
            }
            
            // Preferred eras
            if (profile.Metadata?.ContainsKey("PreferredEras") == true)
            {
                var eras = profile.Metadata["PreferredEras"] as List<string>;
                if (eras?.Any() == true)
                {
                    context.AppendLine($"• Era preference: {string.Join(", ", eras)}");
                }
            }
            
            // Album types
            if (profile.Metadata?.ContainsKey("AlbumTypes") == true)
            {
                var albumTypes = profile.Metadata["AlbumTypes"] as Dictionary<string, int>;
                if (albumTypes?.Any() == true)
                {
                    var topTypes = string.Join(", ", albumTypes.Take(3).Select(t => $"{t.Key} ({t.Value})"));
                    context.AppendLine($"• Album types: {topTypes}");
                }
            }
            
            // New release interest
            if (profile.Metadata?.ContainsKey("NewReleaseRatio") == true)
            {
                var ratio = Convert.ToDouble(profile.Metadata["NewReleaseRatio"]);
                var interest = ratio > 0.3 ? "High" : ratio > 0.15 ? "Moderate" : "Low";
                context.AppendLine($"• New release interest: {interest} ({ratio:P0} recent)");
            }
            
            return context.ToString().TrimEnd();
        }
        
        private string BuildCollectionPatterns(LibraryProfile profile)
        {
            var context = new StringBuilder();
            
            // Discovery trend
            if (profile.Metadata?.ContainsKey("DiscoveryTrend") == true)
            {
                context.AppendLine($"• Discovery trend: {profile.Metadata["DiscoveryTrend"]}");
            }
            
            // Collection quality
            if (profile.Metadata?.ContainsKey("CollectionCompleteness") == true)
            {
                var completeness = Convert.ToDouble(profile.Metadata["CollectionCompleteness"]);
                var quality = completeness > 0.8 ? "Very High" : completeness > 0.6 ? "High" : completeness > 0.4 ? "Moderate" : "Building";
                context.AppendLine($"• Collection quality: {quality} ({completeness:P0} complete)");
            }
            
            // Monitoring ratio
            if (profile.Metadata?.ContainsKey("MonitoredRatio") == true)
            {
                var ratio = Convert.ToDouble(profile.Metadata["MonitoredRatio"]);
                context.AppendLine($"• Active tracking: {ratio:P0} of collection");
            }
            
            // Average depth
            if (profile.Metadata?.ContainsKey("AverageAlbumsPerArtist") == true)
            {
                var avg = Convert.ToDouble(profile.Metadata["AverageAlbumsPerArtist"]);
                context.AppendLine($"• Collection depth: {avg:F1} albums per artist");
            }
            
            return context.ToString().TrimEnd();
        }
        
        private string GetCollectionCharacter(LibraryProfile profile)
        {
            if (profile.Metadata?.ContainsKey("CollectionFocus") == true)
            {
                return profile.Metadata["CollectionFocus"].ToString();
            }
            return "balanced";
        }
        
        private string GetTemporalPreference(LibraryProfile profile)
        {
            if (profile.Metadata?.ContainsKey("PreferredEras") == true)
            {
                var eras = profile.Metadata["PreferredEras"] as List<string>;
                if (eras?.Any() == true)
                {
                    return string.Join("/", eras).ToLower();
                }
            }
            return "mixed era";
        }
        
        private string GetDiscoveryTrend(LibraryProfile profile)
        {
            if (profile.Metadata?.ContainsKey("DiscoveryTrend") == true)
            {
                return profile.Metadata["DiscoveryTrend"].ToString();
            }
            return "steady";
        }
    }
    
    public class LibrarySample
    {
        public List<string> Artists { get; set; } = new List<string>();
        public List<string> Albums { get; set; } = new List<string>();
    }
}