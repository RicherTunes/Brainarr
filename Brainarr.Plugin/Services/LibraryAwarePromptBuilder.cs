using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;
using Newtonsoft.Json;
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
        private const double TOKENS_PER_WORD = 1.6;
        private const double TOKENS_PER_CHAR = 0.32;
        // Base prompt structure overhead (instructions, formatting)
        private const int BASE_PROMPT_TOKENS = 300;

        private const int MAX_STRUCTURED_ARTIST_SAMPLE = 240;
        private const int MAX_STRUCTURED_ALBUM_SAMPLE = 90;
        private const int MAX_STRUCTURED_EXCLUDE_ARTISTS = 120;
        private const int MAX_STRUCTURED_RECENT_ARTISTS = 30;

        private static readonly JsonSerializerSettings PromptJsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        // Token limits by sampling strategy and provider type
        // Local models have smaller context windows
        private const int MINIMAL_TOKEN_LIMIT = 3000;
        // Standard cloud providers balance cost and context
        private const int BALANCED_TOKEN_LIMIT = 6000;
        // Premium providers can handle much larger contexts
        private const int COMPREHENSIVE_TOKEN_LIMIT = 20000;

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
            var result = BuildLibraryAwarePromptWithMetrics(profile, allArtists, allAlbums, settings, shouldRecommendArtists);
            return result.Prompt;
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
        private LibrarySample BuildSmartLibrarySample(List<Artist> allArtists, List<Album> allAlbums, int tokenBudget, DiscoveryMode mode)
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
                sample.Artists = SampleArtistsStrategically(allArtists, allAlbums, 30, mode);
                sample.Albums = SampleAlbumsStrategically(allAlbums, 80, mode);
            }
            else
            {
                // Large library: Smart sampling with token estimation
                sample = BuildTokenConstrainedSample(allArtists, allAlbums, tokenBudget, mode);
            }

            sample.Artists = DeduplicateAndTrim(sample.Artists, MAX_STRUCTURED_ARTIST_SAMPLE);
            sample.Albums = DeduplicateAndTrim(sample.Albums, MAX_STRUCTURED_ALBUM_SAMPLE);
            return sample;
        }

        public LibraryPromptResult BuildLibraryAwarePromptWithMetrics(
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings,
            bool shouldRecommendArtists = false,
            IEnumerable<string>? excludedArtists = null,
            int? overrideMaxRecommendations = null)
        {
            var result = new LibraryPromptResult();
            var targetCount = Math.Max(1, overrideMaxRecommendations ?? settings.MaxRecommendations);
            var sessionExclusions = excludedArtists != null
                ? DeduplicateAndTrim(excludedArtists, shouldRecommendArtists ? MAX_STRUCTURED_EXCLUDE_ARTISTS : MAX_STRUCTURED_EXCLUDE_ARTISTS)
                : new List<string>();
            try
            {
                var maxTokens = GetTokenLimitForStrategy(settings.SamplingStrategy, settings.Provider);
                var availableTokens = maxTokens - BASE_PROMPT_TOKENS;
                if (settings.Provider == AIProvider.LMStudio || settings.Provider == AIProvider.Ollama)
                {
                    availableTokens = (int)(availableTokens * 0.8);
                }

                var librarySample = BuildSmartLibrarySample(allArtists, allAlbums, availableTokens, settings.DiscoveryMode);

                if (sessionExclusions.Count > 0)
                {
                    var excludeSet = new HashSet<string>(sessionExclusions, StringComparer.OrdinalIgnoreCase);
                    if (shouldRecommendArtists)
                    {
                        librarySample.Artists = librarySample.Artists
                            .Where(a => !excludeSet.Contains(a))
                            .ToList();
                    }
                    else
                    {
                        librarySample.Albums = librarySample.Albums
                            .Where(a => !excludeSet.Contains(a))
                            .ToList();
                    }
                }

                var useStructured = ShouldUseStructuredPayload(settings);
                string prompt = useStructured
                    ? BuildStructuredPrompt(profile, librarySample, settings, shouldRecommendArtists, sessionExclusions, targetCount)
                    : BuildPromptWithLibraryContext(profile, librarySample, settings, shouldRecommendArtists, targetCount, sessionExclusions);

                var estimatedTokens = EstimateTokens(prompt);

                if (useStructured && estimatedTokens > maxTokens)
                {
                    librarySample.Artists = DeduplicateAndTrim(librarySample.Artists, Math.Max(50, (int)(librarySample.Artists.Count * 0.75)));
                    librarySample.Albums = DeduplicateAndTrim(librarySample.Albums, Math.Max(20, (int)(librarySample.Albums.Count * 0.75)));
                    prompt = BuildStructuredPrompt(profile, librarySample, settings, shouldRecommendArtists, sessionExclusions, targetCount);
                    estimatedTokens = EstimateTokens(prompt);
                }

                result.Prompt = prompt;
                result.SampledArtists = librarySample.Artists.Count;
                result.SampledAlbums = librarySample.Albums.Count;
                result.EstimatedTokens = estimatedTokens;
                result.UsedStructuredPayload = useStructured;
                result.TargetRecommendationCount = targetCount;

                _logger.Debug($"Built library-aware prompt with {result.SampledArtists} artists, {result.SampledAlbums} albums (estimated {estimatedTokens} tokens, structured={useStructured})");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to build library-aware prompt with metrics, falling back");
                var prompt = BuildFallbackPrompt(profile, settings, targetCount);
                result.Prompt = prompt;
                result.SampledArtists = 0;
                result.SampledAlbums = 0;
                result.EstimatedTokens = EstimateTokens(prompt);
                result.UsedStructuredPayload = false;
                result.TargetRecommendationCount = targetCount;
            }

            return result;
        }

        public int GetEffectiveTokenLimit(SamplingStrategy strategy, AIProvider provider)
        {
            return GetTokenLimitForStrategy(strategy, provider);
        }

        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var separators = new[] { ' ', '\n', '\r', '\t' };
            var wordCount = text.Split(separators, StringSplitOptions.RemoveEmptyEntries).Length;
            var byWord = wordCount * TOKENS_PER_WORD;
            var byChar = text.Length * TOKENS_PER_CHAR;
            return (int)Math.Ceiling(Math.Max(byWord, byChar));
        }

        private List<string> SampleArtistsStrategically(List<Artist> allArtists, List<Album> allAlbums, int targetCount, DiscoveryMode mode)
        {
            // Get album counts per artist for prioritization
            var artistAlbumCounts = allAlbums
                .GroupBy(a => a.ArtistId)
                .ToDictionary(g => g.Key, g => g.Count());

            var sampledArtists = new List<string>();

            // Weight splits based on discovery mode
            var topPct = mode == DiscoveryMode.Similar ? 60 : mode == DiscoveryMode.Adjacent ? 40 : 30;
            var recentPct = mode == DiscoveryMode.Similar ? 30 : mode == DiscoveryMode.Adjacent ? 30 : 30;
            var randomPct = Math.Max(0, 100 - topPct - recentPct);

            // 1. Include top artists by album count
            var topByAlbums = allArtists
                .Where(a => artistAlbumCounts.ContainsKey(a.Id))
                .OrderByDescending(a => artistAlbumCounts[a.Id])
                .Take(Math.Max(1, targetCount * topPct / 100))
                .Select(a => a.Name);
            sampledArtists.AddRange(topByAlbums);

            // 2. Include recently added artists
            var recentlyAdded = allArtists
                .OrderByDescending(a => a.Added)
                .Take(Math.Max(1, targetCount * recentPct / 100))
                .Select(a => a.Name)
                .Where(name => !sampledArtists.Contains(name));
            sampledArtists.AddRange(recentlyAdded);

            // 3. Random sampling from remaining artists
            var remaining = allArtists
                .Select(a => a.Name)
                .Where(name => !sampledArtists.Contains(name))
                .OrderBy(x => Guid.NewGuid())
                .Take(Math.Max(0, targetCount - sampledArtists.Count));
            sampledArtists.AddRange(remaining);

            return sampledArtists.Where(name => !string.IsNullOrEmpty(name)).ToList();
        }

        private List<string> SampleAlbumsStrategically(List<Album> allAlbums, int targetCount, DiscoveryMode mode)
        {
            var sampledAlbums = new List<string>();

            // Weight splits based on discovery mode
            var topPct = mode == DiscoveryMode.Similar ? 60 : mode == DiscoveryMode.Adjacent ? 40 : 20;
            var recentPct = mode == DiscoveryMode.Similar ? 30 : mode == DiscoveryMode.Adjacent ? 40 : 30;
            var randomPct = Math.Max(0, 100 - topPct - recentPct);

            // 1. Top-rated / most notable albums
            var topRated = allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .OrderByDescending(a => a.Ratings?.Value ?? 0)
                .ThenByDescending(a => a.Ratings?.Votes ?? 0)
                .Take(Math.Max(1, targetCount * topPct / 100))
                .Select(a => $"{a.ArtistMetadata.Value.Name} - {a.Title}");
            sampledAlbums.AddRange(topRated);

            // 2. Recently added albums
            var recentAlbums = allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .OrderByDescending(a => a.Added)
                .Select(a => $"{a.ArtistMetadata.Value.Name} - {a.Title}")
                .Where(name => !sampledAlbums.Contains(name))
                .Take(Math.Max(1, targetCount * recentPct / 100));
            sampledAlbums.AddRange(recentAlbums);

            // 3. Random sampling from remaining
            var remaining = allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .Select(a => $"{a.ArtistMetadata.Value.Name} - {a.Title}")
                .Where(name => !sampledAlbums.Contains(name))
                .OrderBy(x => Guid.NewGuid())
                .Take(Math.Max(0, targetCount - sampledAlbums.Count));
            sampledAlbums.AddRange(remaining);

            return sampledAlbums.ToList();
        }

        private LibrarySample BuildTokenConstrainedSample(List<Artist> allArtists, List<Album> allAlbums, int tokenBudget, DiscoveryMode mode)
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

            // Allocate token budget between artists and albums based on discovery mode
            // Similar: emphasize artists (avoid changing direction), Exploratory: emphasize albums (show variety)
            var artistShare = mode switch
            {
                DiscoveryMode.Similar => 0.7,       // 70% artists / 30% albums
                DiscoveryMode.Exploratory => 0.4,   // 40% artists / 60% albums
                _ => 0.6                             // Adjacent/Balanced default 60% / 40%
            };

            foreach (var artist in prioritizedArtists)
            {
                var artistTokens = EstimateTokens(artist);
                if (usedTokens + artistTokens > tokenBudget * artistShare) break; // Reserve share for albums

                sample.Artists.Add(artist);
                usedTokens += artistTokens;
            }

            var albumCandidates = new List<string>();

            var topRatedAlbums = allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .OrderByDescending(a => a.Ratings?.Value ?? 0)
                .ThenByDescending(a => a.Ratings?.Votes ?? 0)
                .Select(a => $"{a.ArtistMetadata.Value.Name} - {a.Title}");

            var recentAlbumNames = allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .OrderByDescending(a => a.Added)
                .Select(a => $"{a.ArtistMetadata.Value.Name} - {a.Title}");

            // Interleave top-rated and recent to maximize coverage
            using (var topEnum = topRatedAlbums.GetEnumerator())
            using (var recentEnum = recentAlbumNames.GetEnumerator())
            {
                bool hasTop = true, hasRecent = true;
                while ((hasTop = topEnum.MoveNext()) | (hasRecent = recentEnum.MoveNext()))
                {
                    if (hasTop && !albumCandidates.Contains(topEnum.Current)) albumCandidates.Add(topEnum.Current);
                    if (hasRecent && !albumCandidates.Contains(recentEnum.Current)) albumCandidates.Add(recentEnum.Current);
                    if (albumCandidates.Count > 5000) break; // safety guard
                }
            }

            foreach (var album in albumCandidates)
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


        private static bool ShouldUseStructuredPayload(BrainarrSettings settings)
        {
            return settings.Provider == AIProvider.Gemini;
        }

        private string BuildStructuredPrompt(
            LibraryProfile profile,
            LibrarySample sample,
            BrainarrSettings settings,
            bool shouldRecommendArtists,
            IEnumerable<string>? excludedArtists,
            int targetCount)
        {
            var knownArtists = DeduplicateAndTrim(sample.Artists, MAX_STRUCTURED_ARTIST_SAMPLE);
            var knownAlbums = shouldRecommendArtists ? new List<string>() : DeduplicateAndTrim(sample.Albums, MAX_STRUCTURED_ALBUM_SAMPLE);
            var excludedList = excludedArtists != null ? DeduplicateAndTrim(excludedArtists, MAX_STRUCTURED_EXCLUDE_ARTISTS) : new List<string>();
            var recent = profile.RecentlyAdded?.Any() == true ? DeduplicateAndTrim(profile.RecentlyAdded, MAX_STRUCTURED_RECENT_ARTISTS) : new List<string>();

            var payload = new Dictionary<string, object?>
            {
                ["cfg"] = new Dictionary<string, object?>
                {
                    ["mode"] = shouldRecommendArtists ? "artists" : "albums",
                    ["target"] = targetCount,
                    ["sampling"] = settings.SamplingStrategy.ToString().ToLowerInvariant(),
                    ["discovery"] = settings.DiscoveryMode.ToString().ToLowerInvariant()
                },
                ["profile"] = BuildCompactProfile(profile),
                ["lib"] = new Dictionary<string, object?>
                {
                    ["artists"] = knownArtists
                },
                ["rules"] = BuildRuleSet(shouldRecommendArtists, profile),
                ["output"] = BuildOutputDescriptor(shouldRecommendArtists)
            };

            if (!shouldRecommendArtists && knownAlbums.Count > 0)
            {
                ((Dictionary<string, object?>)payload["lib"])["albums"] = knownAlbums;
            }

            if (recent.Count > 0)
            {
                ((Dictionary<string, object?>)payload["lib"])["recent"] = recent;
            }

            if (excludedList.Count > 0)
            {
                payload["exclude"] = shouldRecommendArtists
                    ? new Dictionary<string, object?> { ["artists"] = excludedList }
                    : new Dictionary<string, object?> { ["albums"] = excludedList };
            }

            return JsonConvert.SerializeObject(payload, PromptJsonSettings);
        }

        private Dictionary<string, object?> BuildCompactProfile(LibraryProfile profile)
        {
            var payload = new Dictionary<string, object?>
            {
                ["size"] = new { a = profile.TotalArtists, al = profile.TotalAlbums },
                ["style"] = GetCollectionCharacter(profile)
            };

            var completion = SafeGetDouble(profile.Metadata, "CollectionCompleteness");
            if (completion > 0)
            {
                payload["completion"] = Math.Round(completion, 3);
            }

            var avgDepth = SafeGetDouble(profile.Metadata, "AverageAlbumsPerArtist");
            if (avgDepth > 0)
            {
                payload["avgAlbumsPerArtist"] = Math.Round(avgDepth, 1);
            }

            var newRelease = SafeGetDouble(profile.Metadata, "NewReleaseRatio");
            if (newRelease > 0)
            {
                payload["newReleaseRatio"] = Math.Round(newRelease, 2);
            }

            if (profile.TopGenres?.Any() == true)
            {
                payload["genres"] = profile.TopGenres
                    .OrderByDescending(kv => kv.Value)
                    .Take(6)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            var eras = ExtractStringList(profile.Metadata, "PreferredEras", 4);
            if (eras.Count > 0)
            {
                payload["eras"] = eras;
            }

            var trend = GetDiscoveryTrend(profile);
            if (!string.IsNullOrWhiteSpace(trend))
            {
                payload["discoveryTrend"] = trend;
            }

            return payload;
        }

        private static List<string> BuildRuleSet(bool shouldRecommendArtists, LibraryProfile profile)
        {
            var rules = new List<string>
            {
                "match_existing_styles",
                "avoid_library_duplicates",
                "json_only_response"
            };

            if (shouldRecommendArtists)
            {
                rules.Insert(1, "prefer_studio_album_artists");
            }

            var newRelease = SafeGetDouble(profile.Metadata, "NewReleaseRatio");
            if (newRelease > 0)
            {
                rules.Add("new_release_bias:" + newRelease.ToString("0.00", CultureInfo.InvariantCulture));
            }

            return rules;
        }

        private static Dictionary<string, object?> BuildOutputDescriptor(bool shouldRecommendArtists)
        {
            var fields = shouldRecommendArtists
                ? new[] { "artist", "genre", "confidence", "reason" }
                : new[] { "artist", "album", "genre", "year", "confidence", "reason" };

            return new Dictionary<string, object?>
            {
                ["fields"] = fields,
                ["confidenceRange"] = new[] { 0.0, 1.0 },
                ["exactCount"] = true
            };
        }

        private static List<string> DeduplicateAndTrim(IEnumerable<string>? source, int max)
        {
            var result = new List<string>();
            if (source == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source)
            {
                var value = item?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (seen.Add(value))
                {
                    result.Add(value);
                    if (result.Count >= max) break;
                }
            }
            return result;
        }

        private static double SafeGetDouble(IDictionary<string, object> metadata, string key)
        {
            if (metadata == null || !metadata.TryGetValue(key, out var value) || value == null)
            {
                return 0;
            }

            return value switch
            {
                double d => d,
                float f => f,
                decimal m => (double)m,
                int i => i,
                long l => l,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0
            };
        }

        private static List<string> ExtractStringList(IDictionary<string, object> metadata, string key, int max)
        {
            var result = new List<string>();
            if (metadata == null || !metadata.TryGetValue(key, out var value) || value == null)
            {
                return result;
            }

            switch (value)
            {
                case IEnumerable<string> strings:
                    result.AddRange(strings.Where(s => !string.IsNullOrWhiteSpace(s)));
                    break;
                case IEnumerable<object> objects:
                    foreach (var obj in objects)
                    {
                        if (obj is string s && !string.IsNullOrWhiteSpace(s))
                        {
                            result.Add(s);
                        }
                    }
                    break;
                case string csv:
                    result.AddRange(csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                    break;
            }

            return DeduplicateAndTrim(result, max);
        }

        private string BuildPromptWithLibraryContext(LibraryProfile profile, LibrarySample sample, BrainarrSettings settings, bool shouldRecommendArtists, int targetCount, IEnumerable<string>? excludedArtists)
        {
            var promptBuilder = new StringBuilder();

            // Add sampling strategy context preamble
            var strategyPreamble = GetSamplingStrategyPreamble(settings.SamplingStrategy);
            if (!string.IsNullOrEmpty(strategyPreamble))
            {
                promptBuilder.AppendLine(strategyPreamble);
                promptBuilder.AppendLine();
                // Clarify sample nature to reduce duplicates on large libraries
                promptBuilder.AppendLine("Note: The above lists are a SAMPLE of a much larger library; treat them as representative of all existing items and avoid duplicates even if not explicitly listed.");
            }

            // Use discovery mode-specific prompt template
            var modeTemplate = GetDiscoveryModeTemplate(settings.DiscoveryMode, targetCount, shouldRecommendArtists);
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
                promptBuilder.AppendLine($"2. Return EXACTLY {targetCount} NEW ARTIST recommendations as JSON");
                promptBuilder.AppendLine($"3. Each must have: artist, genre, confidence (0.0-1.0), reason");
                promptBuilder.AppendLine($"4. Focus on ARTISTS (not specific albums) - Lidarr will import all their albums");
                promptBuilder.AppendLine($"5. Prefer studio album artists over live/compilation specialists");
            }
            else
            {
                promptBuilder.AppendLine($"1. DO NOT recommend any albums from the library shown above");
                promptBuilder.AppendLine($"2. Return EXACTLY {targetCount} specific album recommendations as JSON");
                promptBuilder.AppendLine($"3. Each must have: artist, album, genre, year, confidence (0.0-1.0), reason");
                promptBuilder.AppendLine($"4. Prefer studio albums over live/compilation versions");
            }

            promptBuilder.AppendLine($"6. Match the collection's {GetCollectionCharacter(profile)} character");
            promptBuilder.AppendLine($"7. Align with {GetTemporalPreference(profile)} preferences");
            promptBuilder.AppendLine($"8. Consider {GetDiscoveryTrend(profile)} discovery pattern");
            promptBuilder.AppendLine();

            if (excludedArtists != null)
            {
                var excludedList = DeduplicateAndTrim(excludedArtists, 20);
                if (excludedList.Count > 0)
                {
                    promptBuilder.AppendLine("Previously suggested this session (avoid duplicates):");
                    promptBuilder.AppendLine(string.Join(", ", excludedList));
                    promptBuilder.AppendLine();
                }
            }

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

        private string BuildFallbackPrompt(LibraryProfile profile, BrainarrSettings settings, int? overrideRecommendationCount = null)
        {
            var target = Math.Max(1, overrideRecommendationCount ?? settings.MaxRecommendations);
            // Fallback to original simple prompt if library-aware building fails
            return $@"Based on this music library, recommend {target} new albums to discover:

Library: {profile.TotalArtists} artists, {profile.TotalAlbums} albums
Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", profile.TopArtists.Take(10))}

Return a JSON array with exactly {target} recommendations.
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

        private string GetDiscoveryModeTemplate(DiscoveryMode mode, int maxRecommendations, bool artists)
        {
            var target = artists ? "artists" : "albums";
            return mode switch
            {
                DiscoveryMode.Similar =>
                        $@"You are a music connoisseur tasked with finding {maxRecommendations} {target} that perfectly match this user's established taste.

OBJECTIVE: Recommend {target} from the EXACT SAME subgenres and styles already in the collection.
- Look for artists frequently mentioned alongside their favorites
- Match production styles, era, and sonic characteristics precisely
- Prioritize artists who have collaborated with or influenced their collection
- NO genre-hopping; stay within their comfort zone
Example: If they love 80s synth-pop (Depeche Mode, New Order), recommend more 80s synth-pop, NOT modern synthwave or general electronic.",

                DiscoveryMode.Adjacent =>
                    $@"You are a music discovery expert helping expand this user's horizons into ADJACENT musical territories.

OBJECTIVE: Recommend {maxRecommendations} {target} from related but unexplored genres.
- Find the bridges between their current genres
- Recommend fusion genres that combine their interests
- Look for artists who started in their genres but evolved differently
- Include ""gateway"" albums that ease the transition
Example: If they love prog rock, suggest jazz fusion albums that prog fans typically enjoy (like Return to Forever or Mahavishnu Orchestra).",

                DiscoveryMode.Exploratory =>
                    $@"You are a bold music curator introducing this user to completely NEW musical experiences.

OBJECTIVE: Recommend {maxRecommendations} {target} from genres OUTSIDE their current collection.
- Choose critically acclaimed {target} from unexplored genres
- Find ""entry points"" - accessible {target} in new genres
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
            // Local providers can handle larger prompts on modern setups; scale conservatively
            return (strategy, provider) switch
            {
                // Local providers: provide even more headroom (Qwen/Llama often handle 32–40k tokens)
                (SamplingStrategy.Minimal, AIProvider.Ollama) => (int)(MINIMAL_TOKEN_LIMIT * 1.4),
                (SamplingStrategy.Balanced, AIProvider.Ollama) => (int)(BALANCED_TOKEN_LIMIT * 1.6),
                (SamplingStrategy.Comprehensive, AIProvider.Ollama) => (int)(COMPREHENSIVE_TOKEN_LIMIT * 2.0),

                (SamplingStrategy.Minimal, AIProvider.LMStudio) => (int)(MINIMAL_TOKEN_LIMIT * 1.4),
                (SamplingStrategy.Balanced, AIProvider.LMStudio) => (int)(BALANCED_TOKEN_LIMIT * 1.6),
                (SamplingStrategy.Comprehensive, AIProvider.LMStudio) => (int)(COMPREHENSIVE_TOKEN_LIMIT * 2.0),

                // Premium/balanced cloud providers
                (SamplingStrategy.Minimal, _) => MINIMAL_TOKEN_LIMIT,
                (SamplingStrategy.Balanced, _) => BALANCED_TOKEN_LIMIT,
                (SamplingStrategy.Comprehensive, _) => COMPREHENSIVE_TOKEN_LIMIT,

                _ => BALANCED_TOKEN_LIMIT
            };
        }

        // Note: EstimateTokens exposed publicly via ILibraryAwarePromptBuilder.EstimateTokens

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

            // Recently added artists (steer towards up-to-date tastes)
            if (profile.RecentlyAdded != null && profile.RecentlyAdded.Any())
            {
                var recent = string.Join(", ", profile.RecentlyAdded.Take(10));
                context.AppendLine($"• Recently added artists: {recent}");
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

            // Top collected artists (names with counts) if available
            if (profile.Metadata?.ContainsKey("TopCollectedArtistNames") == true)
            {
                if (profile.Metadata["TopCollectedArtistNames"] is Dictionary<string, int> nameCounts && nameCounts.Any())
                {
                    var line = string.Join(", ", nameCounts
                        .OrderByDescending(kv => kv.Value)
                        .Take(5)
                        .Select(kv => $"{kv.Key} ({kv.Value})"));
                    context.AppendLine($"• Top collected artists: {line}");
                }
            }

            return context.ToString().TrimEnd();
        }

        internal static int ComputeStableHash(string value)
        {
            value ??= string.Empty;

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            var rawSeed = BinaryPrimitives.ReadInt32LittleEndian(hash.AsSpan(0, sizeof(int)));

            return rawSeed & int.MaxValue;
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
