using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Common.Http;
using FluentValidation.Results;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Main orchestrator implementation for the Brainarr plugin that coordinates all aspects of
    /// AI-powered music recommendation generation including provider management,
    /// health monitoring, caching, and library analysis.
    /// 
    /// This orchestrator consolidates the logic that was previously scattered across
    /// multiple overlapping orchestrator classes, providing a single, well-defined
    /// orchestrator responsible for the entire recommendation workflow.
    /// 
    /// Architecture follows the Single Responsibility Principle while maintaining
    /// clear separation of concerns through dependency injection.
    /// </summary>
    public class BrainarrOrchestrator : IBrainarrOrchestrator
    {
        private readonly Logger _logger;
        private readonly IProviderFactory _providerFactory;
        private readonly ILibraryAnalyzer _libraryAnalyzer;
        private readonly IRecommendationCache _cache;
        private readonly IProviderHealthMonitor _providerHealth;
        private readonly IRecommendationValidator _validator;
        private readonly IModelDetectionService _modelDetection;
        private readonly IHttpClient _httpClient;
        private readonly IDuplicationPrevention _duplicationPrevention;

        private IAIProvider _currentProvider;

        public BrainarrOrchestrator(
            Logger logger,
            IProviderFactory providerFactory,
            ILibraryAnalyzer libraryAnalyzer,
            IRecommendationCache cache,
            IProviderHealthMonitor providerHealth,
            IRecommendationValidator validator,
            IModelDetectionService modelDetection,
            IHttpClient httpClient,
            IDuplicationPrevention duplicationPrevention = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
            _libraryAnalyzer = libraryAnalyzer ?? throw new ArgumentNullException(nameof(libraryAnalyzer));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _providerHealth = providerHealth ?? throw new ArgumentNullException(nameof(providerHealth));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _modelDetection = modelDetection ?? throw new ArgumentNullException(nameof(modelDetection));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _duplicationPrevention = duplicationPrevention ?? new DuplicationPreventionService(logger);
        }

        // ====== CORE RECOMMENDATION WORKFLOW ======

        public async Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings)
        {
            // CRITICAL: Prevent concurrent executions which cause duplicates
            var operationKey = $"fetch_{settings.Provider}_{settings.GetHashCode()}";
            
            return await _duplicationPrevention.PreventConcurrentFetch(operationKey, async () =>
            {
                _logger.Info("Starting consolidated recommendation workflow");
                
                try
                {
                    // Step 1: Initialize provider if needed
                    InitializeProvider(settings);

                    // Step 2: Check provider health
                    if (!IsProviderHealthy())
                    {
                        _logger.Warn("Provider is unhealthy, cannot generate recommendations");
                        return new List<ImportListItemInfo>();
                    }

                    // Step 3: Get library profile (with caching)
                    var libraryProfile = await GetLibraryProfileAsync(settings);
                    
                    // Step 4: Check cache first
                    var cacheKey = GenerateCacheKey(settings, libraryProfile);
                    if (_cache.TryGet(cacheKey, out var cachedRecommendations))
                    {
                        _logger.Debug("Retrieved recommendations from cache");
                        // Apply historical filter to prevent duplicates across sessions
                        var filteredCached = _duplicationPrevention.FilterPreviouslyRecommended(cachedRecommendations);
                        return filteredCached;
                    }

                    // Step 5: Generate new recommendations
                    var recommendations = await GenerateRecommendationsAsync(settings, libraryProfile);
                    
                    // Step 6: Validate and filter recommendations
                    var validatedRecommendations = await ValidateRecommendationsAsync(recommendations);
                    
                    // Step 7: Convert to import list items (with deduplication)
                    var importItems = ConvertToImportListItems(validatedRecommendations);
                    
                    // Step 8: Apply global deduplication and history filtering
                    importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);
                    importItems = _duplicationPrevention.FilterPreviouslyRecommended(importItems);
                    
                    // Step 9: Cache the results
                    _cache.Set(cacheKey, importItems, settings.CacheDuration);
                    
                    _logger.Info($"Generated {importItems.Count} validated recommendations");
                    return importItems;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in consolidated recommendation workflow");
                    return new List<ImportListItemInfo>();
                }
            });
        }

        public IList<ImportListItemInfo> FetchRecommendations(BrainarrSettings settings)
        {
            // Synchronous wrapper for backward compatibility
            return FetchRecommendationsAsync(settings).GetAwaiter().GetResult();
        }

        // ====== PROVIDER MANAGEMENT ======

        public void InitializeProvider(BrainarrSettings settings)
        {
            // Check if we already have the correct provider initialized
            if (_currentProvider != null && _currentProvider.GetType().Name.Contains(settings.Provider.ToString()))
            {
                _logger.Debug($"Provider {settings.Provider} already initialized for current settings");
                return;
            }

            _logger.Info($"Initializing provider: {settings.Provider}");
            
            try
            {
                _currentProvider = _providerFactory.CreateProvider(settings, _httpClient, _logger);
                _logger.Info($"Successfully initialized {settings.Provider} provider");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to initialize {settings.Provider} provider");
                _currentProvider = null;
                throw;
            }
        }

        public void UpdateProviderConfiguration(BrainarrSettings settings)
        {
            // This is equivalent to InitializeProvider but makes intent clearer
            InitializeProvider(settings);
        }

        public async Task<bool> TestProviderConnectionAsync(BrainarrSettings settings)
        {
            try
            {
                InitializeProvider(settings);
                
                if (_currentProvider == null)
                    return false;

                var testResult = await _currentProvider.TestConnectionAsync();
                _logger.Debug($"Provider connection test result: {testResult}");
                
                return testResult;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Provider connection test failed");
                return false;
            }
        }

        public bool IsProviderHealthy()
        {
            if (_currentProvider == null)
                return false;

            return _providerHealth.IsHealthy(_currentProvider.GetType().Name);
        }

        public string GetProviderStatus()
        {
            if (_currentProvider == null)
                return "Not Initialized";

            var providerName = _currentProvider.ProviderName;
            var isHealthy = _providerHealth.IsHealthy(providerName);
            
            return $"{providerName}: {(isHealthy ? "Healthy" : "Unhealthy")}";
        }

        // ====== CONFIGURATION VALIDATION ======

        public void ValidateConfiguration(BrainarrSettings settings, List<ValidationFailure> failures)
        {
            try
            {
                // Test provider connection
                var connectionTest = TestProviderConnectionAsync(settings).GetAwaiter().GetResult();
                if (!connectionTest)
                {
                    failures.Add(new ValidationFailure("Provider", "Unable to connect to AI provider"));
                }

                // Validate model availability for local providers
                if (settings.Provider == AIProvider.Ollama)
                {
                    var models = _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl).GetAwaiter().GetResult();
                    if (!models.Any())
                    {
                        failures.Add(new ValidationFailure("Model", "No models detected for Ollama provider"));
                    }
                }
                else if (settings.Provider == AIProvider.LMStudio)
                {
                    var models = _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl).GetAwaiter().GetResult();
                    if (!models.Any())
                    {
                        failures.Add(new ValidationFailure("Model", "No models detected for LM Studio provider"));
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure("Configuration", $"Validation error: {ex.Message}"));
            }
        }

        // ====== UI ACTIONS ======

        public object HandleAction(string action, IDictionary<string, string> query, BrainarrSettings settings)
        {
            _logger.Debug($"Handling UI action: {action}");

            try
            {
                return action.ToLowerInvariant() switch
                {
                    "getmodeloptions" => GetModelOptionsAsync(settings).GetAwaiter().GetResult(),
                    "detectmodels" => DetectModelsAsync(settings).GetAwaiter().GetResult(),
                    "testconnection" => TestProviderConnectionAsync(settings).GetAwaiter().GetResult(),
                    "getproviderstatus" => GetProviderStatus(),
                    _ => throw new NotSupportedException($"Action '{action}' is not supported")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling action: {action}");
                return new { error = ex.Message };
            }
        }

        // ====== LIBRARY ANALYSIS ======

        public async Task<LibraryProfile> GetLibraryProfileAsync(BrainarrSettings settings)
        {
            var profileCacheKey = $"library_profile_{settings.SamplingStrategy}";
            
            if (_cache.TryGet(profileCacheKey, out var cachedItems))
            {
                _logger.Debug("Retrieved library profile from cache");
                // We'd need to convert back from ImportListItemInfo to LibraryProfile
                // For now, let's regenerate the profile instead of caching it
            }

            _logger.Debug("Generating new library profile");
            var profile = _libraryAnalyzer.AnalyzeLibrary();
            
            return profile;
        }

        // ====== PRIVATE HELPER METHODS ======

        private async Task<List<Recommendation>> GenerateRecommendationsAsync(BrainarrSettings settings, LibraryProfile libraryProfile)
        {
            if (_currentProvider == null)
                throw new InvalidOperationException("Provider not initialized");

            _logger.Debug("Generating recommendations from AI provider");
            var prompt = _libraryAnalyzer.BuildPrompt(libraryProfile, settings.MaxRecommendations, settings.DiscoveryMode);
            var recommendations = await _currentProvider.GetRecommendationsAsync(prompt);
            return recommendations;
        }

        private async Task<List<Recommendation>> ValidateRecommendationsAsync(List<Recommendation> recommendations)
        {
            _logger.Debug($"Validating {recommendations.Count} recommendations");
            
            var validationResult = _validator.ValidateBatch(recommendations);
            
            _logger.Debug($"Validation result: {validationResult.ValidCount}/{validationResult.TotalCount} passed ({validationResult.PassRate:F1}%)");
            
            return validationResult.ValidRecommendations;
        }

        private List<ImportListItemInfo> ConvertToImportListItems(List<Recommendation> recommendations)
        {
            // Deduplicate recommendations to prevent the same artist/album from appearing multiple times
            // This fixes the bug where artists were getting duplicated up to 8 times in Lidarr
            var groups = recommendations
                .GroupBy(r => new { 
                    Artist = r.Artist?.Trim().ToLowerInvariant(), 
                    Album = r.Album?.Trim().ToLowerInvariant() 
                })
                .ToList();

            // Log if duplicates were found
            var duplicateCount = recommendations.Count - groups.Count;
            if (duplicateCount > 0)
            {
                _logger.Info($"Removed {duplicateCount} duplicate recommendations from {recommendations.Count} total");
                
                // Log specific duplicates for debugging
                var duplicateGroups = groups.Where(g => g.Count() > 1);
                foreach (var group in duplicateGroups)
                {
                    var first = group.First();
                    _logger.Debug($"Duplicate found: {first.Artist} - {first.Album} (appeared {group.Count()} times)");
                }
            }

            var deduplicatedRecommendations = groups
                .Select(g => g.First())  // Take the first occurrence of each unique artist/album pair
                .Select(r => new ImportListItemInfo
                {
                    Artist = r.Artist,
                    Album = r.Album,
                    ReleaseDate = r.Year.HasValue ? new DateTime(r.Year.Value, 1, 1) : DateTime.MinValue
                })
                .ToList();

            return deduplicatedRecommendations;
        }

        private static string GenerateCacheKey(BrainarrSettings settings, LibraryProfile profile)
        {
            var keyComponents = new[]
            {
                settings.Provider.ToString(),
                settings.DiscoveryMode.ToString(),
                settings.MaxRecommendations.ToString(),
                profile?.GetHashCode().ToString() ?? "empty"
            };
            
            return string.Join("_", keyComponents);
        }

        private async Task<object> GetModelOptionsAsync(BrainarrSettings settings)
        {
            if (settings.Provider == AIProvider.Ollama)
            {
                return await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
            }
            else if (settings.Provider == AIProvider.LMStudio)
            {
                return await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
            }
            
            // For cloud providers, return static model lists
            return GetStaticModelOptions(settings.Provider);
        }

        private async Task<object> DetectModelsAsync(BrainarrSettings settings)
        {
            if (settings.Provider == AIProvider.Ollama)
            {
                return await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
            }
            else if (settings.Provider == AIProvider.LMStudio)
            {
                return await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
            }
            
            return new List<string>();
        }

        private static object GetStaticModelOptions(AIProvider provider)
        {
            // Return appropriate model options based on provider
            // This would typically come from the provider settings classes
            return provider switch
            {
                AIProvider.OpenAI => new[] { "gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo" },
                AIProvider.Anthropic => new[] { "claude-3.5-sonnet", "claude-3.5-haiku", "claude-3-opus" },
                AIProvider.Perplexity => new[] { "sonar-large", "sonar-small", "sonar-huge" },
                _ => new string[0]
            };
        }
    }
}