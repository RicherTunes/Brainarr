using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Music;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Common.Http;
using FluentValidation.Results;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public class Brainarr : ImportListBase<BrainarrSettings>
    {
        private readonly IHttpClient _httpClient;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly ISearchForNewArtist _artistSearch;
        private readonly ISearchForNewAlbum _albumSearch;
        private readonly ModelDetectionService _modelDetection;
        private readonly IRecommendationCache _cache;
        private readonly IProviderHealthMonitor _healthMonitor;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IRateLimiter _rateLimiter;
        private readonly IProviderFactory _providerFactory;
        private readonly LibraryAwarePromptBuilder _promptBuilder;
        private readonly IterativeRecommendationStrategy _iterativeStrategy;
        private readonly IMusicBrainzResolver _musicBrainzResolver;
        private IAIProvider _provider;
        
        // Cache for model detection to avoid repeated API calls (thread-safe)
        private static readonly ConcurrentDictionary<string, (List<string> models, DateTime fetchTime)> _modelCache = new();
        private static readonly TimeSpan ModelCacheDuration = TimeSpan.FromMinutes(5);

        public override string Name => "Brainarr AI Music Discovery";
        public override ImportListType ListType => ImportListType.Program;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

        public Brainarr(
            IHttpClient httpClient,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IArtistService artistService,
            IAlbumService albumService,
            ISearchForNewArtist artistSearch,
            ISearchForNewAlbum albumSearch,
            Logger logger) : base(importListStatusService, configService, parsingService, logger)
        {
            _httpClient = httpClient;
            _artistService = artistService;
            _albumService = albumService;
            _artistSearch = artistSearch;
            _albumSearch = albumSearch;
            
            // Initialize services with proper abstraction
            _modelDetection = new ModelDetectionService(httpClient, logger);
            _cache = new RecommendationCache(logger);
            _healthMonitor = new ProviderHealthMonitor(logger);
            _retryPolicy = new ExponentialBackoffRetryPolicy(logger);
            _rateLimiter = new RateLimiter(logger);
            _providerFactory = new AIProviderFactory();
            
            // Configure rate limiters
            RateLimiterConfiguration.ConfigureDefaults(_rateLimiter);
            
            // Initialize new services
            _promptBuilder = new LibraryAwarePromptBuilder(logger);
            _iterativeStrategy = new IterativeRecommendationStrategy(logger, _promptBuilder);
            _musicBrainzResolver = new MusicBrainzResolver(_artistSearch, _albumSearch, _artistService, _albumService, logger);
        }

        public override IList<ImportListItemInfo> Fetch()
        {
            // IMPORTANT: This sync-over-async pattern is necessary because Lidarr's ImportListBase
            // requires a synchronous Fetch() method, but we need to make async HTTP calls.
            // Task.Run prevents deadlocks by running the async work on a thread pool thread.
            // ConfigureAwait(false) ensures we don't capture the synchronization context.
            // This is the recommended pattern for bridging sync and async code when refactoring
            // the base class is not possible.
            return Task.Run(async () => await FetchAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
        }
        
        private async Task<IList<ImportListItemInfo>> FetchAsync()
        {
            try
            {
                // Initialize provider with auto-detection
                await InitializeProviderAsync();
                
                if (_provider == null)
                {
                    _logger.Error("No AI provider configured");
                    return new List<ImportListItemInfo>();
                }

                // Get library profile for cache key
                var libraryProfile = GetRealLibraryProfile();
                var libraryFingerprint = GenerateLibraryFingerprint(libraryProfile);
                
                // Check cache first
                var cacheKey = _cache.GenerateCacheKey(
                    Settings.Provider.ToString(), 
                    Settings.MaxRecommendations, 
                    libraryFingerprint);
                
                if (_cache.TryGet(cacheKey, out var cachedRecommendations))
                {
                    _logger.Info($"Returning {cachedRecommendations.Count} cached recommendations");
                    return cachedRecommendations;
                }

                // Check provider health
                var healthStatus = await _healthMonitor.CheckHealthAsync(
                        Settings.Provider.ToString(), 
                        Settings.BaseUrl).ConfigureAwait(false);
                
                if (healthStatus == HealthStatus.Unhealthy)
                {
                    _logger.Warn($"Provider {Settings.Provider} is unhealthy, returning empty list");
                    return new List<ImportListItemInfo>();
                }
                
                // Get library-aware recommendations using iterative strategy
                var startTime = DateTime.UtcNow;
                var recommendations = await _rateLimiter.ExecuteAsync(Settings.Provider.ToString().ToLower(), async () =>
                {
                    return await _retryPolicy.ExecuteAsync(
                        async () => await GetLibraryAwareRecommendationsAsync(libraryProfile),
                        $"GetRecommendations_{Settings.Provider}");
                }).ConfigureAwait(false);
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                // Record metrics
                _healthMonitor.RecordSuccess(Settings.Provider.ToString(), responseTime);
                
                if (!recommendations.Any())
                {
                    _logger.Warn("No recommendations received from AI provider");
                    return new List<ImportListItemInfo>();
                }

                // Resolve recommendations through MusicBrainz for proper identification
                var resolvedItems = new List<ImportListItemInfo>();
                
                foreach (var rec in recommendations.Where(r => !string.IsNullOrWhiteSpace(r.Artist)))
                {
                    var resolved = await _musicBrainzResolver.ResolveRecommendation(rec).ConfigureAwait(false);
                    
                    if (resolved.Status == ResolutionStatus.Resolved && resolved.Confidence > 0.7)
                    {
                        // High confidence - create import item with MusicBrainz IDs
                        var item = new ImportListItemInfo
                        {
                            ImportListId = Definition.Id,
                            Artist = resolved.DisplayArtist,
                            Album = resolved.DisplayAlbum,
                            ArtistMusicBrainzId = resolved.ArtistMbId,
                            AlbumMusicBrainzId = resolved.AlbumMbId,
                            ReleaseDate = DateTime.UtcNow.AddDays(-30)
                        };
                        resolvedItems.Add(item);
                        _logger.Info($"Resolved: {rec.Artist} -> {resolved.DisplayArtist} (MBID: {resolved.ArtistMbId})");
                    }
                    else if (resolved.Status == ResolutionStatus.Resolved && resolved.Confidence > 0.5)
                    {
                        // Medium confidence - still add but log warning
                        var item = ConvertToImportItem(rec);
                        if (item != null) resolvedItems.Add(item);
                        _logger.Warn($"Low confidence match for {rec.Artist}: {resolved.Confidence:F2}");
                    }
                    else
                    {
                        _logger.Debug($"Skipping unresolved: {rec.Artist} - Status: {resolved.Status}, Reason: {resolved.Reason}");
                    }
                }
                
                var uniqueItems = resolvedItems;

                // Cache the results
                _cache.Set(cacheKey, uniqueItems, TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes));

                _logger.Info($"Fetched {uniqueItems.Count} unique recommendations from {_provider.ProviderName}");
                return uniqueItems;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching AI recommendations");
                
                // Record failure in health monitor
                _healthMonitor.RecordFailure(Settings.Provider.ToString(), ex.Message);
                
                return new List<ImportListItemInfo>();
            }
        }

        private async Task InitializeProviderAsync()
        {
            // Auto-detect models if enabled
            if (Settings.AutoDetectModel)
            {
                await AutoDetectAndSetModelAsync().ConfigureAwait(false);
            }
            
            // Use factory pattern for provider creation
            try
            {
                _provider = _providerFactory.CreateProvider(Settings, _httpClient, _logger);
            }
            catch (NotSupportedException ex)
            {
                _logger.Error(ex, $"Provider type {Settings.Provider} is not supported");
                _provider = null;
            }
            catch (ArgumentException ex)
            {
                _logger.Error(ex, "Invalid provider configuration");
                _provider = null;
            }
        }
        
        private void InitializeProvider()
        {
            // Synchronous wrapper for backward compatibility
            Task.Run(async () => await InitializeProviderAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
        }
        
        private async Task AutoDetectAndSetModelAsync()
        {
            try
            {
                _logger.Info($"Auto-detecting models for {Settings.Provider}");
                
                List<string> detectedModels;
                if (Settings.Provider == AIProvider.Ollama)
                {
                    detectedModels = await _modelDetection.GetOllamaModelsAsync(Settings.OllamaUrl).ConfigureAwait(false);
                }
                else if (Settings.Provider == AIProvider.LMStudio)
                {
                    detectedModels = await _modelDetection.GetLMStudioModelsAsync(Settings.LMStudioUrl).ConfigureAwait(false);
                }
                else
                {
                    detectedModels = new List<string>();
                }
                
                if (detectedModels != null && detectedModels.Any())
                {
                    // Prefer models in this order
                    var preferredModels = new[] { "qwen", "llama", "mistral", "phi", "gemma" };
                    
                    string selectedModel = null;
                    foreach (var preferred in preferredModels)
                    {
                        selectedModel = detectedModels.FirstOrDefault(m => m.ToLower().Contains(preferred));
                        if (selectedModel != null) break;
                    }
                    
                    // Fallback to first model if no preferred model found
                    selectedModel = selectedModel ?? detectedModels.First();
                    
                    if (Settings.Provider == AIProvider.Ollama)
                    {
                        Settings.OllamaModel = selectedModel;
                        _logger.Info($"Auto-detected Ollama model: {selectedModel}");
                    }
                    else
                    {
                        Settings.LMStudioModel = selectedModel;
                        _logger.Info($"Auto-detected LM Studio model: {selectedModel}");
                    }
                }
                else
                {
                    _logger.Warn($"No models detected for {Settings.Provider}, using configured default");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to auto-detect models for {Settings.Provider}, using configured default");
            }
        }

        private LibraryProfile GetRealLibraryProfile()
        {
            try
            {
                // Get ACTUAL data from Lidarr database
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();

                // Build profile from available data
                var artistAlbumCounts = albums
                    .GroupBy(a => a.ArtistId)
                    .Select(g => new { ArtistId = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(20)
                    .ToList();

                var topArtistNames = artistAlbumCounts
                    .Select(ac => artists.FirstOrDefault(a => a.Id == ac.ArtistId)?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                // Create genre list (simplified since Lidarr doesn't expose genres directly)
                var genreCounts = new Dictionary<string, int>();
                for (int i = 0; i < Math.Min(5, BrainarrConstants.FallbackGenres.Length); i++)
                {
                    genreCounts[BrainarrConstants.FallbackGenres[i]] = 20 - (i * 3);
                }

                return new LibraryProfile
                {
                    TotalArtists = artists.Count,
                    TotalAlbums = albums.Count,
                    TopGenres = genreCounts,
                    TopArtists = topArtistNames,
                    RecentlyAdded = artists
                        .OrderByDescending(a => a.Added)
                        .Take(10)
                        .Select(a => a.Name)
                        .ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to get real library data, using sample: {ex.Message}");
                
                // Fallback to sample data if Lidarr services aren't available
                return new LibraryProfile
                {
                    TotalArtists = 100,
                    TotalAlbums = 500,
                    TopGenres = new Dictionary<string, int> 
                    { 
                        { "Rock", 30 }, { "Electronic", 20 }, { "Jazz", 15 }
                    },
                    TopArtists = new List<string> 
                    { 
                        "Radiohead", "Pink Floyd", "Miles Davis" 
                    },
                    RecentlyAdded = new List<string>()
                };
            }
        }

        private async Task<List<Recommendation>> GetLibraryAwareRecommendationsAsync(LibraryProfile profile)
        {
            try
            {
                // Get complete library data for context
                var allArtists = _artistService.GetAllArtists();
                var allAlbums = _albumService.GetAllAlbums();
                
                _logger.Info($"Using library-aware strategy with {allArtists.Count} artists, {allAlbums.Count} albums");
                
                // Use iterative strategy to get high-quality recommendations
                var recommendations = await _iterativeStrategy.GetIterativeRecommendationsAsync(
                    _provider, profile, allArtists, allAlbums, Settings);
                
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Library-aware recommendation failed, falling back to simple prompt");
                return await GetSimpleRecommendationsAsync(profile);
            }
        }
        
        private async Task<List<Recommendation>> GetSimpleRecommendationsAsync(LibraryProfile profile)
        {
            // Fallback to original simple prompt approach
            var prompt = BuildSimplePrompt(profile);
            return await _provider.GetRecommendationsAsync(prompt);
        }
        
        private string BuildSimplePrompt(LibraryProfile profile)
        {
            var prompt = $@"Based on this music library, recommend {Settings.MaxRecommendations} new albums to discover:

Library: {profile.TotalArtists} artists, {profile.TotalAlbums} albums
Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", profile.TopArtists.Take(10))}

Return a JSON array with exactly {Settings.MaxRecommendations} recommendations.
Each item must have: artist, album, genre, confidence (0.0-1.0), reason (brief).

Focus on: {GetDiscoveryFocus()}

Example format:
[
  {{""artist"": ""Artist Name"", ""album"": ""Album Title"", ""genre"": ""Genre"", ""confidence"": 0.8, ""reason"": ""Similar to your jazz collection""}}
]";

            return prompt;
        }
        
        private string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            // Create a more detailed fingerprint that changes when library composition changes significantly
            var topArtistsHash = string.Join(",", profile.TopArtists.Take(10)).GetHashCode();
            var topGenresHash = string.Join(",", profile.TopGenres.Take(5).Select(g => g.Key)).GetHashCode();
            var recentlyAddedHash = string.Join(",", profile.RecentlyAdded.Take(5)).GetHashCode();
            
            return $"{profile.TotalArtists}_{profile.TotalAlbums}_{Math.Abs(topArtistsHash)}_{Math.Abs(topGenresHash)}_{Math.Abs(recentlyAddedHash)}";
        }

        private string GetDiscoveryFocus()
        {
            return Settings.DiscoveryMode switch
            {
                DiscoveryMode.Similar => "artists very similar to the library",
                DiscoveryMode.Adjacent => "artists in related genres",
                DiscoveryMode.Exploratory => "new genres and styles to explore",
                _ => "balanced recommendations"
            };
        }

        private ImportListItemInfo ConvertToImportItem(Recommendation rec)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(rec.Artist) || string.IsNullOrWhiteSpace(rec.Album))
                {
                    _logger.Debug($"Skipping recommendation with empty artist or album: '{rec.Artist}' - '{rec.Album}'");
                    return null;
                }

                // Clean the strings
                var cleanArtist = rec.Artist?.Trim().Replace("\"", "").Replace("'", "'");
                var cleanAlbum = rec.Album?.Trim().Replace("\"", "").Replace("'", "'");

                // Prevent problematic artist names that cause "Various Artists" issues
                if (IsProblematicArtistName(cleanArtist))
                {
                    _logger.Debug($"Skipping recommendation with problematic artist name: '{cleanArtist}' - '{cleanAlbum}'");
                    return null;
                }

                // Log the import item for debugging
                _logger.Debug($"Creating import item: '{cleanArtist}' - '{cleanAlbum}'");

                return new ImportListItemInfo
                {
                    ImportListId = Definition.Id, // Required for Lidarr processing
                    Artist = cleanArtist,
                    Album = cleanAlbum,
                    ArtistMusicBrainzId = null, // Lidarr will match by artist/album name
                    AlbumMusicBrainzId = null,  // Lidarr will match by artist/album name
                    ReleaseDate = DateTime.UtcNow.AddDays(-30) // Set to recent past for better import handling
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to convert recommendation: {ex.Message}");
                return null;
            }
        }

        private bool IsProblematicArtistName(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
                return true;

            var normalized = artistName.ToLowerInvariant().Trim();

            // Block common problematic names that cause Various Artists mapping
            var problematicNames = new[]
            {
                "various artists",
                "various",
                "compilation",
                "soundtrack",
                "ost",
                "original soundtrack",
                "multiple artists",
                "mixed artists",
                "unknown artist",
                "unknown",
                "va",
                "feat.",
                "featuring"
            };

            foreach (var problematic in problematicNames)
            {
                if (normalized.Contains(problematic))
                {
                    return true;
                }
            }

            // Block very short or single-character artist names
            if (normalized.Length <= 2)
            {
                return true;
            }

            // Block names that are just numbers or special characters
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[\d\s\-_\.]+$"))
            {
                return true;
            }

            return false;
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            _logger.Debug($"RequestAction called with action: {action}");
            
            if (action == "getModelOptions")
            {
                _logger.Debug($"RequestAction: getModelOptions called for provider: {Settings.Provider}");
                
                // Only try to connect to the currently selected provider
                return Settings.Provider switch
                {
                    AIProvider.Ollama => GetOllamaModelOptions(),
                    AIProvider.LMStudio => GetLMStudioModelOptions(),
                    AIProvider.Perplexity => GetStaticModelOptions(typeof(PerplexityModel)),
                    AIProvider.OpenAI => GetStaticModelOptions(typeof(OpenAIModel)),
                    AIProvider.Anthropic => GetStaticModelOptions(typeof(AnthropicModel)),
                    AIProvider.OpenRouter => GetStaticModelOptions(typeof(OpenRouterModel)),
                    AIProvider.DeepSeek => GetStaticModelOptions(typeof(DeepSeekModel)),
                    AIProvider.Gemini => GetStaticModelOptions(typeof(GeminiModel)),
                    AIProvider.Groq => GetStaticModelOptions(typeof(GroqModel)),
                    _ => new { options = new List<object>() }
                };
            }

            // Support for model fetching actions from field definitions
            // IMPORTANT: Only fetch if the provider matches to avoid unnecessary API calls
            if (action == "getOllamaModels")
            {
                // Return static options if not Ollama provider to avoid unnecessary probing
                if (Settings.Provider != AIProvider.Ollama)
                {
                    _logger.Debug("Skipping Ollama model fetch - not selected provider");
                    return GetOllamaFallbackOptions();
                }
                return GetOllamaModelOptions();
            }

            if (action == "getLMStudioModels")
            {
                // Return static options if not LM Studio provider to avoid unnecessary probing
                if (Settings.Provider != AIProvider.LMStudio)
                {
                    _logger.Debug("Skipping LM Studio model fetch - not selected provider");
                    return GetLMStudioFallbackOptions();
                }
                return GetLMStudioModelOptions();
            }
            
            // Dynamic mood and era selection (similar to Spotify playlist loading)
            if (action == "getMoodOptions")
            {
                return GetDynamicMoodOptions();
            }
            
            if (action == "getEraOptions")
            {
                return GetDynamicEraOptions();
            }
            
            // Legacy support for old method names (but only if current provider matches)
            if (action == "getOllamaOptions" && Settings.Provider == AIProvider.Ollama)
            {
                return GetOllamaModelOptions();
            }

            if (action == "getLMStudioOptions" && Settings.Provider == AIProvider.LMStudio)
            {
                return GetLMStudioModelOptions();
            }

            _logger.Debug($"RequestAction: Unknown action '{action}' or provider mismatch, returning empty object");
            return new { };
        }

        private object GetOllamaModelOptions()
        {
            _logger.Debug("Getting Ollama model options");
            
            if (string.IsNullOrWhiteSpace(Settings.OllamaUrl))
            {
                _logger.Debug("OllamaUrl is empty, returning fallback options");
                return GetOllamaFallbackOptions();
            }

            // Check cache first
            var cacheKey = $"ollama:{Settings.OllamaUrl}";
            if (_modelCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow - cached.fetchTime < ModelCacheDuration)
                {
                    _logger.Debug($"Returning cached Ollama models (age: {(DateTime.UtcNow - cached.fetchTime).TotalSeconds:F1}s)");
                    var cachedOptions = cached.models.Select(model => new
                    {
                        Value = model,
                        Name = FormatModelName(model)
                    }).ToList();
                    return new { options = cachedOptions };
                }
            }

            try
            {
                _logger.Info($"Fetching Ollama models from {Settings.OllamaUrl}");
                var models = Task.Run(async () => await _modelDetection.GetOllamaModelsAsync(Settings.OllamaUrl).ConfigureAwait(false)).GetAwaiter().GetResult();

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} Ollama models, caching for {ModelCacheDuration.TotalMinutes} minutes");
                    
                    // Update cache
                    _modelCache[cacheKey] = (models, DateTime.UtcNow);
                    
                    var options = models.Select(model => new
                    {
                        Value = model,
                        Name = FormatModelName(model)
                    }).ToList();
                    
                    return new { options = options };
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get Ollama models for dropdown");
            }

            return GetOllamaFallbackOptions();
        }

        private object GetLMStudioModelOptions()
        {
            _logger.Debug("Getting LM Studio model options");
            
            if (string.IsNullOrWhiteSpace(Settings.LMStudioUrl))
            {
                _logger.Debug("LMStudioUrl is empty, returning fallback options");
                return GetLMStudioFallbackOptions();
            }

            // Check cache first
            var cacheKey = $"lmstudio:{Settings.LMStudioUrl}";
            if (_modelCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow - cached.fetchTime < ModelCacheDuration)
                {
                    _logger.Debug($"Returning cached LM Studio models (age: {(DateTime.UtcNow - cached.fetchTime).TotalSeconds:F1}s)");
                    var cachedOptions = cached.models.Select(model => new
                    {
                        Value = model,
                        Name = FormatModelName(model)
                    }).ToList();
                    return new { options = cachedOptions };
                }
            }

            try
            {
                _logger.Info($"Fetching LM Studio models from {Settings.LMStudioUrl}");
                var models = Task.Run(async () => await _modelDetection.GetLMStudioModelsAsync(Settings.LMStudioUrl).ConfigureAwait(false)).GetAwaiter().GetResult();

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} LM Studio models, caching for {ModelCacheDuration.TotalMinutes} minutes");
                    
                    // Update cache
                    _modelCache[cacheKey] = (models, DateTime.UtcNow);
                    
                    var options = models.Select(model => new
                    {
                        Value = model,
                        Name = FormatModelName(model)
                    }).ToList();
                    
                    return new { options = options };
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get LM Studio models for dropdown");
            }

            return GetLMStudioFallbackOptions();
        }

        private object GetStaticModelOptions(System.Type enumType)
        {
            var options = System.Enum.GetValues(enumType)
                .Cast<System.Enum>()
                .Select(value => new
                {
                    Value = value.ToString(),
                    Name = FormatEnumName(value.ToString())
                }).ToList();
            
            return new { options = options };
        }
        
        private object GetStaticIntegerEnumOptions(System.Type enumType)
        {
            var options = System.Enum.GetValues(enumType)
                .Cast<System.Enum>()
                .Select(value => new
                {
                    Value = (int)(object)value,
                    Name = FormatEnumName(value.ToString())
                }).ToList();
            
            return new { options = options };
        }

        private object GetOllamaFallbackOptions()
        {
            return new
            {
                options = new[]
                {
                    new { Value = "qwen2.5:latest", Name = "Qwen 2.5 (Recommended)" },
                    new { Value = "qwen2.5:7b", Name = "Qwen 2.5 7B" },
                    new { Value = "llama3.2:latest", Name = "Llama 3.2" },
                    new { Value = "mistral:latest", Name = "Mistral" }
                }
            };
        }

        private object GetLMStudioFallbackOptions()
        {
            return new
            {
                options = new[]
                {
                    new { Value = "local-model", Name = "Currently Loaded Model" }
                }
            };
        }

        private object GetDynamicMoodOptions()
        {
            try
            {
                // Analyze library to suggest relevant moods
                var allArtists = _artistService.GetAllArtists();
                var libraryProfile = GetRealLibraryProfile();
                
                // Create mood options based on library analysis
                var moodOptions = new List<object>
                {
                    new { Value = 0, Name = "Any (Library-based)" }
                };
                
                // Add genre-based mood suggestions
                var topGenres = libraryProfile.TopGenres.Take(5);
                foreach (var genre in topGenres)
                {
                    var suggestedMoods = GetMoodsForGenre(genre.Key);
                    foreach (var mood in suggestedMoods)
                    {
                        if (!moodOptions.Any(m => ((dynamic)m).Value == mood.Value))
                        {
                            moodOptions.Add(new { Value = mood.Value, Name = mood.Name });
                        }
                    }
                }
                
                // Add all standard moods
                var allMoods = Enum.GetValues(typeof(MusicMoodOptions))
                    .Cast<MusicMoodOptions>()
                    .Where(m => m != MusicMoodOptions.Any)
                    .Select(m => new { Value = (int)m, Name = FormatEnumName(m.ToString()) });
                
                foreach (var mood in allMoods)
                {
                    if (!moodOptions.Any(m => ((dynamic)m).Value == mood.Value))
                    {
                        moodOptions.Add(mood);
                    }
                }
                
                return new
                {
                    options = moodOptions,
                    metadata = new
                    {
                        libraryAnalysis = $"Based on {libraryProfile.TotalArtists} artists",
                        topGenres = string.Join(", ", topGenres.Select(g => g.Key))
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get dynamic mood options");
                // Fallback to static options
                return GetStaticIntegerEnumOptions(typeof(MusicMoodOptions));
            }
        }

        private object GetDynamicEraOptions()
        {
            try
            {
                var allAlbums = _albumService.GetAllAlbums();
                
                // Analyze album release dates to suggest relevant eras
                var releaseYears = allAlbums
                    .Where(a => a.ReleaseDate.HasValue)
                    .Select(a => a.ReleaseDate.Value.Year)
                    .GroupBy(y => GetEraForYear(y))
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .ToList();
                
                var eraOptions = new List<object>
                {
                    new { Value = 0, Name = "Any (Library-based)" }
                };
                
                // Add library-relevant eras first
                foreach (var era in releaseYears.Take(3))
                {
                    if (era != MusicEraOptions.Any)
                    {
                        eraOptions.Add(new { Value = (int)era, Name = FormatEnumName(era.ToString()) + " ⭐" });
                    }
                }
                
                // Add all other eras
                var allEras = Enum.GetValues(typeof(MusicEraOptions))
                    .Cast<MusicEraOptions>()
                    .Where(e => e != MusicEraOptions.Any && !releaseYears.Contains(e))
                    .Select(e => new { Value = (int)e, Name = FormatEnumName(e.ToString()) });
                
                eraOptions.AddRange(allEras);
                
                return new
                {
                    options = eraOptions,
                    metadata = new
                    {
                        librarySpan = $"{allAlbums.Min(a => a.ReleaseDate?.Year ?? 2000)}-{allAlbums.Max(a => a.ReleaseDate?.Year ?? DateTime.Now.Year)}",
                        totalAlbums = allAlbums.Count
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get dynamic era options");
                return GetStaticIntegerEnumOptions(typeof(MusicEraOptions));
            }
        }

        private List<(int Value, string Name)> GetMoodsForGenre(string genre)
        {
            var genreLower = genre.ToLowerInvariant();
            
            if (genreLower.Contains("metal") || genreLower.Contains("punk"))
                return new List<(int, string)> { (7, "Aggressive"), (1, "Energetic") };
            
            if (genreLower.Contains("jazz") || genreLower.Contains("soul"))
                return new List<(int, string)> { (2, "Chill"), (15, "Groovy"), (14, "Intimate") };
            
            if (genreLower.Contains("electronic") || genreLower.Contains("techno"))
                return new List<(int, string)> { (6, "Danceable"), (5, "Experimental"), (1, "Energetic") };
            
            if (genreLower.Contains("classical") || genreLower.Contains("orchestral"))
                return new List<(int, string)> { (13, "Epic"), (8, "Peaceful"), (4, "Emotional") };
            
            if (genreLower.Contains("indie") || genreLower.Contains("alternative"))
                return new List<(int, string)> { (9, "Melancholic"), (11, "Mysterious"), (14, "Intimate") };
            
            return new List<(int, string)> { (1, "Energetic"), (2, "Chill") };
        }

        private MusicEraOptions GetEraForYear(int year)
        {
            return year switch
            {
                < 1950 => MusicEraOptions.PreRock,
                < 1960 => MusicEraOptions.EarlyRock,
                < 1970 => MusicEraOptions.SixtiesPop,
                < 1980 => MusicEraOptions.SeventiesRock,
                < 1990 => MusicEraOptions.EightiesPop,
                < 2000 => MusicEraOptions.NinetiesAlt,
                < 2010 => MusicEraOptions.MillenniumPop,
                < 2020 => MusicEraOptions.TensSocial,
                _ => MusicEraOptions.Modern
            };
        }

        private string FormatEnumName(string enumValue)
        {
            // Convert enum value to readable name (e.g., "GPT4o_Mini" -> "GPT-4o Mini")
            return enumValue
                .Replace("_", " ")
                .Replace("GPT4o", "GPT-4o")
                .Replace("Claude35", "Claude 3.5")
                .Replace("Claude3", "Claude 3")
                .Replace("Llama33", "Llama 3.3")
                .Replace("Llama32", "Llama 3.2")
                .Replace("Llama31", "Llama 3.1")
                .Replace("Gemini15", "Gemini 1.5")
                .Replace("Gemini20", "Gemini 2.0");
        }

        private string FormatModelName(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return "Unknown Model";
            
            // For LM Studio models with path separators, show more context
            if (modelId.Contains("/"))
            {
                var parts = modelId.Split('/');
                if (parts.Length >= 2)
                {
                    // Format: "organization/model-name" -> "Model Name (organization)"
                    var org = parts[0];
                    var modelName = parts[1];
                    
                    // Clean up the model name part
                    var cleanName = CleanModelName(modelName);
                    
                    return $"{cleanName} ({org})";
                }
            }
            
            // For Ollama models with colons (model:tag format)
            if (modelId.Contains(":"))
            {
                var parts = modelId.Split(':');
                if (parts.Length >= 2)
                {
                    var modelName = CleanModelName(parts[0]);
                    var tag = parts[1];
                    
                    // Show both model and tag for clarity
                    return $"{modelName}:{tag}";
                }
            }
            
            // Fallback: clean the name but preserve most information
            return CleanModelName(modelId);
        }

        private string CleanModelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            
            // Replace common separators with spaces and title case key parts
            var cleaned = name
                .Replace("-", " ")
                .Replace("_", " ")
                .Replace(".", " ");
            
            // Capitalize known model families while preserving version info
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bqwen\b", "Qwen", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bllama\b", "Llama", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bmistral\b", "Mistral", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bgemma\b", "Gemma", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bphi\b", "Phi", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\bcoder\b", "Coder", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\binstruct\b", "Instruct", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Clean up multiple spaces
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
            
            return cleaned;
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                InitializeProvider();
                
                if (_provider == null)
                {
                    failures.Add(new ValidationFailure(nameof(Settings.Provider), 
                        "AI provider not configured"));
                    return;
                }

                // Test connection
                var connected = Task.Run(async () => await _provider.TestConnectionAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
                if (!connected)
                {
                    failures.Add(new ValidationFailure(string.Empty, 
                        $"Cannot connect to {_provider.ProviderName}"));
                    return;
                }

                // Detect and display available models for ALL providers during Test
                DetectAndDisplayAvailableModels(failures);

                _logger.Info($"Test successful: Connected to {_provider.ProviderName}");
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Test failed: {ex.Message}"));
            }
        }

        private void DetectAndDisplayAvailableModels(List<ValidationFailure> failures)
        {
            try
            {
                _logger.Info($"🔍 Detecting available models for {Settings.Provider}...");
                
                switch (Settings.Provider)
                {
                    case AIProvider.Ollama:
                        DetectOllamaModels(failures);
                        break;
                        
                    case AIProvider.LMStudio:
                        DetectLMStudioModels(failures);
                        break;
                        
                    case AIProvider.OpenAI:
                    case AIProvider.Anthropic:
                    case AIProvider.Gemini:
                    case AIProvider.Groq:
                    case AIProvider.Perplexity:
                    case AIProvider.DeepSeek:
                    case AIProvider.OpenRouter:
                        DetectCloudProviderModels();
                        break;
                        
                    default:
                        _logger.Info($"✅ Connected successfully to {Settings.Provider}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to detect models for {Settings.Provider}, but connection is working");
            }
        }

        private void DetectOllamaModels(List<ValidationFailure> failures)
        {
            var models = Task.Run(async () => await _modelDetection.GetOllamaModelsAsync(Settings.OllamaUrl).ConfigureAwait(false)).GetAwaiter().GetResult();
            
            if (models.Any())
            {
                _logger.Info($"✅ Found {models.Count} Ollama models: {string.Join(", ", models)}");
                Settings.DetectedModels = models;
                
                var topModels = models.Take(3).ToList();
                var modelList = string.Join(", ", topModels);
                if (models.Count > 3) modelList += $" (and {models.Count - 3} more)";
                
                _logger.Info($"🎯 Recommended: Copy one of these models into the field above: {modelList}");
            }
            else
            {
                failures.Add(new ValidationFailure(string.Empty, 
                    "No suitable models found. Install models like: ollama pull qwen2.5"));
            }
        }

        private void DetectLMStudioModels(List<ValidationFailure> failures)
        {
            var models = Task.Run(async () => await _modelDetection.GetLMStudioModelsAsync(Settings.LMStudioUrl).ConfigureAwait(false)).GetAwaiter().GetResult();
            
            if (models.Any())
            {
                _logger.Info($"✅ Found {models.Count} LM Studio models: {string.Join(", ", models)}");
                Settings.DetectedModels = models;
                
                var topModels = models.Take(3).ToList();
                var modelList = string.Join(", ", topModels);
                if (models.Count > 3) modelList += $" (and {models.Count - 3} more)";
                
                _logger.Info($"🎯 Recommended: Copy one of these models into the field above: {modelList}");
            }
            else
            {
                failures.Add(new ValidationFailure(string.Empty, 
                    "No models loaded. Load a model in LM Studio first."));
            }
        }

        private void DetectCloudProviderModels()
        {
            // For cloud providers, show available models from the provider's GetAvailableModelsAsync
            try
            {
                // Check if provider supports model listing (BaseAIProvider has this method)
                if (_provider is BaseAIProvider baseProvider)
                {
                    var models = Task.Run(async () => await baseProvider.GetAvailableModelsAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
                
                    if (models.Any())
                    {
                        _logger.Info($"✅ {Settings.Provider} is connected. Available models:");
                        foreach (var model in models.Take(10)) // Limit to first 10
                        {
                            _logger.Info($"   • {model}");
                        }
                        if (models.Count > 10)
                        {
                            _logger.Info($"   ... and {models.Count - 10} more models available");
                        }
                        
                        Settings.DetectedModels = models;
                        _logger.Info($"🎯 You can use any of these models in your model field above!");
                    }
                    else
                    {
                        _logger.Info($"✅ Connected successfully to {Settings.Provider}");
                    }
                }
                else
                {
                    _logger.Info($"✅ Connected successfully to {Settings.Provider}");
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Could not fetch model list from {Settings.Provider}, but connection is working");
                _logger.Info($"✅ Connected successfully to {Settings.Provider}");
            }
        }
    }

    public class LibraryProfile
    {
        public int TotalArtists { get; set; }
        public int TotalAlbums { get; set; }
        public Dictionary<string, int> TopGenres { get; set; }
        public List<string> TopArtists { get; set; }
        public List<string> RecentlyAdded { get; set; }
    }
}