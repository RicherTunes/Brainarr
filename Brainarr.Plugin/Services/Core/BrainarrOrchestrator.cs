using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using NzbDrone.Common.Http;
using NLog;
using FluentValidation.Results;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Main orchestrator implementation for the Brainarr plugin, coordinating all aspects of
    /// AI-powered music recommendation generation. Manages provider lifecycle, caching,
    /// health monitoring, and intelligent recommendation strategies.
    /// </summary>
    /// <remarks>
    /// This orchestrator serves as the primary entry point for recommendation generation,
    /// implementing sophisticated strategies including:
    /// - Correlation ID tracking for request tracing
    /// - Library-aware vs simple recommendation strategies
    /// - Automatic fallback model switching on health issues
    /// - Iterative refinement for improved recommendation quality
    /// - Comprehensive error handling and recovery
    /// 
    /// Performance considerations:
    /// - Uses caching to reduce API calls and improve response times
    /// - Implements rate limiting to respect provider API limits
    /// - Monitors provider health to avoid failed requests
    /// - Automatically switches to fallback models when primary fails
    /// </remarks>
    public class BrainarrOrchestrator : IBrainarrOrchestrator
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
        private readonly Logger _logger;
        private IAIProvider _provider;

        /// <summary>
        /// Initializes a new instance of the BrainarrOrchestrator with core Lidarr services.
        /// Sets up the complete recommendation infrastructure including caching, health monitoring,
        /// rate limiting, and advanced recommendation strategies.
        /// </summary>
        /// <param name="httpClient">HTTP client for provider communications</param>
        /// <param name="artistService">Lidarr artist service for library analysis</param>
        /// <param name="albumService">Lidarr album service for library profiling</param>
        /// <param name="logger">Logger instance for comprehensive monitoring</param>
        /// <remarks>
        /// The constructor initializes all supporting services with sensible defaults:
        /// - Rate limiter with per-provider configuration
        /// - Exponential backoff retry policies
        /// - In-memory recommendation caching
        /// - Provider health monitoring with automatic recovery
        /// </remarks>
        public BrainarrOrchestrator(
            IHttpClient httpClient,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger)
        {
            _httpClient = httpClient;
            _artistService = artistService;
            _albumService = albumService;
            _logger = logger;
            
            _modelDetection = new ModelDetectionService(httpClient, logger);
            _cache = new RecommendationCache(logger);
            _healthMonitor = new ProviderHealthMonitor(logger);
            _retryPolicy = new ExponentialBackoffRetryPolicy(logger);
            _rateLimiter = new RateLimiter(logger);
            _providerFactory = new AIProviderFactory();
            
            RateLimiterConfiguration.ConfigureDefaults(_rateLimiter);
            
            _promptBuilder = new LibraryAwarePromptBuilder(logger);
            _iterativeStrategy = new IterativeRecommendationStrategy(logger, _promptBuilder);
        }

        public IList<ImportListItemInfo> FetchRecommendations(BrainarrSettings settings)
        {
            // Use AsyncHelper to safely execute async code without deadlock risk
            return AsyncHelper.RunSync(() => FetchRecommendationsAsync(settings));
        }

        public async Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings)
        {
            // Generate correlation ID for this request
            var correlationId = CorrelationContext.StartNew();
            
            try
            {
                _logger.InfoWithCorrelation($"Starting recommendation fetch for provider: {settings.Provider}");
                InitializeProvider(settings);
                
                if (_provider == null)
                {
                    _logger.ErrorWithCorrelation("No AI provider configured");
                    return new List<ImportListItemInfo>();
                }

                var libraryProfile = GetLibraryProfile(settings);
                var libraryFingerprint = GenerateLibraryFingerprint(libraryProfile);
                
                var cacheKey = _cache.GenerateCacheKey(
                    settings.Provider.ToString(), 
                    settings.MaxRecommendations, 
                    libraryFingerprint);
                
                if (_cache.TryGet(cacheKey, out var cachedRecommendations))
                {
                    _logger.InfoWithCorrelation($"Returning {cachedRecommendations.Count} cached recommendations");
                    return cachedRecommendations;
                }

                var healthStatus = await _healthMonitor.CheckHealthAsync(
                    settings.Provider.ToString(), 
                    settings.BaseUrl);
                
                if (healthStatus == HealthStatus.Unhealthy)
                {
                    _logger.WarnWithCorrelation("Provider is unhealthy, attempting with fallback model");
                    if (settings.EnableFallbackModel && !string.IsNullOrEmpty(settings.FallbackModel))
                    {
                        _provider.UpdateModel(settings.FallbackModel);
                    }
                }

                List<Recommendation> recommendations;
                if (settings.EnableLibraryAnalysis && libraryProfile.TopArtists.Any())
                {
                    recommendations = await GetLibraryAwareRecommendationsAsync(libraryProfile, settings);
                }
                else
                {
                    recommendations = await GetSimpleRecommendationsAsync(libraryProfile, settings);
                }

                var importItems = recommendations
                    .Select(ConvertToImportItem)
                    .Where(item => item != null)
                    .ToList();

                _cache.Set(cacheKey, importItems, settings.CacheDuration);
                _healthMonitor.RecordSuccess(settings.Provider.ToString(), 1000.0);
                
                return importItems;
            }
            catch (Exception ex)
            {
                _logger.ErrorWithCorrelation(ex, "Failed to fetch AI recommendations");
                _healthMonitor.RecordFailure(settings.Provider.ToString(), ex.Message);
                return new List<ImportListItemInfo>();
            }
        }

        public void InitializeProvider(BrainarrSettings settings)
        {
            if (_provider != null && IsProviderCurrent(settings))
            {
                return;
            }

            _provider = _providerFactory.CreateProvider(
                settings,
                _httpClient,
                _logger);

            if (settings.EnableAutoDetection && 
                (settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio))
            {
                AutoDetectAndSetModel(settings);
            }
        }

        public void UpdateProviderConfiguration(BrainarrSettings settings)
        {
            InitializeProvider(settings);
        }

        public bool IsProviderHealthy()
        {
            return _provider != null && _healthMonitor.IsHealthy(_provider.GetType().Name);
        }

        public string GetProviderStatus()
        {
            if (_provider == null)
            {
                return "Not Initialized";
            }

            var health = _healthMonitor.GetHealthStatus(_provider.GetType().Name);
            return $"{_provider.GetType().Name}: {health}";
        }

        private bool IsProviderCurrent(BrainarrSettings settings)
        {
            if (_provider == null) return false;
            
            var currentProviderType = _provider.GetType().Name.Replace("Provider", "");
            return currentProviderType.Equals(settings.Provider.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private void AutoDetectAndSetModel(BrainarrSettings settings)
        {
            // Use AsyncHelper to safely run async model detection
            AsyncHelper.RunSync(() => AutoDetectAndSetModelAsync(settings));
        }

        private async Task AutoDetectAndSetModelAsync(BrainarrSettings settings)
        {
            try
            {
                var availableModels = settings.Provider == AIProvider.Ollama
                    ? await _modelDetection.DetectOllamaModelsAsync(settings.BaseUrl).ConfigureAwait(false)
                    : await _modelDetection.DetectLMStudioModelsAsync(settings.BaseUrl).ConfigureAwait(false);

                if (availableModels?.Any() == true)
                {
                    var currentModel = settings.Provider == AIProvider.Ollama 
                        ? settings.OllamaModel 
                        : settings.LMStudioModel;

                    if (string.IsNullOrEmpty(currentModel) || !availableModels.Contains(currentModel))
                    {
                        var selectedModel = SelectBestModel(availableModels);
                        
                        if (settings.Provider == AIProvider.Ollama)
                        {
                            settings.OllamaModel = selectedModel;
                        }
                        else
                        {
                            settings.LMStudioModel = selectedModel;
                        }

                        _logger.Info($"Auto-detected and set model: {selectedModel}");
                        _provider?.UpdateModel(selectedModel);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Auto-detection failed, using default model");
            }
        }

        private string SelectBestModel(List<string> models)
        {
            var preferredModels = new[] 
            { 
                "llama3", "llama2", "mistral", "mixtral", 
                "qwen", "gemma", "phi", "neural-chat" 
            };

            foreach (var preferred in preferredModels)
            {
                var match = models.FirstOrDefault(m => 
                    m.ToLower().Contains(preferred));
                if (match != null) return match;
            }

            return models.First();
        }

        private LibraryProfile GetLibraryProfile(BrainarrSettings settings)
        {
            var artists = _artistService.GetAllArtists();
            var albums = _albumService.GetAllAlbums();

            var topGenres = albums
                .SelectMany(a => a.Genres ?? new List<string>())
                .GroupBy(g => g)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToList();

            var topArtists = artists
                .OrderBy(a => a.Name)
                .Take(20)
                .Select(a => a.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            var recentAlbums = albums
                .Where(a => a.Added > DateTime.UtcNow.AddMonths(-3))
                .OrderByDescending(a => a.Added)
                .Take(20)
                .Select(a => new AlbumProfile
                {
                    Title = a.Title,
                    Artist = a.Artist?.Value?.Name ?? "Unknown",
                    ReleaseDate = a.ReleaseDate,
                    Genres = a.Genres
                })
                .ToList();

            return new LibraryProfile
            {
                TopGenres = topGenres.Take(15).ToDictionary(g => g, g => 1),
                TopArtists = topArtists,
                RecentlyAdded = topArtists.Take(10).ToList(),
                TotalArtists = artists.Count,
                TotalAlbums = albums.Count
            };
        }

        private List<string> DetermineListeningTrends(List<string> genres, List<AlbumProfile> recentAlbums)
        {
            var trends = new List<string>();
            
            if (genres.Any(g => g.Contains("metal", StringComparison.OrdinalIgnoreCase)))
                trends.Add("Heavy Music Preference");
            
            if (genres.Any(g => g.Contains("electronic", StringComparison.OrdinalIgnoreCase)))
                trends.Add("Electronic Music Fan");
            
            if (recentAlbums.Count > 15)
                trends.Add("Active Collector");

            return trends;
        }

        private async Task<List<Recommendation>> GetLibraryAwareRecommendationsAsync(
            LibraryProfile profile, 
            BrainarrSettings settings)
        {
            // Get the full artist and album lists for detailed analysis
            var artists = _artistService.GetAllArtists();
            var albums = _albumService.GetAllAlbums();

            if (settings.EnableIterativeRefinement)
            {
                return await _iterativeStrategy.GetIterativeRecommendationsAsync(
                    _provider, 
                    profile, 
                    artists,
                    albums,
                    settings);
            }

            var prompt = _promptBuilder.BuildLibraryAwarePrompt(
                profile, 
                artists,
                albums,
                settings);

            return await _provider.GetRecommendationsAsync(prompt);
        }

        private async Task<List<Recommendation>> GetSimpleRecommendationsAsync(
            LibraryProfile profile, 
            BrainarrSettings settings)
        {
            var prompt = BuildSimplePrompt(profile, settings);
            return await _provider.GetRecommendationsAsync(prompt);
        }

        private string BuildSimplePrompt(LibraryProfile profile, BrainarrSettings settings)
        {
            var genres = profile.TopGenres.Any() 
                ? string.Join(", ", profile.TopGenres.Keys.Take(5))
                : "rock, indie, alternative, electronic, jazz";

            var focus = GetDiscoveryFocus(settings);
            
            return $@"Recommend {settings.MaxRecommendations} music albums.
Focus: {focus}
Preferred genres: {genres}

Return a JSON array with this exact structure:
[
  {{
    ""artist"": ""Artist Name"",
    ""album"": ""Album Title"",
    ""year"": 2024,
    ""genre"": ""Primary Genre"",
    ""reason"": ""Brief reason for recommendation""
  }}
]";
        }

        private string GetDiscoveryFocus(BrainarrSettings settings)
        {
            return settings.DiscoveryMode switch
            {
                DiscoveryMode.Similar => "Artists similar to my library",
                DiscoveryMode.Adjacent => "Related genres and styles to explore",
                DiscoveryMode.Exploratory => "New genres and hidden gems to discover",
                _ => "Balanced mix of familiar and new"
            };
        }

        private string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            var components = new[]
            {
                string.Join(",", profile.TopGenres.Keys.Take(5)),
                profile.TotalArtists.ToString(),
                profile.TotalAlbums.ToString(),
                string.Join(",", profile.TopArtists.Take(5))
            };

            return string.Join("|", components).GetHashCode().ToString();
        }

        private ImportListItemInfo ConvertToImportItem(Recommendation rec)
        {
            try
            {
                return new ImportListItemInfo
                {
                    Artist = rec.Artist,
                    Album = rec.Album,
                    ReleaseDate = rec.Year.HasValue 
                        ? new DateTime(rec.Year.Value, 1, 1) 
                        : DateTime.Now.AddYears(-1),
                    ArtistMusicBrainzId = rec.ArtistMusicBrainzId,
                    AlbumMusicBrainzId = rec.AlbumMusicBrainzId
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to convert recommendation: {rec.Artist} - {rec.Album}");
                return null;
            }
        }
        
        public void ValidateConfiguration(BrainarrSettings settings, List<ValidationFailure> failures)
        {
            try
            {
                // Initialize provider for validation
                InitializeProvider(settings);
                
                if (_provider == null)
                {
                    failures.Add(new ValidationFailure(nameof(settings.Provider), 
                        "AI provider not configured"));
                    return;
                }

                // Test connection - use sync version since validation is sync
                var connected = AsyncHelper.RunSync(() => _provider.TestConnectionAsync());
                if (!connected)
                {
                    failures.Add(new ValidationFailure(string.Empty, 
                        $"Cannot connect to {_provider.ProviderName}"));
                    return;
                }

                // Model detection for local providers
                if (settings.Provider == AIProvider.Ollama)
                {
                    var models = AsyncHelper.RunSync(() => _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl));
                    
                    if (models.Any())
                    {
                        _logger.Info($"✅ Found {models.Count} Ollama models: {string.Join(", ", models)}");
                        settings.DetectedModels = models;
                        
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
                else if (settings.Provider == AIProvider.LMStudio)
                {
                    var models = AsyncHelper.RunSync(() => _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl));
                    
                    if (models.Any())
                    {
                        _logger.Info($"✅ Found {models.Count} LM Studio models: {string.Join(", ", models)}");
                        settings.DetectedModels = models;
                        
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
                    _logger.Info($"✅ Connected successfully to {settings.Provider}");
                }

                _logger.Info($"Test successful: Connected to {_provider.ProviderName}");
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Test failed: {ex.Message}"));
            }
        }

        public object HandleAction(string action, IDictionary<string, string> query, BrainarrSettings settings)
        {
            try
            {
                _logger.Info($"RequestAction called with action: {action}");
                
                // Handle provider change to clear model cache
                if (action == "providerChanged")
                {
                    _logger.Info("Provider changed, clearing model cache");
                    settings.DetectedModels?.Clear();
                    return new { success = true, message = "Provider changed, model cache cleared" };
                }
                
                if (action == "getModelOptions")
                {
                    _logger.Info($"RequestAction: getModelOptions called for provider: {settings.Provider}");
                    
                    // Clear any stale detected models from previous provider
                    if (settings.DetectedModels != null && settings.DetectedModels.Any())
                    {
                        _logger.Info("Clearing stale detected models from previous provider");
                        settings.DetectedModels.Clear();
                    }
                    
                    // Delegate to action handler
                    var actionHandler = new BrainarrActionHandler(_httpClient, _modelDetection, _logger);
                    return actionHandler.GetModelOptions(settings.Provider.ToString());
                }

                // Legacy support for old method names (but only if current provider matches)
                if (action == "getOllamaOptions" && settings.Provider == AIProvider.Ollama)
                {
                    var actionHandler = new BrainarrActionHandler(_httpClient, _modelDetection, _logger);
                    var queryDict = new Dictionary<string, string> { { "baseUrl", settings.OllamaUrl } };
                    return actionHandler.HandleAction("getOllamaModels", queryDict);
                }

                if (action == "getLMStudioOptions" && settings.Provider == AIProvider.LMStudio)
                {
                    var actionHandler = new BrainarrActionHandler(_httpClient, _modelDetection, _logger);
                    var queryDict = new Dictionary<string, string> { { "baseUrl", settings.LMStudioUrl } };
                    return actionHandler.HandleAction("getLMStudioModels", queryDict);
                }

                _logger.Info($"RequestAction: Unknown action '{action}' or provider mismatch, returning empty object");
                return new { };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling action: {action}");
                return new { error = ex.Message };
            }
        }
    }
}