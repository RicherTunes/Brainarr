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

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
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
            return FetchRecommendationsAsync(settings).GetAwaiter().GetResult();
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
            try
            {
                var detectionTask = settings.Provider == AIProvider.Ollama
                    ? _modelDetection.DetectOllamaModelsAsync(settings.BaseUrl)
                    : _modelDetection.DetectLMStudioModelsAsync(settings.BaseUrl);

                var availableModels = detectionTask.GetAwaiter().GetResult();

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
    }
}