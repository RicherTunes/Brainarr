using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Common.Http;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Utils;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using System.Diagnostics;

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
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.IMusicBrainzResolver _mbidResolver;
        private NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.IArtistMbidResolver _artistResolver;
        private NzbDrone.Core.MetadataSource.ISearchForNewArtist _artistSearchService;
        private readonly ReviewQueueService _reviewQueue;
        private readonly RecommendationHistory _history;
        private readonly ILibraryAwarePromptBuilder _promptBuilder;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics _metrics;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Core.IRecommendationSchemaValidator _schemaValidator;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.IRecommendationSanitizer _sanitizer;

        private IAIProvider _currentProvider;
        private AIProvider? _currentProviderType;

        // Lightweight in-memory cache for library profile to avoid repeated scans when hitting recommendation cache
        private readonly object _profileCacheLock = new object();
        private LibraryProfile _cachedLibraryProfile;
        private string _cachedLibraryProfileKey;
        private DateTime _cachedLibraryProfileAt = DateTime.MinValue;
        private static readonly TimeSpan LibraryProfileTtl = TimeSpan.FromMinutes(10);

        // Lightweight in-memory recommendation cache to complement IRecommendationCache mocks
        private readonly object _recCacheLock = new object();
        private readonly Dictionary<string, (DateTime expiresUtc, List<ImportListItemInfo> items)> _localRecCache
            = new Dictionary<string, (DateTime expiresUtc, List<ImportListItemInfo> items)>();

        private readonly Action _persistSettingsCallback;

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
            NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.IMusicBrainzResolver mbidResolver = null,
            NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.IArtistMbidResolver artistResolver = null,
            Action persistSettingsCallback = null)
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
            _mbidResolver = mbidResolver ?? new NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.MusicBrainzResolver(logger);
            _artistResolver = artistResolver ?? new NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver(logger, httpClient: null, artistSearch: _artistSearchService);
            _reviewQueue = new ReviewQueueService(logger);
            _history = new RecommendationHistory(logger);
            _promptBuilder = new LibraryAwarePromptBuilder(logger);
            _metrics = new NzbDrone.Core.ImportLists.Brainarr.Performance.PerformanceMetrics(logger);
            _persistSettingsCallback = persistSettingsCallback;
            _sanitizer = new NzbDrone.Core.ImportLists.Brainarr.Services.RecommendationSanitizer(logger);
            _schemaValidator = new NzbDrone.Core.ImportLists.Brainarr.Services.Core.RecommendationSchemaValidator(logger);
        }

        // ====== CORE RECOMMENDATION WORKFLOW ======

        public async Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings)
        {
            // CRITICAL: Prevent concurrent executions which cause duplicates
            var operationKey = $"fetch_{settings.Provider}_{settings.GetHashCode()}";
            
            return await _duplicationPrevention.PreventConcurrentFetch(operationKey, async () =>
            {
                using var _corr = new CorrelationScope();
                using var _dbg = DebugFlags.PushFromSettings(settings);
                _logger.InfoWithCorrelation("Starting consolidated recommendation workflow");
                try
                {
                    var ip0 = settings.GetIterationProfile();
                    _logger.Info($"Backfill Plan => Strategy={settings.BackfillStrategy}, Enabled={ip0.EnableRefinement}, MaxIterations={ip0.MaxIterations}, ZeroStop={ip0.ZeroStop}, LowStop={ip0.LowStop}, GuaranteeExactTarget={ip0.GuaranteeExactTarget}");
                }
                catch { }
                if (settings.EnableDebugLogging)
                {
                    _logger.InfoWithCorrelation("[Brainarr Debug] Provider payload logging ENABLED for this run");
                }
                
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
                    // Local in-memory cache
                    List<ImportListItemInfo> cachedRecommendations;
                    lock (_recCacheLock)
                    {
                        if (_localRecCache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow < entry.expiresUtc)
                        {
                            cachedRecommendations = entry.items;
                        }
                        else
                        {
                            cachedRecommendations = null;
                        }
                    }

                    // External cache
                    if (cachedRecommendations == null && _cache.TryGet(cacheKey, out var extCached))
                    {
                        cachedRecommendations = extCached;
                        // Seed local cache to avoid re-scans when using mocks
                        lock (_recCacheLock)
                        {
                            _localRecCache[cacheKey] = (DateTime.UtcNow.Add(settings.CacheDuration), extCached);
                        }
                        try { _metrics.RecordCacheHit(cacheKey); } catch { }
                    }
                    else if (cachedRecommendations == null)
                    {
                        try { _metrics.RecordCacheMiss(cacheKey); } catch { }
                        try { NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.EventLogger.Log(_logger, NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.BrainarrEvent.CacheMiss, $"key={cacheKey}"); } catch { }
                    }

                    if (cachedRecommendations != null)
                    {
                        _logger.Debug("Retrieved recommendations from cache");
                        try { NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.EventLogger.Log(_logger, NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.BrainarrEvent.CacheHit, $"key={cacheKey}"); } catch { }
                        try
                        {
                            var snap = _metrics.GetSnapshot();
                            var providerName = _currentProvider?.ProviderName ?? settings.Provider.ToString();
                            _logger.InfoWithCorrelation($"Run summary: provider={providerName}, items={cachedRecommendations.Count}, cache=hit, cacheHitRate={snap.CacheHitRate:P0}");
                        }
                        catch { }
                        // Return cached results as-is (already validated on first generation)
                        return cachedRecommendations;
                    }

                    // Step 5: Generate new recommendations
                    var recommendations = await GenerateRecommendationsAsync(settings, libraryProfile);
                    // Deterministic sanitization + schema check
                    var sanitized = _sanitizer.SanitizeRecommendations(recommendations);
                    var schemaReport = _schemaValidator.Validate(sanitized);
                    try { NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.EventLogger.Log(_logger, NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.BrainarrEvent.SanitizationComplete, $"items={schemaReport.TotalItems} dropped={schemaReport.DroppedItems} clamped={schemaReport.ClampedConfidences} trimmed={schemaReport.TrimmedFields}"); } catch { }
                    recommendations = sanitized;
                    _history.RecordSuggestions(recommendations);
                    
                    // Step 6: Validate and filter recommendations
                    var allowArtistOnly = settings.RecommendationMode == RecommendationMode.Artists;
                    var validationSummary = await ValidateRecommendationsAsync(recommendations, allowArtistOnly, settings.EnableDebugLogging, settings.LogPerItemDecisions);
var validatedRecommendations = validationSummary.ValidRecommendations;
                    
                    // Step 6.5: Resolve MusicBrainz IDs and drop unresolvable items
                    List<Recommendation> enrichedRecommendations;
                    if (settings.RecommendationMode == RecommendationMode.Artists)
                    {
                        enrichedRecommendations = await _artistResolver.EnrichArtistsAsync(validatedRecommendations);
                    }
                    else
                    {
                        enrichedRecommendations = await _mbidResolver.EnrichWithMbidsAsync(validatedRecommendations);
                    }

                    // Step 6.6: Apply safety gates and optionally queue borderline items
                    var minConf = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
                    bool requireMbids = settings.RequireMbids;
                    bool queueBorderline = settings.QueueBorderlineItems;

                    var passNow = new List<Recommendation>();
                    var toQueue = new List<Recommendation>();
                    foreach (var r in enrichedRecommendations)
                    {
                        bool hasMbids = (settings.RecommendationMode == RecommendationMode.Artists)
                            ? !string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId)
                            : (!string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) && !string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId));
                        bool confOk = r.Confidence >= minConf;
                        if (confOk && (!requireMbids || hasMbids))
                        {
                            passNow.Add(r);
                        }
                        else
                        {
                            toQueue.Add(r);
                        }
                    }

                    if (toQueue.Count > 0 && queueBorderline)
                    {
                        _reviewQueue.Enqueue(toQueue, reason: "Safety gate (confidence/MBID)");
                        try { NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.EventLogger.Log(_logger, NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.BrainarrEvent.ReviewQueued, $"count={toQueue.Count}"); } catch { }
                        _logger.Info($"Queued {toQueue.Count} items for review (safety gates)");
                    }

                    // If artist-mode + MBID requirement produced no immediate passes, surface a helpful hint
                    if (settings.RecommendationMode == RecommendationMode.Artists && requireMbids && passNow.Count == 0)
                    {
                        _logger.Warn("Artist-mode MBID requirement filtered all items. Consider disabling 'Require MusicBrainz IDs' or ensure network access for MusicBrainz lookups.");
                        // Fail-open fallback: promote name-only artists to allow Lidarr-side MBID mapping
                        if (toQueue.Count > 0)
                        {
                            var targetCount = Math.Max(1, settings.MaxRecommendations);
                            var promoted = toQueue.Take(targetCount).ToList();
                            passNow.AddRange(promoted);
                            // Mark promoted items accepted in review queue to keep queues clean
                            foreach (var pr in promoted)
                            {
                                _reviewQueue.SetStatus(pr.Artist, pr.Album ?? string.Empty, ReviewQueueService.ReviewStatus.Accepted);
                            }
                            _logger.Warn($"Promoted {promoted.Count} artist(s) without MBIDs for downstream mapping");
                            _metrics.RecordArtistModePromotions(promoted.Count);
                        }
                    }

                    // Enforce mode normalization: in artist mode, strip albums to ensure artist-only pipeline
                    if (settings.RecommendationMode == RecommendationMode.Artists && passNow.Count > 0)
                    {
                        passNow = passNow
                            .Select(r => r with { Album = string.Empty })
                            .GroupBy(r => r.Artist, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.First())
                            .ToList();
                    }

                    // Include items that users already accepted from the review queue
                    var acceptedFromQueue = _reviewQueue.DequeueAccepted();
                    if (acceptedFromQueue.Count > 0)
                    {
                        passNow.AddRange(acceptedFromQueue);
                    }

                    // Apply approvals selected via settings (Approve Suggestions Tag field)
                    if (settings.ReviewApproveKeys != null)
                    {
                        int applied = 0;
                        foreach (var key in settings.ReviewApproveKeys)
                        {
                            var parts = (key ?? "").Split('|');
                            if (parts.Length >= 2)
                            {
                                var a = parts[0];
                                var b = parts[1];
                                if (_reviewQueue.SetStatus(a, b, ReviewQueueService.ReviewStatus.Accepted))
                                {
                                    applied++;
                                }
                            }
                        }
                        if (applied > 0)
                        {
                            var approvedNow = _reviewQueue.DequeueAccepted();
                            passNow.AddRange(approvedNow);
                            settings.ReviewApproveKeys = Array.Empty<string>();
                            TryPersistSettings();
                            _logger.Info($"Applied {applied} approvals from settings and cleared selections");
                        }
                    }
                    
                    // Step 7: Convert to import list items
                    var importItems = ConvertToImportListItems(passNow);

                    // Step 8: Filter out items already in the library (artist/album exists)
                    importItems = _libraryAnalyzer.FilterDuplicates(importItems);

                    // Step 9: Deduplicate within this batch
                    importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);
                    
                    // If we came up short, attempt an iterative top-up using feedback loops
                    var target = Math.Max(1, settings.MaxRecommendations);
                    var iterationProfile = settings.GetIterationProfile();
                    var refine = iterationProfile.EnableRefinement;
                    if (importItems.Count < target && refine)
                    {
                        var deficit = target - importItems.Count;
                        _logger.Info($"Under target by {deficit}; starting iterative top-up");

                    var topUp = await TopUpRecommendationsAsync(settings, libraryProfile, deficit, validationSummary);

                        if (topUp.Count > 0)
                        {
                            // Merge and deduplicate combined set
                            importItems.AddRange(topUp);
                            importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);
                            // Re-run library duplicate filter as a safety net
                            importItems = _libraryAnalyzer.FilterDuplicates(importItems);
                            _logger.Info($"Top-up added {topUp.Count} items; total now {importItems.Count}/{target}");
                            _logger.Debug($"Top-up summary: requested deficit={deficit}, obtained={topUp.Count}, provider={_currentProvider?.ProviderName}");
                        }
                        else
                        {
                            _logger.Warn("Iterative top-up returned no additional unique items");
                        }
                    }

                    // Step 10: Cache the results
                    _cache.Set(cacheKey, importItems, settings.CacheDuration);
                    lock (_recCacheLock)
                    {
                        _localRecCache[cacheKey] = (DateTime.UtcNow.Add(settings.CacheDuration), importItems);
                    }
                    
                    _logger.Info($"Generated {importItems.Count} validated recommendations");
                    try
                    {
                        _metrics.RecordRecommendationCount(importItems.Count);
                        var snap = _metrics.GetSnapshot();
                        var pm = _providerHealth.GetMetrics(_currentProvider?.ProviderName ?? settings.Provider.ToString());
                        _logger.InfoWithCorrelation($"Run summary: provider={_currentProvider?.ProviderName ?? settings.Provider.ToString()}, items={importItems.Count}, cache=miss, successRate={pm.SuccessRate:F1}%, avgMs={pm.AverageResponseTimeMs:F0}, cacheHitRate={snap.CacheHitRate:P0}");
                    }
                    catch { }
                    return importItems;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in consolidated recommendation workflow");
                    return new List<ImportListItemInfo>();
                }
            });
        }

        public async Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings, System.Threading.CancellationToken cancellationToken)
        {
            var operationKey = $"fetch_{settings.Provider}_{settings.GetHashCode()}";

            return await _duplicationPrevention.PreventConcurrentFetch(operationKey, async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    InitializeProvider(settings);
                    if (cancellationToken.IsCancellationRequested) return new List<ImportListItemInfo>();

                    try
                    {
                        var ip1 = settings.GetIterationProfile();
                        _logger.Info($"Backfill Plan => Strategy={settings.BackfillStrategy}, Enabled={ip1.EnableRefinement}, MaxIterations={ip1.MaxIterations}, ZeroStop={ip1.ZeroStop}, LowStop={ip1.LowStop}, GuaranteeExactTarget={ip1.GuaranteeExactTarget}");
                    }
                    catch { }

                    if (!IsValidProviderConfiguration(settings) || !IsProviderHealthy())
                    {
                        _logger.Warn("Provider is unhealthy, cannot generate recommendations");
                        return new List<ImportListItemInfo>();
                    }

                    var libraryProfile = await GetLibraryProfileAsync(settings);
                    if (cancellationToken.IsCancellationRequested) return new List<ImportListItemInfo>();

                    var cacheKey = GenerateCacheKey(settings, libraryProfile);
                    List<ImportListItemInfo> cachedRecommendations = null;
                    lock (_recCacheLock)
                    {
                        if (_localRecCache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow < entry.expiresUtc)
                        {
                            cachedRecommendations = entry.items;
                        }
                    }
                    if (cachedRecommendations == null && _cache.TryGet(cacheKey, out var extCached))
                    {
                        cachedRecommendations = extCached;
                        lock (_recCacheLock)
                        {
                            _localRecCache[cacheKey] = (DateTime.UtcNow.Add(settings.CacheDuration), extCached);
                        }
                        try { _metrics.RecordCacheHit(cacheKey); } catch { }
                    }
                    else if (cachedRecommendations == null)
                    {
                        try { _metrics.RecordCacheMiss(cacheKey); } catch { }
                    }
                    if (cachedRecommendations != null)
                    {
                        try
                        {
                            var snap = _metrics.GetSnapshot();
                            var providerName = _currentProvider?.ProviderName ?? settings.Provider.ToString();
                            _logger.InfoWithCorrelation($"Run summary: provider={providerName}, items={cachedRecommendations.Count}, cache=hit, cacheHitRate={snap.CacheHitRate:P0}");
                        }
                        catch { }
                        return cachedRecommendations;
                    }

                    var recommendations = await GenerateRecommendationsAsync(settings, libraryProfile, cancellationToken);
                    _history.RecordSuggestions(recommendations);
                    if (cancellationToken.IsCancellationRequested) return new List<ImportListItemInfo>();

                    var allowArtistOnly = settings.RecommendationMode == RecommendationMode.Artists;
                    var validationSummary = await ValidateRecommendationsAsync(recommendations, allowArtistOnly, settings.EnableDebugLogging, settings.LogPerItemDecisions);
var validatedRecommendations = validationSummary.ValidRecommendations;
                    if (cancellationToken.IsCancellationRequested) return new List<ImportListItemInfo>();

                    List<Recommendation> enrichedRecommendations;
                    if (settings.RecommendationMode == RecommendationMode.Artists)
                    {
                        var artistResolver = new ArtistMbidResolver(_logger);
                        enrichedRecommendations = await artistResolver.EnrichArtistsAsync(validatedRecommendations, cancellationToken);
                    }
                    else
                    {
                        enrichedRecommendations = await _mbidResolver.EnrichWithMbidsAsync(validatedRecommendations, cancellationToken);
                    }

                    var minConf = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
                    bool requireMbids = settings.RequireMbids;
                    bool queueBorderline = settings.QueueBorderlineItems;
                    var recommendArtists = settings.RecommendationMode == RecommendationMode.Artists;

                    var passNow = new List<Recommendation>();
                    var toQueue = new List<Recommendation>();
                    foreach (var r in enrichedRecommendations)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        bool hasMbids = recommendArtists
                            ? !string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId)
                            : (!string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) && !string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId));
                        bool confOk = r.Confidence >= minConf;
                        if (confOk && (!requireMbids || hasMbids)) passNow.Add(r); else toQueue.Add(r);
                    }

                    if (toQueue.Count > 0 && queueBorderline)
                    {
                        _reviewQueue.Enqueue(toQueue, reason: "Safety gate (confidence/MBID)");
                    }

                    // If artist-mode + MBID requirement produced no immediate passes, surface a helpful hint
                    if (recommendArtists && requireMbids && passNow.Count == 0)
                    {
                        _logger.Warn("Artist-mode MBID requirement filtered all items. Consider disabling 'Require MusicBrainz IDs' or ensure network access for MusicBrainz lookups.");
                        if (toQueue.Count > 0)
                        {
                            var targetCount = Math.Max(1, settings.MaxRecommendations);
                            var promoted = toQueue.Take(targetCount).ToList();
                            passNow.AddRange(promoted);
                            foreach (var pr in promoted)
                            {
                                _reviewQueue.SetStatus(pr.Artist, pr.Album ?? string.Empty, ReviewQueueService.ReviewStatus.Accepted);
                            }
                            _logger.Warn($"Promoted {promoted.Count} artist(s) without MBIDs for downstream mapping");
                            _metrics.RecordArtistModePromotions(promoted.Count);
                        }
                    }

                    var acceptedFromQueue = _reviewQueue.DequeueAccepted();
                    if (acceptedFromQueue.Count > 0) passNow.AddRange(acceptedFromQueue);

                    if (settings.ReviewApproveKeys != null)
                    {
                        int applied = 0;
                        foreach (var key in settings.ReviewApproveKeys)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            var parts = (key ?? "").Split('|');
                            if (parts.Length >= 2)
                            {
                                if (_reviewQueue.SetStatus(parts[0], parts[1], ReviewQueueService.ReviewStatus.Accepted)) applied++;
                            }
                        }
                        if (applied > 0)
                        {
                            var approvedNow = _reviewQueue.DequeueAccepted();
                            passNow.AddRange(approvedNow);
                        }
                    }

                    var importItems = ConvertToImportListItems(passNow);
                    importItems = _libraryAnalyzer.FilterDuplicates(importItems);
                    importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);

                    // Optional: skip iterative top-up if cancellation requested
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        var target = Math.Max(1, settings.MaxRecommendations);
                        var refine = settings.GetIterationProfile().EnableRefinement;
                        if (importItems.Count < target && refine)
                        {
                            var deficit = target - importItems.Count;
                            var topUp = await TopUpRecommendationsAsync(settings, libraryProfile, deficit, validationSummary);
                            if (topUp.Count > 0)
                            {
                                importItems.AddRange(topUp);
                                importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);
                                importItems = _libraryAnalyzer.FilterDuplicates(importItems);
                            }
                        }
                    }

                    _cache.Set(cacheKey, importItems, settings.CacheDuration);
                    lock (_recCacheLock)
                    {
                        _localRecCache[cacheKey] = (DateTime.UtcNow.Add(settings.CacheDuration), importItems);
                    }

                    try
                    {
                        _metrics.RecordRecommendationCount(importItems.Count);
                        var snap = _metrics.GetSnapshot();
                        var pm = _providerHealth.GetMetrics(_currentProvider?.ProviderName ?? settings.Provider.ToString());
                        _logger.InfoWithCorrelation($"Run summary: provider={_currentProvider?.ProviderName ?? settings.Provider.ToString()}, items={importItems.Count}, cache=miss, successRate={pm.SuccessRate:F1}%, avgMs={pm.AverageResponseTimeMs:F0}, cacheHitRate={snap.CacheHitRate:P0}");
                    }
                    catch { }
                    return importItems;
                }
                catch (OperationCanceledException)
                {
                    _logger.Info("Recommendation fetch was cancelled");
                    return new List<ImportListItemInfo>();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in cancellation-aware workflow");
                    return new List<ImportListItemInfo>();
                }
            });
        }

        private async Task<List<Recommendation>> GenerateRecommendationsAsync(BrainarrSettings settings, LibraryProfile libraryProfile, System.Threading.CancellationToken cancellationToken)
        {
            if (_currentProvider == null) throw new InvalidOperationException("Provider not initialized");

            var artistMode = settings.RecommendationMode == RecommendationMode.Artists;
            // Prefer the library-aware prompt with sampled context for both initial and iterative calls
            var allArtistsForPrompt = _libraryAnalyzer.GetAllArtists();
            var allAlbumsForPrompt = _libraryAnalyzer.GetAllAlbums();

            // Initial oversampling: request more than target on the first call, then trim/dedupe before top-up
            var originalTarget = settings.MaxRecommendations;
            try
            {
                int initialRequest = originalTarget;
                var ip = settings.GetIterationProfile();
                // Only oversample if backfill is enabled (Standard/Aggressive)
                if (ip.EnableRefinement)
                {
                    double factor = settings.BackfillStrategy switch
                    {
                        BackfillStrategy.Aggressive => (settings.SamplingStrategy == SamplingStrategy.Comprehensive ? 2.0 : 1.75),
                        BackfillStrategy.Standard => (settings.SamplingStrategy == SamplingStrategy.Comprehensive ? 1.75 : 1.5),
                        _ => 1.0
                    };
                    var cap = (settings.SamplingStrategy == SamplingStrategy.Comprehensive)
                        ? ((settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio) ? 150 : 120)
                        : ((settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio) ? 100 : 80);
                    initialRequest = Math.Min(cap, Math.Max(originalTarget, (int)Math.Ceiling(originalTarget * factor)));
                }

                // Temporarily bump target for the initial prompt only
                settings.MaxRecommendations = initialRequest;

                var promptRes = _promptBuilder.BuildLibraryAwarePromptWithMetrics(libraryProfile, allArtistsForPrompt, allAlbumsForPrompt, settings, artistMode);
                var prompt = promptRes.Prompt;

                // Emit detailed request info when import list debug logging is enabled
                if (settings.EnableDebugLogging)
                {
                    try
                    {
                        var modelLabel = settings.ModelSelection;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Model request => Provider={settings.Provider}, Model={modelLabel}, Mode={settings.RecommendationMode}, Sampling={settings.SamplingStrategy}, Discovery={settings.DiscoveryMode}, MaxRecs={settings.MaxRecommendations}");
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Prompt ({prompt?.Length ?? 0} chars):\n{prompt}");
                    }
                    catch { /* logging must not interfere with execution */ }
                }

                var localProvider = settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio;
                var requestedTimeout = settings.AIRequestTimeoutSeconds;
                var effectiveTimeout = (localProvider && requestedTimeout <= BrainarrConstants.DefaultAITimeout)
                    ? 360
                    : requestedTimeout;
                if (settings.EnableDebugLogging)
                {
                    _logger.InfoWithCorrelation($"[Brainarr Debug] Effective timeout: {effectiveTimeout}s");
                }
                using var _timeout = TimeoutContext.Push(effectiveTimeout);
                var sw = Stopwatch.StartNew();
                var recsResult = await ResiliencePolicy.RunWithRetriesAsync<List<Recommendation>>(
                    async ct => await _currentProvider.GetRecommendationsAsync(prompt, ct),
                    _logger,
                    "Provider.GetRecommendations",
                    maxAttempts: 3,
                    initialDelay: TimeSpan.FromMilliseconds(250),
                    cancellationToken: cancellationToken);
                sw.Stop();

                var providerName = _currentProvider.ProviderName;
                if (recsResult != null && recsResult.Count > 0)
                {
                    _providerHealth.RecordSuccess(providerName, sw.Elapsed.TotalMilliseconds);
                    try { _metrics.RecordProviderResponseTime(providerName, sw.Elapsed); } catch { }
                    LogProviderScoreboard(providerName);
                    // If we oversampled, we still return full set here; caller will validate, trim to target and consider top-up.
                    return recsResult;
                }
                else
                {
                    _providerHealth.RecordFailure(providerName, "Empty recommendation result");
                    try { _metrics.RecordProviderResponseTime(providerName, sw.Elapsed); } catch { }
                    LogProviderScoreboard(providerName);
                    return new List<Recommendation>();
                }
            }
            finally
            {
                // Ensure we always restore the user's target even if request fails
                try { settings.MaxRecommendations = originalTarget; } catch { }
            }
        }

        private void LogProviderScoreboard(string providerName)
        {
            try
            {
                var m = _providerHealth.GetMetrics(providerName);
                _logger.InfoWithCorrelation($"[Scoreboard] {providerName} — success {m.SuccessRate:F1}% | avg {m.AverageResponseTimeMs:F0}ms | failures {m.FailedRequests}/{m.TotalRequests}");
            }
            catch { }
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
                try { NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.EventLogger.Log(_logger, NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.BrainarrEvent.ProviderSelected, $"provider={_currentProviderType} model={settings.EffectiveModel}"); } catch { }
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

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var testResult = await _currentProvider.TestConnectionAsync();
                sw.Stop();
                if (testResult)
                {
                    _providerHealth.RecordSuccess(_currentProvider.ProviderName, sw.Elapsed.TotalMilliseconds);
                }
                else
                {
                    _providerHealth.RecordFailure(_currentProvider.ProviderName, "Connection test failed");
                }
                _logger.Debug($"Provider connection test result: {testResult}");
                return testResult;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Provider connection test failed");
                if (_currentProvider != null)
                {
                    _providerHealth.RecordFailure(_currentProvider.ProviderName, ex.Message);
                }
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
                    // Allow provider/baseUrl override via query during unsaved UI changes
                    "getmodeloptions" => SafeAsyncHelper.RunSafeSync(() => GetModelOptionsAsync(settings, query)),
                    "detectmodels" => SafeAsyncHelper.RunSafeSync(() => DetectModelsAsync(settings, query)),
                    "testconnection" => SafeAsyncHelper.RunSafeSync(() => TestProviderConnectionAsync(settings)),
                    "getproviderstatus" => GetProviderStatus(),
                    // Review Queue actions
                    "review/getqueue" => new { items = _reviewQueue.GetPending() },
                    "review/accept" => HandleReviewUpdate(query, Services.Support.ReviewQueueService.ReviewStatus.Accepted),
                    "review/reject" => HandleReviewUpdate(query, Services.Support.ReviewQueueService.ReviewStatus.Rejected),
                    "review/never"  => HandleReviewNever(query),
                    "review/apply"  => ApplyApprovalsNow(settings, query),
                    "review/clear"  => ClearApprovalSelections(settings),
                    "review/rejectselected" => RejectOrNeverSelected(settings, query, Services.Support.ReviewQueueService.ReviewStatus.Rejected),
                    "review/neverselected"  => RejectOrNeverSelected(settings, query, Services.Support.ReviewQueueService.ReviewStatus.Never),
                    // Metrics snapshot (lightweight)
                    "metrics/get" => GetMetricsSnapshot(),
                    // Options for Approve Suggestions Select field
                    "review/getoptions" => GetReviewOptions(),
                    // Read-only Review Summary options
                    "review/getsummaryoptions" => GetReviewSummaryOptions(),
                    _ => throw new NotSupportedException($"Action '{action}' is not supported")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling action: {action}");
                return new { error = ex.Message };
            }
        }

        private object HandleReviewUpdate(IDictionary<string,string> query, Services.Support.ReviewQueueService.ReviewStatus status)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            var notes = query.TryGetValue("notes", out var n) ? n : null;
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album))
            {
                return new { ok = false, error = "artist and album are required" };
            }

            var ok = _reviewQueue.SetStatus(artist, album, status, notes);
            if (ok && status == Services.Support.ReviewQueueService.ReviewStatus.Rejected)
            {
                // Record rejection in history (soft negative feedback)
                _history.MarkAsRejected(artist, album, reason: notes);
            }
            return new { ok };
        }

        private object HandleReviewNever(IDictionary<string,string> query)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            var notes = query.TryGetValue("notes", out var n) ? n : null;
            if (string.IsNullOrWhiteSpace(artist))
            {
                return new { ok = false, error = "artist is required" };
            }
            var ok = _reviewQueue.SetStatus(artist, album, Services.Support.ReviewQueueService.ReviewStatus.Never, notes);
            // Strong negative constraint
            _history.MarkAsDisliked(artist, album, RecommendationHistory.DislikeLevel.NeverAgain);
            return new { ok };
        }

        private object GetMetricsSnapshot()
        {
            var counts = _reviewQueue.GetCounts();
            var perf = _metrics.GetSnapshot();
            return new
            {
                review = new { pending = counts.pending, accepted = counts.accepted, rejected = counts.rejected, never = counts.never },
                cache = new { },
                provider = GetProviderStatus(),
                artistPromotion = new { events = perf.ArtistModeGatingEvents, promoted = perf.ArtistModePromotedRecommendations }
            };
        }

        private object GetReviewOptions()
        {
            var items = _reviewQueue.GetPending();
            var options = items
                .Select(i => new
                {
                    value = $"{i.Artist}|{i.Album}",
                    name = string.IsNullOrWhiteSpace(i.Album)
                        ? i.Artist
                        : $"{i.Artist} — {i.Album}{(i.Year.HasValue ? " (" + i.Year.Value + ")" : string.Empty)}"
                })
                .OrderBy(o => o.name, StringComparer.InvariantCultureIgnoreCase)
                .ToList();
            return new { options };
        }

        private object GetReviewSummaryOptions()
        {
            var (pending, accepted, rejected, never) = _reviewQueue.GetCounts();
            var options = new List<object>
            {
                new { value = $"pending:{pending}", name = $"Pending: {pending}" },
                new { value = $"accepted:{accepted}", name = $"Accepted (released): {accepted}" },
                new { value = $"rejected:{rejected}", name = $"Rejected: {rejected}" },
                new { value = $"never:{never}", name = $"Never Again: {never}" }
            };
            return new { options };
        }

        private object ApplyApprovalsNow(BrainarrSettings settings, IDictionary<string,string> query)
        {
            var keysCsv = query.TryGetValue("keys", out var k) ? k : null;
            var keys = new List<string>();
            if (!string.IsNullOrWhiteSpace(keysCsv))
            {
                keys.AddRange(keysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (settings.ReviewApproveKeys != null)
            {
                keys.AddRange(settings.ReviewApproveKeys);
            }

            int applied = 0;
            foreach (var key in keys)
            {
                var parts = (key ?? "").Split('|');
                if (parts.Length >= 2)
                {
                    if (_reviewQueue.SetStatus(parts[0], parts[1], ReviewQueueService.ReviewStatus.Accepted))
                    {
                        applied++;
                    }
                }
            }

            var accepted = _reviewQueue.DequeueAccepted();
            // Clear approval selections in memory (persist by saving settings from UI)
            settings.ReviewApproveKeys = Array.Empty<string>();

            // Attempt to persist the cleared selections, if a persistence callback was provided
            TryPersistSettings();

            return new
            {
                ok = true,
                approved = applied,
                released = accepted.Count,
                cleared = true,
                note = "Selections cleared in memory; click Save to persist clearing in settings"
            };
        }

        private object ClearApprovalSelections(BrainarrSettings settings)
        {
            settings.ReviewApproveKeys = Array.Empty<string>();
            TryPersistSettings();
            return new { ok = true, cleared = true, note = "Selections cleared and persisted (if supported)." };
        }

        private object RejectOrNeverSelected(BrainarrSettings settings, IDictionary<string,string> query, ReviewQueueService.ReviewStatus status)
        {
            var keysCsv = query.TryGetValue("keys", out var k) ? k : null;
            var keys = new List<string>();
            if (!string.IsNullOrWhiteSpace(keysCsv))
            {
                keys.AddRange(keysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (settings.ReviewApproveKeys != null)
            {
                keys.AddRange(settings.ReviewApproveKeys);
            }

            int applied = 0;
            foreach (var key in keys)
            {
                var parts = (key ?? "").Split('|');
                if (parts.Length >= 2)
                {
                    if (_reviewQueue.SetStatus(parts[0], parts[1], status))
                    {
                        applied++;
                        if (status == ReviewQueueService.ReviewStatus.Rejected)
                        {
                            _history.MarkAsRejected(parts[0], parts[1], reason: "Batch reject");
                        }
                        else if (status == ReviewQueueService.ReviewStatus.Never)
                        {
                            _history.MarkAsDisliked(parts[0], parts[1], RecommendationHistory.DislikeLevel.NeverAgain);
                        }
                    }
                }
            }

            // For symmetry, clear selection in memory after action
            settings.ReviewApproveKeys = Array.Empty<string>();
            TryPersistSettings();
            return new { ok = true, updated = applied, cleared = true, note = "Selections cleared and persisted (if supported)." };
        }

        private void TryPersistSettings()
        {
            try
            {
                _persistSettingsCallback?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to persist Brainarr settings automatically");
            }
        }

        // ====== LIBRARY ANALYSIS ======

        public async Task<LibraryProfile> GetLibraryProfileAsync(BrainarrSettings settings)
        {
            var profileCacheKey = $"library_profile_{settings.SamplingStrategy}";

            // Use an internal in-memory cache for the computed profile to avoid re-scanning when returning cached recommendations
            lock (_profileCacheLock)
            {
                if (_cachedLibraryProfile != null &&
                    _cachedLibraryProfileKey == profileCacheKey &&
                    (DateTime.UtcNow - _cachedLibraryProfileAt) < LibraryProfileTtl)
                {
                    _logger.Debug("Using cached library profile");
                    return _cachedLibraryProfile;
                }
            }

            _logger.Debug("Generating new library profile");
            var profile = _libraryAnalyzer.AnalyzeLibrary();

            lock (_profileCacheLock)
            {
                _cachedLibraryProfile = profile;
                _cachedLibraryProfileKey = profileCacheKey;
                _cachedLibraryProfileAt = DateTime.UtcNow;
            }

            return profile;
        }

        // ====== PRIVATE HELPER METHODS ======

        private async Task<List<Recommendation>> GenerateRecommendationsAsync(BrainarrSettings settings, LibraryProfile libraryProfile)
        {
            if (_currentProvider == null)
                throw new InvalidOperationException("Provider not initialized");

            // Propagate debug flag and timeout for provider payload logging and timing
            using var _dbgLocal = DebugFlags.PushFromSettings(settings);
            var localProvider2 = settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio;
            var reqTimeout2 = settings.AIRequestTimeoutSeconds;
            var effTimeout2 = (localProvider2 && reqTimeout2 <= BrainarrConstants.DefaultAITimeout) ? 360 : reqTimeout2;
            if (settings.EnableDebugLogging)
            {
                _logger.InfoWithCorrelation($"[Brainarr Debug] Effective timeout: {effTimeout2}s");
            }
            using var _timeoutLocal = TimeoutContext.Push(effTimeout2);

            var artistMode = settings.RecommendationMode == RecommendationMode.Artists;
            var allArtistsForPrompt = _libraryAnalyzer.GetAllArtists();
            var allAlbumsForPrompt = _libraryAnalyzer.GetAllAlbums();

            // Initial oversampling for first pass (no cancellation token variant)
            var originalTarget = settings.MaxRecommendations;
            try
            {
                var ip = settings.GetIterationProfile();
                if (ip.EnableRefinement)
                {
                    double factor = settings.BackfillStrategy switch
                    {
                        BackfillStrategy.Aggressive => (settings.SamplingStrategy == SamplingStrategy.Comprehensive ? 2.0 : 1.75),
                        BackfillStrategy.Standard => (settings.SamplingStrategy == SamplingStrategy.Comprehensive ? 1.75 : 1.5),
                        _ => 1.0
                    };
                    var cap = (settings.SamplingStrategy == SamplingStrategy.Comprehensive)
                        ? ((settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio) ? 150 : 120)
                        : ((settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio) ? 100 : 80);
                    var initialRequest = Math.Min(cap, Math.Max(originalTarget, (int)Math.Ceiling(originalTarget * factor)));
                    settings.MaxRecommendations = initialRequest;
                }
            }
            catch { }

            var promptRes = _promptBuilder.BuildLibraryAwarePromptWithMetrics(libraryProfile, allArtistsForPrompt, allAlbumsForPrompt, settings, artistMode);
                var prompt = promptRes.Prompt;

            if (settings.EnableDebugLogging)
            {
                try
                {
                    var modelLabel = settings.ModelSelection;
                    _logger.Info($"[Brainarr Debug] Model request => Provider={settings.Provider}, Model={modelLabel}, Mode={settings.RecommendationMode}, Sampling={settings.SamplingStrategy}, Discovery={settings.DiscoveryMode}, MaxRecs={settings.MaxRecommendations}");
                    _logger.Info($"[Brainarr Debug] Prompt ({prompt?.Length ?? 0} chars):\n{prompt}");
                }
                catch { }
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await ResiliencePolicy.RunWithRetriesAsync<List<Recommendation>>(
                async _ => await _currentProvider.GetRecommendationsAsync(prompt),
                _logger,
                "Provider.GetRecommendations",
                maxAttempts: 3,
                initialDelay: TimeSpan.FromMilliseconds(250),
                cancellationToken: System.Threading.CancellationToken.None);
            sw.Stop();

            // Restore original target for subsequent logic
            try { settings.MaxRecommendations = originalTarget; } catch { }

            var providerName = _currentProvider.ProviderName;
            // Emit token budget info to aid tuning
            if (settings.EnableDebugLogging)
            {
                try
                {
                    var limit = _promptBuilder.GetEffectiveTokenLimit(settings.SamplingStrategy, settings.Provider);
                    var est = _promptBuilder.EstimateTokens(prompt);
                    _logger.Info($"[Brainarr Debug] Tokens => Strategy={settings.SamplingStrategy}, Provider={settings.Provider}, Limit≈{limit}, EstimatedUsed≈{promptRes.EstimatedTokens}, Sampled: {promptRes.SampledArtists} artists, {promptRes.SampledAlbums} albums");
                }
                catch { }
            }
            if (result != null && result.Count > 0)
            {
                _providerHealth.RecordSuccess(providerName, sw.Elapsed.TotalMilliseconds);
                try { _metrics.RecordProviderResponseTime(providerName, sw.Elapsed); } catch { }
                return result;
            }
            else
            {
                _providerHealth.RecordFailure(providerName, "Empty recommendation result");
                return new List<Recommendation>();
            }
        }

        private async Task<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult> ValidateRecommendationsAsync(List<Recommendation> recommendations, bool allowArtistOnly, bool debug = false, bool logPerItem = true)
        {
            _logger.Debug($"Validating {recommendations.Count} recommendations");
            
            var validationResult = _validator.ValidateBatch(recommendations, allowArtistOnly);
            
            _logger.Debug($"Validation result: {validationResult.ValidCount}/{validationResult.TotalCount} passed ({validationResult.PassRate:F1}%)");
            
            try
            {
                if (logPerItem)
                {
                foreach (var r in validationResult.FilteredRecommendations)
                {
                    var name = string.IsNullOrWhiteSpace(r.Album) ? r.Artist : $"{r.Artist} - {r.Album}";
                    string reason;
                    if (!validationResult.FilterDetails.TryGetValue(name, out reason))
                    {
                        reason = "filtered";
                    }
                    _logger.InfoWithCorrelation($"[Brainarr Debug] Rejected: {name} (conf={r.Confidence:F2}) because {reason}");
                }
                // Accepted items are logged only when debug is enabled to reduce noise
                if (debug)
                {
                    foreach (var r in validationResult.ValidRecommendations)
                    {
                        var name = string.IsNullOrWhiteSpace(r.Album) ? r.Artist : $"{r.Artist} - {r.Album}";
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Accepted: {name} (conf={r.Confidence:F2})");
                    }
                }
                }
            }
            catch { }
            
            return validationResult;
        }

        private List<ImportListItemInfo> ConvertToImportListItems(List<Recommendation> recommendations)
        {
            // Convert model recommendations to ImportList items; global deduplication occurs later
            return recommendations
                .Select(r => new ImportListItemInfo
                {
                    Artist = r.Artist,
                    Album = r.Album,
                    ArtistMusicBrainzId = string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) ? null : r.ArtistMusicBrainzId,
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

            // Include model and sanitizer versions for safe invalidation when behavior changes
            var effectiveModel = settings.EffectiveModel ?? settings.ModelSelection ?? string.Empty;
            var raw = string.Join("|", new[]
            {
                $"cache_v={Configuration.BrainarrConstants.CacheKeyVersion}",
                $"san_v={Configuration.BrainarrConstants.SanitizerVersion}",
                $"provider={settings.Provider}",
                $"mode={settings.DiscoveryMode}",
                $"recmode={settings.RecommendationMode}",
                $"model={effectiveModel}",
                $"max={settings.MaxRecommendations}",
                $"genres={topGenres}",
                $"artists={topArtists}"
            });

            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
            var hash = Convert.ToBase64String(sha.ComputeHash(bytes))
                .Replace("/", "_")
                .Replace("+", "-");
            return $"rec_{hash.Substring(0, 24)}";
        }

        private async Task<List<ImportListItemInfo>> TopUpRecommendationsAsync(BrainarrSettings settings, LibraryProfile libraryProfile, int needed, NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult? initialValidation)
        {
            try
            {
                if (_currentProvider == null)
                {
                    _logger.Warn("Top-up requested without an active provider");
                    return new List<ImportListItemInfo>();
                }

                // Use the iterative strategy to request additional unique items
                var strategy = new IterativeRecommendationStrategy(_logger, _promptBuilder);

                // Temporarily adjust target count for the top-up
                var originalMax = settings.MaxRecommendations;
                settings.MaxRecommendations = Math.Max(1, needed);

                try
                {
                    var shouldRecommendArtists = settings.RecommendationMode == RecommendationMode.Artists;

                    // Supply real library context so the iterative strategy can avoid existing items
                    var allArtists = _libraryAnalyzer.GetAllArtists();
                    var allAlbums = _libraryAnalyzer.GetAllAlbums();

                    var topUpRecs = await strategy.GetIterativeRecommendationsAsync(
                        _currentProvider,
                        libraryProfile,
                        allArtists,
                        allAlbums,
                        settings,
                        shouldRecommendArtists, initialValidation?.FilterReasons, initialValidation?.FilteredRecommendations,
                        aggressiveGuarantee: settings.GetIterationProfile().GuaranteeExactTarget);

                    if (topUpRecs == null || topUpRecs.Count == 0)
                    {
                        return new List<ImportListItemInfo>();
                    }

                    // Validate and enrich MBIDs for the top-up set
                    var validated = await ValidateRecommendationsAsync(topUpRecs, settings.RecommendationMode == RecommendationMode.Artists, settings.EnableDebugLogging, settings.LogPerItemDecisions);
                    List<Recommendation> enriched;
                    if (settings.RecommendationMode == RecommendationMode.Artists)
                    {
                        enriched = await _artistResolver.EnrichArtistsAsync(validated.ValidRecommendations);
                    }
                    else
                    {
                        enriched = await _mbidResolver.EnrichWithMbidsAsync(validated.ValidRecommendations);
                    }

                    // Apply safety gates
                    var minConf = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
                    bool requireMbids = settings.RequireMbids;
                    var recommendArtists = settings.RecommendationMode == RecommendationMode.Artists;
                    var passNow = new List<Recommendation>();
                    foreach (var r in enriched)
                    {
                        bool hasMbids = recommendArtists
                            ? !string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId)
                            : (!string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) && !string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId));
                        bool confOk = r.Confidence >= minConf;
                        if (confOk && (!requireMbids || hasMbids))
                        {
                            passNow.Add(r);
                        }
                        else if (settings.QueueBorderlineItems)
                        {
                            _reviewQueue.Enqueue(new List<Recommendation> { r }, reason: "Safety gate (top-up)");
                        }
                    }

                    // Fallback promotion to avoid empty results when artist-mode + MBID gate filters all
                    if (recommendArtists && requireMbids && passNow.Count == 0)
                    {
                        _logger.Warn("Artist-mode MBID requirement filtered all top-up items; promoting name-only artists for downstream mapping");
                        var targetCount = Math.Max(1, settings.MaxRecommendations);
                        var promoted = enriched.Where(e => !string.IsNullOrWhiteSpace(e.Artist)).Take(targetCount).ToList();
                        passNow.AddRange(promoted);
                        foreach (var pr in promoted)
                        {
                            _reviewQueue.SetStatus(pr.Artist, pr.Album ?? string.Empty, ReviewQueueService.ReviewStatus.Accepted);
                        }
                        _metrics.RecordArtistModePromotions(promoted.Count);
                    }

                    // Mode normalization: in artist mode, ensure artist-only entries
                    if (settings.RecommendationMode == RecommendationMode.Artists && passNow.Count > 0)
                    {
                        passNow = passNow
                            .Select(r => r with { Album = string.Empty })
                            .GroupBy(r => r.Artist, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.First())
                            .ToList();
                    }

                    // Convert and remove duplicates
                    var importItems = ConvertToImportListItems(passNow);
                    importItems = _libraryAnalyzer.FilterDuplicates(importItems);
                    importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);

                    // If aggressively guaranteeing exact target in Artist mode with MBID requirement,
                    // and we're still short, promote additional name-only artists to fill the gap.
                    if (settings.GetIterationProfile().GuaranteeExactTarget && recommendArtists && requireMbids)
                    {
                        var targetCount = Math.Max(1, settings.MaxRecommendations);
                        if (importItems.Count < targetCount)
                        {
                            var deficit = targetCount - importItems.Count;
                            var fallback = enriched
                                .Where(e => !string.IsNullOrWhiteSpace(e.Artist))
                                .Where(e => string.IsNullOrWhiteSpace(e.ArtistMusicBrainzId))
                                .Take(deficit)
                                .ToList();

                            if (fallback.Count > 0)
                            {
                                _logger.Warn($"GuaranteeExactTarget: promoting {fallback.Count} artist(s) without MBIDs to meet target");
                                var add = ConvertToImportListItems(fallback.Select(f => f with { Album = string.Empty }).ToList());
                                importItems.AddRange(add);
                                importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);
                                importItems = _libraryAnalyzer.FilterDuplicates(importItems);
                            }
                        }
                    }

                    return importItems;
                }
                finally
                {
                    // Restore original target
                    settings.MaxRecommendations = originalMax;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Top-up recommendations failed");
                return new List<ImportListItemInfo>();
            }
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

        private async Task<object> GetModelOptionsAsync(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var effectiveProvider = settings.Provider;
            if (query != null && query.TryGetValue("provider", out var p) && Enum.TryParse<AIProvider>(p, out var parsed))
            {
                effectiveProvider = parsed;
            }

            // Allow overriding baseUrl before save
            var ollamaUrl = settings.OllamaUrl;
            var lmUrl = settings.LMStudioUrl;
            if (query != null && query.TryGetValue("baseUrl", out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
            {
                if (effectiveProvider == AIProvider.Ollama) ollamaUrl = baseUrl;
                if (effectiveProvider == AIProvider.LMStudio) lmUrl = baseUrl;
            }

            if (effectiveProvider == AIProvider.Ollama)
            {
                var models = await _modelDetection.GetOllamaModelsAsync(ollamaUrl);
                if (models != null && models.Any())
                {
                    return new
                    {
                        options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList()
                    };
                }
                return GetFallbackOptions(AIProvider.Ollama);
            }
            else if (effectiveProvider == AIProvider.LMStudio)
            {
                var models = await _modelDetection.GetLMStudioModelsAsync(lmUrl);
                if (models != null && models.Any())
                {
                    return new
                    {
                        options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList()
                    };
                }
                return GetFallbackOptions(AIProvider.LMStudio);
            }

            return GetStaticModelOptions(effectiveProvider);
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

        private async Task<object> DetectModelsAsync(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var effectiveProvider = settings.Provider;
            if (query != null && query.TryGetValue("provider", out var p) && Enum.TryParse<AIProvider>(p, out var parsed))
            {
                effectiveProvider = parsed;
            }

            var ollamaUrl = settings.OllamaUrl;
            var lmUrl = settings.LMStudioUrl;
            if (query != null && query.TryGetValue("baseUrl", out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
            {
                if (effectiveProvider == AIProvider.Ollama) ollamaUrl = baseUrl;
                if (effectiveProvider == AIProvider.LMStudio) lmUrl = baseUrl;
            }

            if (effectiveProvider == AIProvider.Ollama)
            {
                var models = await _modelDetection.GetOllamaModelsAsync(ollamaUrl);
                return new { options = models.Select(m => new { value = m, name = FormatModelName(m) }).ToList() };
            }
            else if (effectiveProvider == AIProvider.LMStudio)
            {
                var models = await _modelDetection.GetLMStudioModelsAsync(lmUrl);
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
            return NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatEnumName(enumValue);
        }

        // Optional: allow host to attach Lidarr's artist search for stronger MBID mapping
        public void AttachArtistSearchService(NzbDrone.Core.MetadataSource.ISearchForNewArtist search)
        {
            _artistSearchService = search;
            // Recreate resolver if using default implementation to take advantage of search service
            if (_artistResolver is NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver)
            {
                _artistResolver = new NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver(_logger, httpClient: null, artistSearch: _artistSearchService);
                _logger.Info("Attached Lidarr artist search to MBID resolver");
            }
        }

        private static string FormatModelName(string modelId)
        {
            return NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatModelName(modelId);
        }

        private static string CleanModelName(string name)
        {
            return NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.CleanModelName(name);
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
