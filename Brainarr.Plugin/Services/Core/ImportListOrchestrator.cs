using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Orchestrates the entire recommendation fetching process
    /// </summary>
    public class ImportListOrchestrator : IImportListOrchestrator
    {
        private readonly IProviderLifecycleManager _providerManager;
        private readonly ILibraryContextBuilder _libraryBuilder;
        private readonly IRecommendationCache _cache;
        private readonly IRateLimiter _rateLimiter;
        private readonly IRetryPolicy _retryPolicy;
        private readonly LibraryAwarePromptBuilder _promptBuilder;
        private readonly IterativeRecommendationStrategy _iterativeStrategy;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly ModelDetectionService _modelDetection;
        private readonly Logger _logger;
        private readonly int _definitionId;

        public ImportListOrchestrator(
            IProviderLifecycleManager providerManager,
            ILibraryContextBuilder libraryBuilder,
            IRecommendationCache cache,
            IRateLimiter rateLimiter,
            IRetryPolicy retryPolicy,
            LibraryAwarePromptBuilder promptBuilder,
            IterativeRecommendationStrategy iterativeStrategy,
            IArtistService artistService,
            IAlbumService albumService,
            ModelDetectionService modelDetection,
            Logger logger,
            int definitionId)
        {
            _providerManager = providerManager;
            _libraryBuilder = libraryBuilder;
            _cache = cache;
            _rateLimiter = rateLimiter;
            _retryPolicy = retryPolicy;
            _promptBuilder = promptBuilder;
            _iterativeStrategy = iterativeStrategy;
            _artistService = artistService;
            _albumService = albumService;
            _modelDetection = modelDetection;
            _logger = logger;
            _definitionId = definitionId;
        }

        public async Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings)
        {
            try
            {
                // Step 1: Initialize provider
                var initialized = await _providerManager.InitializeProviderAsync(settings);
                if (!initialized)
                {
                    _logger.Error("Failed to initialize AI provider");
                    return new List<ImportListItemInfo>();
                }

                var provider = _providerManager.GetProvider();
                if (provider == null)
                {
                    _logger.Error("No AI provider configured");
                    return new List<ImportListItemInfo>();
                }

                // Step 2: Build library profile
                var libraryProfile = await _libraryBuilder.BuildLibraryProfileAsync();
                var libraryFingerprint = _libraryBuilder.GenerateLibraryFingerprint(libraryProfile);
                
                // Step 3: Check cache
                var cacheKey = _cache.GenerateCacheKey(
                    settings.Provider.ToString(), 
                    settings.MaxRecommendations, 
                    libraryFingerprint);
                
                if (_cache.TryGet(cacheKey, out var cachedRecommendations))
                {
                    _logger.Info($"Returning {cachedRecommendations.Count} cached recommendations");
                    return cachedRecommendations;
                }

                // Step 4: Check provider health
                var isHealthy = await _providerManager.IsProviderHealthyAsync();
                if (!isHealthy)
                {
                    _logger.Warn($"Provider {settings.Provider} is unhealthy, returning empty list");
                    return new List<ImportListItemInfo>();
                }
                
                // Step 5: Get recommendations with rate limiting and retry
                var startTime = DateTime.UtcNow;
                var recommendations = await _rateLimiter.ExecuteAsync(
                    settings.Provider.ToString().ToLower(), 
                    async () =>
                    {
                        return await _retryPolicy.ExecuteAsync(
                            async () => await GetLibraryAwareRecommendationsAsync(provider, libraryProfile, settings),
                            $"GetRecommendations_{settings.Provider}");
                    });
                
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                // Step 6: Record metrics
                _providerManager.RecordSuccess(responseTime);
                
                if (!recommendations.Any())
                {
                    _logger.Warn("No recommendations received from AI provider");
                    return new List<ImportListItemInfo>();
                }

                // Step 7: Convert to import items
                var uniqueItems = recommendations
                    .Where(r => !string.IsNullOrWhiteSpace(r.Artist) && !string.IsNullOrWhiteSpace(r.Album))
                    .Select(r => ConvertToImportItem(r))
                    .Where(item => item != null)
                    .ToList();

                // Step 8: Cache the results
                _cache.Set(cacheKey, uniqueItems, TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes));

                _logger.Info($"Fetched {uniqueItems.Count} unique recommendations from {provider.ProviderName}");
                return uniqueItems;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching AI recommendations");
                _providerManager.RecordFailure(ex.Message);
                return new List<ImportListItemInfo>();
            }
        }

        public async Task<bool> TestConfigurationAsync(BrainarrSettings settings, List<ValidationFailure> failures)
        {
            try
            {
                // Initialize provider
                var initialized = await _providerManager.InitializeProviderAsync(settings);
                if (!initialized)
                {
                    failures.Add(new ValidationFailure(nameof(settings.Provider), 
                        "AI provider not configured"));
                    return false;
                }

                var provider = _providerManager.GetProvider();
                if (provider == null)
                {
                    failures.Add(new ValidationFailure(nameof(settings.Provider), 
                        "AI provider not configured"));
                    return false;
                }

                // Test connection
                var connected = await _providerManager.TestConnectionAsync();
                if (!connected)
                {
                    failures.Add(new ValidationFailure(string.Empty, 
                        $"Cannot connect to {provider.ProviderName}"));
                    return false;
                }

                // Try to detect available models for local providers
                if (settings.Provider == AIProvider.Ollama)
                {
                    var models = await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
                    
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
                        return false;
                    }
                }
                else if (settings.Provider == AIProvider.LMStudio)
                {
                    var models = await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
                    
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
                        return false;
                    }
                }
                else
                {
                    _logger.Info($"✅ Connected successfully to {settings.Provider}");
                }

                _logger.Info($"Test successful: Connected to {provider.ProviderName}");
                return true;
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Test failed: {ex.Message}"));
                return false;
            }
        }

        private async Task<List<Recommendation>> GetLibraryAwareRecommendationsAsync(
            IAIProvider provider, 
            LibraryProfile profile, 
            BrainarrSettings settings)
        {
            try
            {
                // Get complete library data for context
                var allArtists = _artistService.GetAllArtists();
                var allAlbums = _albumService.GetAllAlbums();
                
                _logger.Info($"Using library-aware strategy with {allArtists.Count} artists, {allAlbums.Count} albums");
                
                // Use iterative strategy to get high-quality recommendations
                var recommendations = await _iterativeStrategy.GetIterativeRecommendationsAsync(
                    provider, profile, allArtists, allAlbums, settings);
                
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Library-aware recommendation failed, falling back to simple prompt");
                return await GetSimpleRecommendationsAsync(provider, profile, settings);
            }
        }
        
        private async Task<List<Recommendation>> GetSimpleRecommendationsAsync(
            IAIProvider provider,
            LibraryProfile profile,
            BrainarrSettings settings)
        {
            // Fallback to original simple prompt approach
            var prompt = BuildSimplePrompt(profile, settings);
            return await provider.GetRecommendationsAsync(prompt);
        }
        
        private string BuildSimplePrompt(LibraryProfile profile, BrainarrSettings settings)
        {
            var discoveryFocus = _libraryBuilder.GetDiscoveryFocus(settings.DiscoveryMode);
            
            var prompt = $@"Based on this music library, recommend {settings.MaxRecommendations} new albums to discover:

Library: {profile.TotalArtists} artists, {profile.TotalAlbums} albums
Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", profile.TopArtists.Take(10))}

Return a JSON array with exactly {settings.MaxRecommendations} recommendations.
Each item must have: artist, album, genre, confidence (0.0-1.0), reason (brief).

Focus on: {discoveryFocus}

Example format:
[
  {{""artist"": ""Artist Name"", ""album"": ""Album Title"", ""genre"": ""Genre"", ""confidence"": 0.8, ""reason"": ""Similar to your jazz collection""}}
]";

            return prompt;
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
                    ImportListId = _definitionId,
                    Artist = cleanArtist,
                    Album = cleanAlbum,
                    ArtistMusicBrainzId = null,
                    AlbumMusicBrainzId = null,
                    ReleaseDate = DateTime.UtcNow.AddDays(-30)
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to convert recommendation: {ex.Message}");
                return null;
            }
        }
    }
}