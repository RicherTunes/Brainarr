using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class LibraryAwarePromptBuilder : ILibraryAwarePromptBuilder
    {
        private readonly Logger _logger;
        private readonly EnhancedPromptBuilder _enhancedBuilder;
        
        // Token estimation constants (rough approximations)
        private const double TOKENS_PER_WORD = 1.3; // GPT-style tokenization
        private const int BASE_PROMPT_TOKENS = 300; // Estimated tokens for base prompt structure
        
        // Token limits by sampling strategy and provider type
        // Modern local models can handle MUCH larger contexts efficiently
        private const int MINIMAL_TOKEN_LIMIT = 8000;      // For local models (Ollama, LM Studio) - Qwen3 can handle 128k+
        private const int BALANCED_TOKEN_LIMIT = 12000;    // Default for most providers - use their capacity
        private const int COMPREHENSIVE_TOKEN_LIMIT = 20000; // For premium providers - they can handle it
        
        // Response optimization
        private const int MAX_RESPONSE_TOKENS = 4000; // Allow fuller responses
        private const int ITEMS_PER_BATCH = 50; // Ask for 50 to get 30 after filtering
        
        public LibraryAwarePromptBuilder(Logger logger)
        {
            _logger = logger;
            _enhancedBuilder = new EnhancedPromptBuilder(logger);
        }

        public virtual string BuildLibraryAwarePrompt(
            LibraryProfile profile,
            IList<Artist> allArtists,
            IList<Album> allAlbums,
            BrainarrSettings settings,
            SamplingStrategy strategy = SamplingStrategy.Balanced)
        {
            try
            {
                // Calculate available token budget based on sampling strategy and provider
                var maxTokens = GetTokenLimitForStrategy(strategy, settings.Provider);
                var availableTokens = maxTokens - BASE_PROMPT_TOKENS;
                
                // Smart sampling of library data based on token constraints
                var librarySample = BuildSmartLibrarySample(allArtists, allAlbums, availableTokens);
                
                // Use enhanced prompt for comprehensive mode, or when target genres/moods/eras are specified
                var useEnhancedPrompt = settings.SamplingStrategy == SamplingStrategy.Comprehensive ||
                                      !string.IsNullOrWhiteSpace(settings.TargetGenres) ||
                                      (settings.MusicMood?.Any() == true) ||
                                      (settings.MusicEra?.Any() == true);
                
                var prompt = useEnhancedPrompt 
                    ? _enhancedBuilder.BuildDJExpertPrompt(profile, librarySample, settings)
                    : BuildPromptWithLibraryContext(profile, librarySample, settings);
                
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

        private LibrarySample BuildSmartLibrarySample(List<Artist> allArtists, List<Album> allAlbums, int tokenBudget)
        {
            var sample = new LibrarySample();
            
            // Much more aggressive sampling - we have the token budget now!
            if (allArtists.Count <= 100)
            {
                // Small library: Include ALL artists and many albums
                sample.Artists = allArtists.Select(a => a.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
                sample.Albums = allAlbums.Take(Math.Min(500, allAlbums.Count))
                    .Select(a => $"{a.ArtistMetadata?.Value?.Name} - {a.Title}")
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
            }
            else if (allArtists.Count <= 500)
            {
                // Medium library: Include most artists strategically
                sample.Artists = SampleArtistsStrategically(allArtists, allAlbums, Math.Min(300, allArtists.Count));
                sample.Albums = SampleAlbumsStrategically(allAlbums, 400);
            }
            else
            {
                // Large library: Still be smart but use much more context
                sample = BuildTokenConstrainedSample(allArtists, allAlbums, tokenBudget);
            }
            
            _logger.Info($"Library sample built: {sample.Artists.Count} artists, {sample.Albums.Count} albums for {tokenBudget} token budget");
            
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
            
            // With much higher token budgets, we can be more generous
            var artistBudget = (int)(tokenBudget * 0.5); // 50% for artists
            var albumBudget = (int)(tokenBudget * 0.4);  // 40% for albums
            // 10% reserved for prompt structure
            
            // Start with essential artists (most prolific)
            var artistAlbumCounts = allAlbums
                .GroupBy(a => a.ArtistId)
                .ToDictionary(g => g.Key, g => g.Count());
            
            var prioritizedArtists = allArtists
                .Where(a => artistAlbumCounts.ContainsKey(a.Id))
                .OrderByDescending(a => artistAlbumCounts[a.Id])
                .ThenByDescending(a => a.Added) // Also prioritize recently added
                .Select(a => a.Name)
                .Where(name => !string.IsNullOrEmpty(name));
            
            foreach (var artist in prioritizedArtists)
            {
                var artistTokens = EstimateTokens(artist);
                if (usedTokens + artistTokens > artistBudget) break;
                
                sample.Artists.Add(artist);
                usedTokens += artistTokens;
            }
            
            // Add recent albums with album budget
            var albumTokensUsed = 0;
            var recentAlbums = allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .OrderByDescending(a => a.Added)
                .ThenByDescending(a => a.Ratings?.Value ?? 0) // Prioritize highly rated
                .Select(a => $"{a.ArtistMetadata.Value.Name} - {a.Title}");
            
            foreach (var album in recentAlbums)
            {
                var albumTokens = EstimateTokens(album);
                if (albumTokensUsed + albumTokens > albumBudget) break;
                
                sample.Albums.Add(album);
                albumTokensUsed += albumTokens;
            }
            
            _logger.Debug($"Token-constrained sampling: {sample.Artists.Count} artists, " +
                         $"{sample.Albums.Count} albums, estimated {usedTokens} tokens");
            
            return sample;
        }

        private string BuildPromptWithLibraryContext(LibraryProfile profile, LibrarySample sample, BrainarrSettings settings)
        {
            var promptBuilder = new StringBuilder();
            
            // Build context based on RecommendationType
            var targetType = settings.RecommendationType == RecommendationType.Artists ? "ARTISTS" : "ALBUMS";
            var targetDescription = settings.RecommendationType == RecommendationType.Artists 
                ? "new ARTISTS to explore (any album from their discography)"
                : "specific ALBUMS from new artists not in the collection";
            
            promptBuilder.AppendLine($"🎯 RECOMMENDATION TASK: Find {settings.MaxRecommendations} {targetDescription}");
            promptBuilder.AppendLine();
            
            // Library overview with user preferences
            promptBuilder.AppendLine($"📊 Library Overview:");
            promptBuilder.AppendLine($"• Total: {profile.TotalArtists} artists, {profile.TotalAlbums} albums");
            promptBuilder.AppendLine($"• Library genres: {string.Join(", ", profile.TopGenres.Take(3).Select(g => g.Key))}");
            
            // Add user-specified preferences
            if (!string.IsNullOrWhiteSpace(settings.TargetGenres))
            {
                promptBuilder.AppendLine($"• TARGET GENRES: {settings.TargetGenres} (user specified)");
            }
            
            var selectedMoods = GetSelectedMoods(settings.MusicMood);
            if (selectedMoods.Any())
            {
                promptBuilder.AppendLine($"• TARGET MOODS: {string.Join(", ", selectedMoods)} (user specified)");
            }
            
            var selectedEras = GetSelectedEras(settings.MusicEra);
            if (selectedEras.Any())
            {
                promptBuilder.AppendLine($"• TARGET ERAS: {string.Join(", ", selectedEras)} (user specified)");
            }
            
            promptBuilder.AppendLine($"• Discovery mode: {GetDiscoveryModeDescription(settings.DiscoveryMode)}");
            promptBuilder.AppendLine($"• Target: {targetType} recommendations");
            promptBuilder.AppendLine();
            
            // Add positive examples for AI guidance
            AddPositiveExamples(promptBuilder, sample, profile, settings);
            promptBuilder.AppendLine();
            
            // Existing artists (for context and avoidance)
            if (sample.Artists.Any())
            {
                promptBuilder.AppendLine($"🎵 Library Artists ({sample.Artists.Count} shown) - DO NOT RECOMMEND ALBUMS BY THESE ARTISTS:");
                promptBuilder.AppendLine(string.Join(", ", sample.Artists));
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("⚠️ CRITICAL: These artists are already in the library. Recommend NEW artists instead!");
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
            
            // Updated instructions based on recommendation type
            promptBuilder.AppendLine("🎯 CRITICAL REQUIREMENTS:");
            
            if (settings.RecommendationType == RecommendationType.Artists)
            {
                promptBuilder.AppendLine("1. Recommend NEW ARTISTS not in the library above");
                promptBuilder.AppendLine("2. For each artist, pick ANY representative album from their discography");
                promptBuilder.AppendLine("3. Each recommendation must be a DIFFERENT artist");
                promptBuilder.AppendLine("4. Focus on artists that match the discovery mode and musical taste shown");
            }
            else
            {
                promptBuilder.AppendLine("1. Recommend specific ALBUMS from NEW artists not in the library");
                promptBuilder.AppendLine("2. DO NOT recommend albums by artists already shown above");
                promptBuilder.AppendLine("3. Each album should be from a DIFFERENT artist (max 1 album per artist)");
                promptBuilder.AppendLine("4. Focus on standout albums that fit the musical profile");
            }
            
            promptBuilder.AppendLine($"5. Return EXACTLY {settings.MaxRecommendations} UNIQUE recommendations");
            promptBuilder.AppendLine("6. Each must have: artist, album, genre, confidence (0.0-1.0), reason");
            promptBuilder.AppendLine("7. Use the library context above to inform your choices");
            promptBuilder.AppendLine("8. NEVER recommend 'Various Artists', 'Soundtrack', 'Compilation', or similar generic artists");
            promptBuilder.AppendLine("9. Always recommend SPECIFIC, NAMED artists with clear discographies");
            promptBuilder.AppendLine();
            
            // Add diversity hints based on discovery mode
            if (settings.DiscoveryMode == DiscoveryMode.Adjacent || settings.DiscoveryMode == DiscoveryMode.Exploratory)
            {
                promptBuilder.AppendLine("🌈 DIVERSITY FOCUS:");
                promptBuilder.AppendLine("• Recommend artists from DIFFERENT genres when possible");
                promptBuilder.AppendLine("• Include both popular and lesser-known artists");
                promptBuilder.AppendLine("• Mix different time periods and styles");
                promptBuilder.AppendLine();
            }
            
            // CRITICAL: Add response optimization instructions
            promptBuilder.AppendLine("⚡ RESPONSE OPTIMIZATION:");
            promptBuilder.AppendLine($"• Generate EXACTLY {settings.MaxRecommendations} valid recommendations");
            promptBuilder.AppendLine("• Keep reasons BRIEF (10 words max) to save tokens");
            promptBuilder.AppendLine("• Use REAL, EXISTING artists only - no made-up names");
            promptBuilder.AppendLine("• Ensure valid JSON format - close all brackets properly");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("Response format (COMPACT for efficiency):");
            promptBuilder.AppendLine("[");
            promptBuilder.AppendLine("  {\"artist\": \"Real Artist\", \"album\": \"Real Album\", \"genre\": \"Genre\", \"confidence\": 0.8, \"reason\": \"Brief reason\"}");
            promptBuilder.AppendLine("]");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("IMPORTANT: Complete ALL recommendations before token limit!");
            
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
                DiscoveryMode.Similar => "NEW artists very similar to the library taste",
                DiscoveryMode.Adjacent => "NEW artists in related but different genres",
                DiscoveryMode.Exploratory => "NEW artists in completely different genres to explore",
                _ => "balanced recommendations from NEW artists"
            };
        }

        private string GetDiscoveryModeDescription(DiscoveryMode mode)
        {
            return mode switch
            {
                DiscoveryMode.Similar => "Find artists very similar to current collection (safe expansion)",
                DiscoveryMode.Adjacent => "Discover artists in related/adjacent genres (balanced exploration)",  
                DiscoveryMode.Exploratory => "Explore completely new genres and styles (adventurous discovery)",
                _ => "Balanced recommendations mixing familiar and new"
            };
        }

        private List<string> GetSelectedMoods(IEnumerable<int> moodOptions)
        {
            var moods = new List<string>();
            
            if (moodOptions == null || !moodOptions.Any()) return moods; // Empty = library analysis
            
            var moodMap = new Dictionary<int, string>
            {
                { 1, "energetic/driving" },        // Energetic
                { 2, "chill/mellow" },             // Chill
                { 3, "dark/brooding" },            // Dark
                { 4, "emotional/heartfelt" },      // Emotional
                { 5, "experimental/avant-garde" }, // Experimental
                { 6, "danceable/rhythmic" },       // Danceable
                { 7, "aggressive/intense" },       // Aggressive
                { 8, "peaceful/meditative" },      // Peaceful
                { 9, "melancholic/introspective" },// Melancholic
                { 10, "uplifting/inspiring" },     // Uplifting
                { 11, "mysterious/ethereal" },     // Mysterious
                { 12, "playful/whimsical" },       // Playful
                { 13, "epic/cinematic" },          // Epic
                { 14, "intimate/personal" },       // Intimate
                { 15, "groovy/funky" },            // Groovy
                { 16, "nostalgic/evocative" }      // Nostalgic
            };
            
            foreach (var moodId in moodOptions.Where(id => id > 0)) // Skip "Any" (0)
            {
                if (moodMap.TryGetValue(moodId, out var moodDescription))
                {
                    moods.Add(moodDescription);
                }
            }
            
            return moods;
        }

        private List<string> GetSelectedEras(IEnumerable<int> eraOptions)
        {
            var eras = new List<string>();
            
            if (eraOptions == null || !eraOptions.Any()) return eras; // Empty = library analysis
            
            var eraMap = new Dictionary<int, string>
            {
                { 1, "1900s-1940s (early jazz/blues)" },    // PreRock
                { 2, "1950s (birth of rock)" },             // EarlyRock
                { 3, "1960s (British invasion/Motown)" },   // SixtiesPop
                { 4, "1970s (classic rock/progressive)" },  // SeventiesRock
                { 5, "1980s (new wave/synth-pop)" },        // EightiesPop
                { 6, "1990s (grunge/alternative)" },        // NinetiesAlt
                { 7, "2000s (nu-metal/pop-punk)" },         // MillenniumPop
                { 8, "2010s (indie/streaming era)" },       // TensSocial
                { 9, "2020s+ (current trends)" },           // Modern
                { 10, "vintage (50s-70s classics)" },       // Vintage
                { 11, "retro (80s-90s)" },                  // Retro
                { 12, "contemporary (last 3-5 years)" }     // Contemporary
            };
            
            foreach (var eraId in eraOptions.Where(id => id > 0)) // Skip "Any" (0)
            {
                if (eraMap.TryGetValue(eraId, out var eraDescription))
                {
                    eras.Add(eraDescription);
                }
            }
            
            return eras;
        }

        private void AddPositiveExamples(StringBuilder promptBuilder, LibrarySample sample, LibraryProfile profile, BrainarrSettings settings)
        {
            promptBuilder.AppendLine("🎵 LIBRARY CONTEXT - Use these as reference for musical taste:");
            
            // Select representative examples based on discovery mode
            var exampleArtists = SelectExampleArtists(sample.Artists, profile, settings.DiscoveryMode, 8);
            var exampleAlbums = SelectExampleAlbums(sample.Albums, profile, settings.DiscoveryMode, 6);
            
            if (exampleArtists.Any())
            {
                promptBuilder.AppendLine($"• Representative artists in library: {string.Join(", ", exampleArtists)}");
            }
            
            if (exampleAlbums.Any())
            {
                promptBuilder.AppendLine($"• Example albums they enjoy: {string.Join(", ", exampleAlbums.Take(3))}");
            }
            
            // Build comprehensive focus guidance
            promptBuilder.AppendLine("➤ FOCUS INSTRUCTIONS:");
            
            // Discovery mode guidance
            switch (settings.DiscoveryMode)
            {
                case DiscoveryMode.Similar:
                    promptBuilder.AppendLine($"  • Style: Find artists that sound VERY similar to the above (same genres, similar style)");
                    break;
                case DiscoveryMode.Adjacent:
                    promptBuilder.AppendLine($"  • Style: Find artists in related genres that complement the above collection");
                    break;
                case DiscoveryMode.Exploratory:
                    promptBuilder.AppendLine($"  • Style: Find artists in DIFFERENT genres to expand beyond current taste");
                    break;
            }
            
            // Add user preferences if specified  
            if (!string.IsNullOrWhiteSpace(settings.TargetGenres))
            {
                promptBuilder.AppendLine($"  • Genres: Focus on {settings.TargetGenres} (HIGH PRIORITY)");
            }
            
            var selectedMoods = GetSelectedMoods(settings.MusicMood);
            if (selectedMoods.Any())
            {
                promptBuilder.AppendLine($"  • Moods: Prioritize {string.Join(" OR ", selectedMoods)} feelings (HIGH PRIORITY)");
            }
            
            var selectedEras = GetSelectedEras(settings.MusicEra);
            if (selectedEras.Any())
            {
                promptBuilder.AppendLine($"  • Eras: Focus on {string.Join(" OR ", selectedEras)} time periods (HIGH PRIORITY)");
            }
            
            promptBuilder.AppendLine($"  • Output: Return {settings.RecommendationType} that match these criteria");
        }

        private List<string> SelectExampleArtists(List<string> artists, LibraryProfile profile, DiscoveryMode mode, int count)
        {
            if (!artists.Any()) return new List<string>();
            
            // Prioritize artists from top genres
            var topGenres = profile.TopGenres.Take(3).Select(g => g.Key.ToLower()).ToHashSet();
            
            return mode switch
            {
                DiscoveryMode.Similar => artists.Take(count).ToList(), // Show most prolific
                DiscoveryMode.Adjacent => artists.OrderBy(a => Guid.NewGuid()).Take(count).ToList(), // Mix it up
                DiscoveryMode.Exploratory => artists.TakeLast(count).ToList(), // Show different examples
                _ => artists.Take(count).ToList()
            };
        }

        private List<string> SelectExampleAlbums(List<string> albums, LibraryProfile profile, DiscoveryMode mode, int count)
        {
            if (!albums.Any()) return new List<string>();
            
            return mode switch
            {
                DiscoveryMode.Similar => albums.Take(count).ToList(), // Recent additions
                DiscoveryMode.Adjacent => albums.OrderBy(a => Guid.NewGuid()).Take(count).ToList(), // Random mix
                DiscoveryMode.Exploratory => albums.Skip(albums.Count / 3).Take(count).ToList(), // Different era
                _ => albums.Take(count).ToList()
            };
        }

        private int GetTokenLimitForStrategy(SamplingStrategy strategy, AIProvider provider)
        {
            // Adjust token limits based on sampling strategy and provider capabilities
            return (strategy, provider) switch
            {
                // Local providers - respect user's sampling choice but cap at reasonable limits
                (SamplingStrategy.Minimal, AIProvider.Ollama) => MINIMAL_TOKEN_LIMIT,
                (SamplingStrategy.Minimal, AIProvider.LMStudio) => MINIMAL_TOKEN_LIMIT,
                (SamplingStrategy.Balanced, AIProvider.Ollama) => Math.Min(BALANCED_TOKEN_LIMIT, 2500),
                (SamplingStrategy.Balanced, AIProvider.LMStudio) => Math.Min(BALANCED_TOKEN_LIMIT, 2500),
                (SamplingStrategy.Comprehensive, AIProvider.Ollama) => Math.Min(COMPREHENSIVE_TOKEN_LIMIT, 3000),
                (SamplingStrategy.Comprehensive, AIProvider.LMStudio) => Math.Min(COMPREHENSIVE_TOKEN_LIMIT, 3000),
                
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