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
using NzbDrone.Core.ImportLists.Brainarr.Utils;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

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
        // Optional services for iterative strategy and review queue
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly ILibraryAwarePromptBuilder _promptBuilder;
        private readonly IterativeRecommendationStrategy _iterativeStrategy;
        private readonly ReviewQueueService _reviewQueue;

        private IAIProvider _currentProvider;
        private AIProvider? _currentProviderType;

        public BrainarrOrchestrator(
            Logger logger,
            IProviderFactory providerFactory,
            ILibraryAnalyzer libraryAnalyzer,
            IRecommendationCache cache,
            IProviderHealthMonitor providerHealth,
            IRecommendationValidator validator,
            IModelDetectionService modelDetection,
            IHttpClient httpClient,
            IDuplicationPrevention duplicationPrevention = null,
            IArtistService artistService = null,
            IAlbumService albumService = null)
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
            _artistService = artistService;
            _albumService = albumService;
            _promptBuilder = new LibraryAwarePromptBuilder(logger);
            _iterativeStrategy = new IterativeRecommendationStrategy(logger, _promptBuilder);
            _reviewQueue = new ReviewQueueService(logger);
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

                    // Step 2: Validate provider configuration and health
                    if (!IsValidProviderConfiguration(settings) || !IsProviderHealthy())
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
                        // Return cached results as-is (already validated on first generation)
                        return cachedRecommendations;
                    }

                    // Step 5: Generate new recommendations (iterative when enabled)
                    List<Recommendation> recommendations;
                    var wantArtists = settings.RecommendationMode == RecommendationMode.Artists;
                    if (settings.EnableIterativeRefinement && _artistService != null && _albumService != null)
                    {
                        var allArtists = _artistService.GetAllArtists();
                        var allAlbums = _albumService.GetAllAlbums();
                        recommendations = await _iterativeStrategy.GetIterativeRecommendationsAsync(
                            _currentProvider,
                            libraryProfile,
                            allArtists,
                            allAlbums,
                            settings,
                            wantArtists);
                    }
                    else
                    {
                        recommendations = await GenerateRecommendationsAsync(settings, libraryProfile);
                    }
                    
                    // Step 6: Validate and filter recommendations
                    var validatedRecommendations = await ValidateRecommendationsAsync(recommendations, settings);

                    // Step 6.1: Safety gates and review queue handling
                    var minConf = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
                    var passNow = new List<Recommendation>();
                    var toQueue = new List<Recommendation>();
                    foreach (var r in validatedRecommendations)
                    {
                        var confOk = r.Confidence >= minConf;
                        bool hasMbids = true;
                        if (settings.RequireMbids)
                        {
                            hasMbids = wantArtists
                                ? !string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId)
                                : (!string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) && !string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId));
                        }
                        if (confOk && (!settings.RequireMbids || hasMbids)) passNow.Add(r);
                        else toQueue.Add(r);
                    }
                    if (settings.QueueBorderlineItems && toQueue.Count > 0)
                    {
                        _reviewQueue.Enqueue(toQueue, reason: "Safety gate (confidence/MBID)");
                        _logger.Info($"Queued {toQueue.Count} items for review (safety gates)");
                    }
                    var accepted = _reviewQueue.DequeueAccepted();
                    if (accepted.Count > 0) passNow.AddRange(accepted);
                    
                    // Step 6.1: Enrich with MBIDs if required
                    if (settings.RequireMbids)
                    {
                        try
                        {
                            if (settings.RecommendationMode == RecommendationMode.Artists)
                            {
                                var artistResolver = new NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver(_logger);
                                passNow = await artistResolver.EnrichArtistsAsync(passNow);
                            }
                            else
                            {
                                var albumResolver = new NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.MusicBrainzResolver(_logger);
                                passNow = await albumResolver.EnrichWithMbidsAsync(passNow);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "MBID enrichment failed; continuing without strict enforcement");
                        }
                    }

                    // Step 7: Convert to import list items
                    var importItems = ConvertToImportListItems(passNow);

                    // Step 7.1: If MBIDs required, drop items without IDs
                    if (settings.RequireMbids)
                    {
                        if (settings.RecommendationMode == RecommendationMode.Artists)
                        {
                            importItems = importItems.Where(i => !string.IsNullOrWhiteSpace(i.ArtistMusicBrainzId)).ToList();
                        }
                        else
                        {
                            importItems = importItems.Where(i => !string.IsNullOrWhiteSpace(i.ArtistMusicBrainzId) && !string.IsNullOrWhiteSpace(i.AlbumMusicBrainzId)).ToList();
                        }
                    }

                    // Step 8: Filter out items already in the library (artist/album exists)
                    importItems = _libraryAnalyzer.FilterDuplicates(importItems);

                    // Step 9: Deduplicate within this batch
                    importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);
                    
                    // Step 10: Cache the results
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
            if (_currentProvider != null && _currentProviderType.HasValue && _currentProviderType.Value == settings.Provider)
            {
                _logger.Debug($"Provider {settings.Provider} already initialized for current settings");
                return;
            }

            _logger.Info($"Initializing provider: {settings.Provider}");
            
            try
            {
                _currentProvider = _providerFactory.CreateProvider(settings, _httpClient, _logger);
                _currentProviderType = settings.Provider;
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

            return _providerHealth.IsHealthy(_currentProvider.ProviderName);
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
                var connectionTest = SafeAsyncHelper.RunSafeSync(() => TestProviderConnectionAsync(settings));
                if (!connectionTest)
                {
                    failures.Add(new ValidationFailure("Provider", "Unable to connect to AI provider"));
                }

                // Validate model availability for local providers
                if (settings.Provider == AIProvider.Ollama)
                {
                    var models = SafeAsyncHelper.RunSafeSync(() => _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl));
                    if (!models.Any())
                    {
                        failures.Add(new ValidationFailure("Model", "No models detected for Ollama provider"));
                    }
                }
                else if (settings.Provider == AIProvider.LMStudio)
                {
                    var models = SafeAsyncHelper.RunSafeSync(() => _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl));
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
                        "getmodeloptions" => SafeAsyncHelper.RunSafeSync(() => GetModelOptionsAsync(settings)),
                        "detectmodels" => SafeAsyncHelper.RunSafeSync(() => DetectModelsAsync(settings)),
                        "testconnection" => SafeAsyncHelper.RunSafeSync(() => TestProviderConnectionAsync(settings)),
                        "getproviderstatus" => GetProviderStatus(),
                        // Review Queue actions
                        "review/getqueue" => new { items = _reviewQueue.GetPending() },
                        "review/accept" => HandleReviewUpdate(query, ReviewQueueService.ReviewStatus.Accepted),
                        "review/reject" => HandleReviewUpdate(query, ReviewQueueService.ReviewStatus.Rejected),
                        "review/never"  => HandleReviewNever(query),
                        "review/getoptions" => GetReviewOptions(),
                        "review/getsummaryoptions" => GetReviewSummaryOptions(),
                        "review/apply" => ApplyBatchApprovals(query),
                        "review/approveall" => ApproveAllPending(),
                        "review/rejectall" => RejectAllPending(),
                        "review/clear" => ClearQueue(query),
                        _ => throw new NotSupportedException($"Action '{action}' is not supported")
                    };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling action: {action}");
                return new { error = ex.Message };
            }
        }

        private object HandleReviewUpdate(IDictionary<string,string> query, ReviewQueueService.ReviewStatus status)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            if (string.IsNullOrWhiteSpace(artist)) return new { ok = false, error = "artist is required" };
            var ok = _reviewQueue.SetStatus(artist, album, status);
            return new { ok };
        }

        private object HandleReviewNever(IDictionary<string,string> query)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            if (string.IsNullOrWhiteSpace(artist)) return new { ok = false, error = "artist is required" };
            var ok = _reviewQueue.SetStatus(artist, album, ReviewQueueService.ReviewStatus.Never);
            return new { ok };
        }

        private object GetReviewOptions()
        {
            var items = _reviewQueue.GetPending();
            var options = items.Select(i => new
            {
                value = $"{i.Artist}|{i.Album}",
                name = string.IsNullOrWhiteSpace(i.Album) ? i.Artist : $"{i.Artist} — {i.Album}"
            }).ToList();
            return new { options };
        }

        private object GetReviewSummaryOptions()
        {
            var (pending, accepted, rejected, never) = _reviewQueue.GetCounts();
            var options = new List<object>
            {
                new { value = $"pending:{pending}", name = $"Pending: {pending}" },
                new { value = $"accepted:{accepted}", name = $"Accepted: {accepted}" },
                new { value = $"rejected:{rejected}", name = $"Rejected: {rejected}" },
                new { value = $"never:{never}", name = $"Never: {never}" }
            };
            return new { options };
        }

        private object ApplyBatchApprovals(IDictionary<string,string> query)
        {
            // Accept comma-separated keys of form "Artist|Album" (Album optional for artist mode)
            var keysCsv = query.TryGetValue("keys", out var k) ? k : null;
            if (string.IsNullOrWhiteSpace(keysCsv))
            {
                return new { ok = false, error = "keys is required (CSV of 'Artist|Album')" };
            }

            var keys = keysCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            int applied = 0;
            foreach (var key in keys)
            {
                var parts = key.Split('|');
                var artist = parts.Length > 0 ? parts[0] : null;
                var album = parts.Length > 1 ? parts[1] : null;
                if (!string.IsNullOrWhiteSpace(artist))
                {
                    if (_reviewQueue.SetStatus(artist, album, ReviewQueueService.ReviewStatus.Accepted))
                    {
                        applied++;
                    }
                }
            }

            var released = _reviewQueue.DequeueAccepted();
            return new { ok = true, approved = applied, released = released.Count };
        }

        private object ApproveAllPending()
        {
            var pending = _reviewQueue.GetPending();
            foreach (var item in pending)
            {
                _reviewQueue.SetStatus(item.Artist, item.Album, ReviewQueueService.ReviewStatus.Accepted);
            }
            var released = _reviewQueue.DequeueAccepted();
            return new { ok = true, approved = pending.Count, released = released.Count };
        }

        private object RejectAllPending()
        {
            var pending = _reviewQueue.GetPending();
            int updated = 0;
            foreach (var item in pending)
            {
                if (_reviewQueue.SetStatus(item.Artist, item.Album, ReviewQueueService.ReviewStatus.Rejected)) updated++;
            }
            return new { ok = true, rejected = updated };
        }

        private object ClearQueue(IDictionary<string,string> query)
        {
            var status = query.TryGetValue("status", out var s) ? s?.ToLowerInvariant() : null;
            ReviewQueueService.ReviewStatus? filter = null;
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                filter = status switch
                {
                    "pending" => ReviewQueueService.ReviewStatus.Pending,
                    "accepted" => ReviewQueueService.ReviewStatus.Accepted,
                    "rejected" => ReviewQueueService.ReviewStatus.Rejected,
                    "never" => ReviewQueueService.ReviewStatus.Never,
                    _ => (ReviewQueueService.ReviewStatus?)null
                };
            }
            _reviewQueue.Clear(filter);
            var (pending, accepted, rejected, never) = _reviewQueue.GetCounts();
            return new { ok = true, counts = new { pending, accepted, rejected, never } };
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
            // Use artist-aware prompt when requested
            var prompt = _libraryAnalyzer.BuildPrompt(libraryProfile, settings.MaxRecommendations, settings.DiscoveryMode, settings.RecommendationMode == RecommendationMode.Artists);
            var recommendations = await _currentProvider.GetRecommendationsAsync(prompt);
            return recommendations;
        }

        private async Task<List<Recommendation>> ValidateRecommendationsAsync(List<Recommendation> recommendations, BrainarrSettings settings)
        {
            _logger.Debug($"Validating {recommendations.Count} recommendations");
            
            var validationResult = _validator.ValidateBatch(recommendations, settings.RecommendationMode == RecommendationMode.Artists);
            
            _logger.Debug($"Validation result: {validationResult.ValidCount}/{validationResult.TotalCount} passed ({validationResult.PassRate:F1}%)");
            
            return validationResult.ValidRecommendations;
        }

        private List<ImportListItemInfo> ConvertToImportListItems(List<Recommendation> recommendations)
        {
            // Convert model recommendations to ImportList items; global deduplication occurs later
            return recommendations
                .Select(r => new ImportListItemInfo
                {
                    Artist = r.Artist,
                    ArtistMusicBrainzId = string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) ? null : r.ArtistMusicBrainzId,
                    Album = r.Album,
                    AlbumMusicBrainzId = string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId) ? null : r.AlbumMusicBrainzId,
                    ReleaseDate = r.Year.HasValue ? new DateTime(r.Year.Value, 1, 1) : DateTime.MinValue
                })
                .ToList();
        }

        private static string GenerateCacheKey(BrainarrSettings settings, LibraryProfile profile)
        {
            // Build a deterministic fingerprint from salient profile characteristics
            var topGenres = profile?.TopGenres != null
                ? string.Join(",", profile.TopGenres.Keys.OrderBy(k => k).Take(5))
                : "";
            var topArtists = profile?.TopArtists != null
                ? string.Join(",", profile.TopArtists.OrderBy(a => a).Take(5))
                : "";

            var raw = string.Join("|", new[]
            {
                settings.Provider.ToString(),
                settings.DiscoveryMode.ToString(),
                settings.MaxRecommendations.ToString(),
                topGenres,
                topArtists
            });

            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
            var hash = Convert.ToBase64String(sha.ComputeHash(bytes))
                .Replace("/", "_")
                .Replace("+", "-");
            return $"rec_{hash.Substring(0, 24)}";
        }

        private async Task<object> GetModelOptionsAsync(BrainarrSettings settings)
        {
            // Return options in the UI-consumed shape: { options: [{ Value, Name }] }
            // Local providers (live-detected models)
            if (settings.Provider == AIProvider.Ollama)
            {
                var models = await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
                if (models != null && models.Any())
                {
                    return new
                    {
                        options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList()
                    };
                }

                return GetFallbackOptions(AIProvider.Ollama);
            }
            else if (settings.Provider == AIProvider.LMStudio)
            {
                var models = await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
                if (models != null && models.Any())
                {
                    return new
                    {
                        options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList()
                    };
                }

                return GetFallbackOptions(AIProvider.LMStudio);
            }

            // Cloud providers (static options)
            return GetStaticModelOptions(settings.Provider);
        }

        private async Task<object> DetectModelsAsync(BrainarrSettings settings)
        {
            // Keep shape consistent with GetModelOptionsAsync for UI consumption
            if (settings.Provider == AIProvider.Ollama)
            {
                var models = await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
                return new { options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList() };
            }
            else if (settings.Provider == AIProvider.LMStudio)
            {
                var models = await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
                return new { options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList() };
            }

            return new { options = Array.Empty<object>() };
        }

        private static object GetStaticModelOptions(AIProvider provider)
        {
            // Return appropriate model options based on provider in the expected UI shape
            IEnumerable<string> values = provider switch
            {
                AIProvider.OpenAI => new[] { "gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo" },
                AIProvider.Anthropic => new[] { "claude-3.5-sonnet", "claude-3.5-haiku", "claude-3-opus" },
                AIProvider.Perplexity => new[] { "sonar-large", "sonar-small", "sonar-huge" },
                _ => Array.Empty<string>()
            };

            return new
            {
                options = values.Select(v => new { value = v, name = FormatEnumName(v) }).ToList()
            };
        }

        private static object GetFallbackOptions(AIProvider provider)
        {
            // Fallback options used when detection fails or URLs are not set
            return provider switch
            {
                AIProvider.Ollama => new
                {
                    options = new[]
                    {
                        new { value = "qwen2.5:latest", name = "Qwen 2.5 (Recommended)" },
                        new { value = "qwen2.5:7b", name = "Qwen 2.5 7B" },
                        new { value = "llama3.2:latest", name = "Llama 3.2" },
                        new { value = "mistral:latest", name = "Mistral" }
                    }
                },
                AIProvider.LMStudio => new
                {
                    options = new[]
                    {
                        new { value = "local-model", name = "Currently Loaded Model" }
                    }
                },
                _ => new { options = Array.Empty<object>() }
            };
        }

        private static string FormatEnumName(string enumValue)
        {
            // Map historical enum-like names into readable display names
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

        private static string FormatModelName(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return "Unknown Model";

            if (modelId.Contains("/"))
            {
                var parts = modelId.Split('/');
                if (parts.Length >= 2)
                {
                    var org = parts[0];
                    var modelName = CleanModelName(parts[1]);
                    return $"{modelName} ({org})";
                }
            }

            if (modelId.Contains(":"))
            {
                var parts = modelId.Split(':');
                if (parts.Length >= 2)
                {
                    var modelName = CleanModelName(parts[0]);
                    var tag = parts[1];
                    return $"{modelName}:{tag}";
                }
            }

            return CleanModelName(modelId);
        }

        private static string CleanModelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var cleaned = name
                .Replace("-", " ")
                .Replace("_", " ")
                .Replace(".", " ");

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bqwen\\b", "Qwen", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bllama\\b", "Llama", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bmistral\\b", "Mistral", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bgemma\\b", "Gemma", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bphi\\b", "Phi", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\s+", " ").Trim();

            return cleaned;
        }

        // ====== VALIDATION HELPERS ======

        private static bool IsValidProviderConfiguration(BrainarrSettings settings)
        {
            // For local providers, ensure URLs are well-formed and ports valid
            try
            {
                if (settings.Provider == AIProvider.Ollama)
                {
                    return IsValidHttpUrl(settings.OllamaUrl);
                }
                if (settings.Provider == AIProvider.LMStudio)
                {
                    return IsValidHttpUrl(settings.LMStudioUrl);
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static bool IsValidHttpUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (!(uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                  uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))) return false;
            // Port must be within valid range if specified
            if (!uri.IsDefaultPort && (uri.Port < 1 || uri.Port > 65535)) return false;
            return true;
        }
    }
}
