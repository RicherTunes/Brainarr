using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Music;
using NzbDrone.Common.Http;
using FluentValidation.Results;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    /// <summary>
    /// Main Lidarr import list implementation for AI-powered music discovery.
    /// Integrates multiple AI providers to generate intelligent music recommendations
    /// based on the user's existing library.
    /// </summary>
    /// <remarks>
    /// Key features:
    /// - Multi-provider support with automatic failover
    /// - Intelligent caching to reduce API calls
    /// - Library-aware prompts for personalized recommendations
    /// - Health monitoring and rate limiting
    /// - Iterative recommendation strategy for quality results
    /// 
    /// The plugin follows Lidarr's import list pattern, fetching recommendations
    /// periodically and converting them to ImportListItemInfo objects that
    /// Lidarr can process for automatic album additions.
    /// </remarks>
    public class Brainarr : ImportListBase<BrainarrSettings>
    {
        private readonly IHttpClient _httpClient;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly ModelDetectionService _modelDetection;
        private readonly IRecommendationCache _cache;
        private readonly IProviderHealthMonitor _healthMonitor;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IRateLimiter _rateLimiter;
        private readonly IProviderFactory _providerFactory;
        private readonly LibraryAwarePromptBuilder _promptBuilder;
        private readonly IterativeRecommendationStrategy _iterativeStrategy;
        private IAIProvider _provider;

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
            Logger logger) : base(importListStatusService, configService, parsingService, logger)
        {
            _httpClient = httpClient;
            _artistService = artistService;
            _albumService = albumService;
            
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
        }

        /// <summary>
        /// Fetches AI-generated music recommendations based on the user's library.
        /// </summary>
        /// <returns>List of recommended albums as ImportListItemInfo objects</returns>
        /// <remarks>
        /// Execution flow:
        /// 1. Initialize/validate AI provider configuration
        /// 2. Check cache for recent recommendations
        /// 3. Build library profile from existing music
        /// 4. Generate context-aware prompt
        /// 5. Get recommendations using iterative strategy
        /// 6. Validate and sanitize results
        /// 7. Cache successful recommendations
        /// 8. Convert to Lidarr import format
        /// </remarks>
        public override IList<ImportListItemInfo> Fetch()
        {
            try
            {
                // Use Task.Run to avoid deadlock in synchronous context
                return Task.Run(async () => await FetchAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching AI recommendations");
                
                // Record failure in health monitor
                _healthMonitor?.RecordFailure(Settings?.Provider.ToString() ?? "Unknown", ex.Message);
                
                return new List<ImportListItemInfo>();
            }
        }
        
        private async Task<IList<ImportListItemInfo>> FetchAsync()
        {
            // Initialize provider with auto-detection for local models
            await InitializeProviderAsync();
            
            if (_provider == null)
            {
                _logger.Error("No AI provider configured");
                return new List<ImportListItemInfo>();
            }

            // Build library profile for personalization and caching
            var libraryProfile = await GetRealLibraryProfileAsync();
            var libraryFingerprint = GenerateLibraryFingerprint(libraryProfile);
            
            // Attempt to use cached recommendations to reduce API calls
            // Cache is keyed by provider, settings, and library fingerprint
            var cacheKey = _cache.GenerateCacheKey(
                Settings.Provider.ToString(), 
                Settings.MaxRecommendations, 
                libraryFingerprint);
            
            if (_cache.TryGet(cacheKey, out var cachedRecommendations))
            {
                _logger.Info($"Returning {cachedRecommendations.Count} cached recommendations");
                return cachedRecommendations;
            }

            // Verify provider is healthy before attempting recommendations
            // This prevents wasted API calls to failing services
            var healthStatus = await _healthMonitor.CheckHealthAsync(
                    Settings.Provider.ToString(), 
                    Settings.BaseUrl);
            
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
            });
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
            // Record metrics
            _healthMonitor.RecordSuccess(Settings.Provider.ToString(), responseTime);
            
            if (!recommendations.Any())
            {
                _logger.Warn("No recommendations received from AI provider");
                return new List<ImportListItemInfo>();
            }

            // Convert to import items (already filtered for duplicates)
            var uniqueItems = recommendations
                    .Where(r => !string.IsNullOrWhiteSpace(r.Artist) && !string.IsNullOrWhiteSpace(r.Album))
                    .Select(ConvertToImportItem)
                    .Where(item => item != null)
                    .ToList();

            // Cache the results
            _cache.Set(cacheKey, uniqueItems, TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes));

            _logger.Info($"Fetched {uniqueItems.Count} unique recommendations from {_provider.ProviderName}");
            return uniqueItems;
        }

        private async Task InitializeProviderAsync()
        {
            // Auto-detect models if enabled
            if (Settings.AutoDetectModel)
            {
                await AutoDetectAndSetModelAsync();
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
        
        private async Task AutoDetectAndSetModelAsync()
        {
            try
            {
                _logger.Info($"Auto-detecting models for {Settings.Provider}");
                
                List<string> detectedModels;
                if (Settings.Provider == AIProvider.Ollama)
                {
                    detectedModels = await _modelDetection.GetOllamaModelsAsync(Settings.OllamaUrl);
                }
                else if (Settings.Provider == AIProvider.LMStudio)
                {
                    detectedModels = await _modelDetection.GetLMStudioModelsAsync(Settings.LMStudioUrl);
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

        private async Task<LibraryProfile> GetRealLibraryProfileAsync()
        {
            try
            {
                // Get ACTUAL data from Lidarr database
                // Wrap synchronous calls in Task.Run to avoid blocking
                var artists = await Task.Run(() => _artistService.GetAllArtists());
                var albums = await Task.Run(() => _albumService.GetAllAlbums());

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
                _logger.Debug($"Configured AI provider: {Settings.Provider}, Base URL: {Settings.BaseUrl ?? "default"}");
                
                // Check if we should recommend artists vs albums based on import list configuration
                var shouldRecommendArtists = ShouldRecommendArtists();
                _logger.Debug($"Recommendation mode: {(shouldRecommendArtists ? "Artists (All Albums)" : "Specific Albums")}");
                
                // Use iterative strategy to get high-quality recommendations
                var recommendations = await _iterativeStrategy.GetIterativeRecommendationsAsync(
                    _provider, profile, allArtists, allAlbums, Settings, shouldRecommendArtists);
                
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Library-aware recommendation failed, falling back to simple prompt");
                return await GetSimpleRecommendationsAsync(profile);
            }
        }
        
        /// <summary>
        /// Determines whether to recommend artists (for comprehensive album import) or specific albums
        /// based on the import list monitoring configuration.
        /// </summary>
        private bool ShouldRecommendArtists()
        {
            // Use the explicit user configuration instead of heuristics
            var shouldRecommendArtists = Settings.RecommendationMode == RecommendationMode.Artists;
            
            if (_logger.IsDebugEnabled)
            {
                var mode = shouldRecommendArtists ? "Artists (All Albums)" : "Specific Albums";
                _logger.Debug($"Recommendation mode: {mode} (User setting: {Settings.RecommendationMode})");
            }
            
            return shouldRecommendArtists;
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

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            _logger.Info($"RequestAction called with action: {action}");
            
            // Handle provider change to clear model cache
            if (action == "providerChanged")
            {
                _logger.Info("Provider changed, clearing model cache");
                // Clear any cached model detection results
                Settings.DetectedModels?.Clear();
                // Return success indicator
                return new { success = true, message = "Provider changed, model cache cleared" };
            }
            
            if (action == "getModelOptions")
            {
                _logger.Info($"RequestAction: getModelOptions called for provider: {Settings.Provider}");
                
                // Clear any stale detected models from previous provider
                if (Settings.DetectedModels != null && Settings.DetectedModels.Any())
                {
                    _logger.Info("Clearing stale detected models from previous provider");
                    Settings.DetectedModels.Clear();
                }
                
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

            // Legacy support for old method names (but only if current provider matches)
            if (action == "getOllamaOptions" && Settings.Provider == AIProvider.Ollama)
            {
                return GetOllamaModelOptions();
            }

            if (action == "getLMStudioOptions" && Settings.Provider == AIProvider.LMStudio)
            {
                return GetLMStudioModelOptions();
            }

            _logger.Info($"RequestAction: Unknown action '{action}' or provider mismatch, returning empty object");
            return new { };
        }

        private object GetOllamaModelOptions()
        {
            _logger.Info("Getting Ollama model options");
            
            if (string.IsNullOrWhiteSpace(Settings.OllamaUrl))
            {
                _logger.Info("OllamaUrl is empty, returning fallback options");
                return GetOllamaFallbackOptions();
            }

            try
            {
                var models = _modelDetection.GetOllamaModelsAsync(Settings.OllamaUrl).GetAwaiter().GetResult();

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} Ollama models");
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
            _logger.Info("Getting LM Studio model options");
            
            if (string.IsNullOrWhiteSpace(Settings.LMStudioUrl))
            {
                _logger.Info("LMStudioUrl is empty, returning fallback options");
                return GetLMStudioFallbackOptions();
            }

            try
            {
                var models = _modelDetection.GetLMStudioModelsAsync(Settings.LMStudioUrl).GetAwaiter().GetResult();

                if (models.Any())
                {
                    _logger.Info($"Found {models.Count} LM Studio models");
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
                var connected = _provider.TestConnectionAsync().GetAwaiter().GetResult();
                if (!connected)
                {
                    failures.Add(new ValidationFailure(string.Empty, 
                        $"Cannot connect to {_provider.ProviderName}"));
                    return;
                }

                // Try to detect available models and update settings (always run, not just when AutoDetectModel is enabled)
                if (Settings.Provider == AIProvider.Ollama)
                {
                    var models = _modelDetection.GetOllamaModelsAsync(Settings.OllamaUrl).GetAwaiter().GetResult();
                    
                    if (models.Any())
                    {
                        _logger.Info($"✅ Found {models.Count} Ollama models: {string.Join(", ", models)}");
                        Settings.DetectedModels = models;
                        
                        // Show available models in logs for copy-paste
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
                else if (Settings.Provider == AIProvider.LMStudio)
                {
                    var models = _modelDetection.GetLMStudioModelsAsync(Settings.LMStudioUrl).GetAwaiter().GetResult();
                    
                    if (models.Any())
                    {
                        _logger.Info($"✅ Found {models.Count} LM Studio models: {string.Join(", ", models)}");
                        Settings.DetectedModels = models;
                        
                        // Show available models in a success message for copy-paste
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
                else
                {
                    // Connection successful for other providers - no validation failure needed
                    _logger.Info($"✅ Connected successfully to {Settings.Provider}");
                }

                _logger.Info($"Test successful: Connected to {_provider.ProviderName}");
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Test failed: {ex.Message}"));
            }
        }
    }

}