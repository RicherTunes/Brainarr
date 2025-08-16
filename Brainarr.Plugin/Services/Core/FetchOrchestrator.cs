using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists;

namespace Brainarr.Plugin.Services.Core
{
    internal class FetchOrchestrator
    {
        private readonly Logger _logger;
        private readonly IAIProviderFactory _providerFactory;
        private readonly IRecommendationCache _cache;
        private readonly IProviderHealth _healthMonitor;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IRateLimiter _rateLimiter;
        private readonly LibraryAwarePromptBuilder _promptBuilder;
        private readonly IterativeRecommendationStrategy _iterativeStrategy;
        private readonly IModelDetectionService _modelDetection;
        
        internal FetchOrchestrator(
            IAIProviderFactory providerFactory,
            IRecommendationCache cache,
            IProviderHealth healthMonitor,
            IRetryPolicy retryPolicy,
            IRateLimiter rateLimiter,
            IModelDetectionService modelDetection,
            Logger logger)
        {
            _logger = logger;
            _providerFactory = providerFactory;
            _cache = cache;
            _healthMonitor = healthMonitor;
            _retryPolicy = retryPolicy;
            _rateLimiter = rateLimiter;
            _modelDetection = modelDetection;
            _promptBuilder = new LibraryAwarePromptBuilder(logger);
            _iterativeStrategy = new IterativeRecommendationStrategy(logger, _promptBuilder);
        }
        
        internal IList<ImportListItemInfo> ExecuteFetch(BrainarrSettings settings, LibraryProfile libraryProfile)
        {
            // This method handles the synchronous-to-async bridge required by Lidarr
            // We use Task.Run to avoid blocking the thread pool
            return Task.Run(async () => await ExecuteFetchAsync(settings, libraryProfile)).GetAwaiter().GetResult();
        }
        
        private async Task<IList<ImportListItemInfo>> ExecuteFetchAsync(BrainarrSettings settings, LibraryProfile libraryProfile)
        {
            try
            {
                // Initialize provider with auto-detection
                var provider = await InitializeProviderAsync(settings);
                
                if (provider == null)
                {
                    _logger.Error("No AI provider configured");
                    return new List<ImportListItemInfo>();
                }
                
                // Generate cache key
                var libraryFingerprint = GenerateLibraryFingerprint(libraryProfile);
                var cacheKey = _cache.GenerateCacheKey(
                    settings.Provider.ToString(), 
                    settings.MaxRecommendations, 
                    libraryFingerprint);
                
                // Check cache first
                if (_cache.TryGet(cacheKey, out var cachedRecommendations))
                {
                    _logger.Info($"Returning {cachedRecommendations.Count} cached recommendations");
                    return cachedRecommendations;
                }
                
                // Check provider health
                var healthStatus = await _healthMonitor.CheckHealthAsync(
                    settings.Provider.ToString(), 
                    settings.BaseUrl);
                
                if (healthStatus == HealthStatus.Unhealthy)
                {
                    _logger.Warn($"Provider {settings.Provider} is unhealthy, returning empty list");
                    return new List<ImportListItemInfo>();
                }
                
                // Get recommendations with proper async handling
                var startTime = DateTime.UtcNow;
                var recommendations = await GetRecommendationsWithRetriesAsync(provider, settings, libraryProfile);
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                // Record metrics
                _healthMonitor.RecordSuccess(settings.Provider.ToString(), responseTime);
                
                if (!recommendations.Any())
                {
                    _logger.Warn("No recommendations received from AI provider");
                    return new List<ImportListItemInfo>();
                }
                
                // Convert to import items
                var uniqueItems = ConvertToImportItems(recommendations);
                
                // Cache the results
                _cache.Set(cacheKey, uniqueItems, TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes));
                
                _logger.Info($"Fetched {uniqueItems.Count} unique recommendations from {provider.ProviderName}");
                return uniqueItems;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching AI recommendations");
                _healthMonitor.RecordFailure(settings.Provider.ToString(), ex.Message);
                return new List<ImportListItemInfo>();
            }
        }
        
        private async Task<IAIProvider> InitializeProviderAsync(BrainarrSettings settings)
        {
            // Auto-detect models if enabled
            if (settings.AutoDetectModel)
            {
                await AutoDetectAndSetModelAsync(settings);
            }
            
            try
            {
                return _providerFactory.CreateProvider(settings);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create provider {settings.Provider}");
                return null;
            }
        }
        
        private async Task AutoDetectAndSetModelAsync(BrainarrSettings settings)
        {
            try
            {
                List<string> detectedModels = null;
                
                switch (settings.Provider)
                {
                    case AIProvider.Ollama:
                        detectedModels = await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
                        break;
                    case AIProvider.LMStudio:
                        detectedModels = await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
                        break;
                }
                
                if (detectedModels?.Any() == true)
                {
                    var preferredModel = SelectPreferredModel(detectedModels);
                    if (!string.IsNullOrEmpty(preferredModel))
                    {
                        switch (settings.Provider)
                        {
                            case AIProvider.Ollama:
                                settings.OllamaModel = preferredModel;
                                break;
                            case AIProvider.LMStudio:
                                settings.LMStudioModel = preferredModel;
                                break;
                        }
                        _logger.Info($"Auto-detected and set model: {preferredModel}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Model auto-detection failed, using configured model");
            }
        }
        
        private async Task<List<Recommendation>> GetRecommendationsWithRetriesAsync(
            IAIProvider provider, 
            BrainarrSettings settings, 
            LibraryProfile libraryProfile)
        {
            return await _rateLimiter.ExecuteAsync(
                settings.Provider.ToString().ToLower(),
                async () => await _retryPolicy.ExecuteAsync(
                    async () =>
                    {
                        if (settings.UseIterativeStrategy)
                        {
                            return await _iterativeStrategy.GetIterativeRecommendationsAsync(
                                provider,
                                libraryProfile,
                                settings.MaxRecommendations,
                                settings.IncludeArtistContext,
                                settings.MinimumRating);
                        }
                        else
                        {
                            var prompt = _promptBuilder.BuildLibraryAwarePrompt(
                                libraryProfile,
                                settings.MaxRecommendations,
                                includeArtistContext: settings.IncludeArtistContext);
                            
                            return await provider.GetRecommendationsAsync(prompt);
                        }
                    },
                    $"GetRecommendations_{settings.Provider}"));
        }
        
        private List<ImportListItemInfo> ConvertToImportItems(List<Recommendation> recommendations)
        {
            return recommendations
                .Where(r => !string.IsNullOrWhiteSpace(r.Artist) && !string.IsNullOrWhiteSpace(r.Album))
                .Select(ConvertToImportItem)
                .Where(item => item != null)
                .ToList();
        }
        
        private ImportListItemInfo ConvertToImportItem(Recommendation recommendation)
        {
            try
            {
                return new ImportListItemInfo
                {
                    Artist = recommendation.Artist.Trim(),
                    Album = recommendation.Album.Trim(),
                    ReleaseDate = ParseReleaseDate(recommendation.Year)
                };
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Failed to convert recommendation: {recommendation.Artist} - {recommendation.Album}");
                return null;
            }
        }
        
        private DateTime? ParseReleaseDate(string year)
        {
            if (string.IsNullOrWhiteSpace(year))
                return null;
                
            if (int.TryParse(year, out var yearInt) && yearInt > 1900 && yearInt <= DateTime.Now.Year + 1)
            {
                return new DateTime(yearInt, 1, 1);
            }
            
            return null;
        }
        
        private string SelectPreferredModel(List<string> models)
        {
            var preferredModels = new[]
            {
                "llama3.2", "llama3.1", "llama3", "llama2",
                "mistral", "mixtral", "gemma2", "qwen2.5"
            };
            
            foreach (var preferred in preferredModels)
            {
                var match = models.FirstOrDefault(m => m.ToLower().Contains(preferred));
                if (match != null) return match;
            }
            
            return models.FirstOrDefault();
        }
        
        private string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            if (profile == null)
                return "empty";
                
            var key = $"{profile.TotalArtists}_{profile.TotalAlbums}_{string.Join(",", profile.TopGenres.Take(3))}";
            return key.GetHashCode().ToString("X8");
        }
    }
}