using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
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
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;

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
        private readonly ModelOptionsProvider _modelOptionsProvider;
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
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Core.IProviderInvoker _providerInvoker;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Core.ISafetyGateService _safetyGates;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Core.ITopUpPlanner _topUpPlanner;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Core.IRecommendationPipeline _pipeline;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Core.IRecommendationCoordinator _coordinator;
        private readonly IStyleCatalogService _styleCatalog;
        private readonly IObservabilityService _observability;
        private readonly ReviewQueueActionHandler _reviewQueueHandler;
        private readonly ProviderLifecycleService _providerLifecycle;
        private readonly ConfigurationValidator _configValidator;
        private readonly RecommendationGenerator _recommendationGenerator;

        private readonly IBreakerRegistry _breakerRegistry;

        // Library profile caching now handled by RecommendationCoordinator

        // Recommendation caching is handled by IRecommendationCache exclusively

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
            Action persistSettingsCallback = null,
            // Optional DI for better testability and layering
            NzbDrone.Core.ImportLists.Brainarr.Services.IRecommendationSanitizer sanitizer = null,
            NzbDrone.Core.ImportLists.Brainarr.Services.Core.IRecommendationSchemaValidator schemaValidator = null,
            NzbDrone.Core.ImportLists.Brainarr.Services.Core.IProviderInvoker providerInvoker = null,
            NzbDrone.Core.ImportLists.Brainarr.Services.Core.ISafetyGateService safetyGates = null,
            NzbDrone.Core.ImportLists.Brainarr.Services.Core.ITopUpPlanner topUpPlanner = null,
            NzbDrone.Core.ImportLists.Brainarr.Services.Core.IRecommendationPipeline pipeline = null,
            NzbDrone.Core.ImportLists.Brainarr.Services.Core.IRecommendationCoordinator coordinator = null,
            NzbDrone.Core.ImportLists.Brainarr.Services.ILibraryAwarePromptBuilder promptBuilder = null,
            IStyleCatalogService styleCatalog = null,
            IBreakerRegistry breakerRegistry = null,
            IDuplicateFilterService duplicateFilter = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
            _libraryAnalyzer = libraryAnalyzer ?? throw new ArgumentNullException(nameof(libraryAnalyzer));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _providerHealth = providerHealth ?? throw new ArgumentNullException(nameof(providerHealth));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _modelDetection = modelDetection ?? throw new ArgumentNullException(nameof(modelDetection));
            _modelOptionsProvider = new ModelOptionsProvider(_modelDetection);
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _providerLifecycle = new ProviderLifecycleService(logger, providerFactory, providerHealth, httpClient);
            _configValidator = new ConfigurationValidator(logger, _providerLifecycle, modelDetection);
            _duplicationPrevention = duplicationPrevention ?? new DuplicationPreventionService(logger);
            _mbidResolver = mbidResolver ?? new NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.MusicBrainzResolver(logger);
            _artistResolver = artistResolver ?? new NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver(logger, httpClient: null);
            if (_artistSearchService != null && _artistResolver is NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver resolverInstance)
            {
                resolverInstance.AttachSearchService(_artistSearchService);
            }
            _reviewQueue = new ReviewQueueService(logger);
            _history = new RecommendationHistory(logger);
            _promptBuilder = promptBuilder ?? new LibraryAwarePromptBuilder(logger);
            _metrics = new NzbDrone.Core.ImportLists.Brainarr.Performance.PerformanceMetrics(logger);
            _persistSettingsCallback = persistSettingsCallback;
            _sanitizer = sanitizer ?? new NzbDrone.Core.ImportLists.Brainarr.Services.RecommendationSanitizer(logger);
            _schemaValidator = schemaValidator ?? new NzbDrone.Core.ImportLists.Brainarr.Services.Core.RecommendationSchemaValidator(logger);
            _providerInvoker = providerInvoker ?? new ProviderInvoker();
            _safetyGates = safetyGates ?? new SafetyGateService();
            _topUpPlanner = topUpPlanner ?? new TopUpPlanner(logger, duplicateFilter);
            _pipeline = pipeline ?? new RecommendationPipeline(logger, _libraryAnalyzer, duplicateFilter, _validator, _safetyGates, _topUpPlanner, _mbidResolver, _artistResolver, _duplicationPrevention, _metrics, _history);
            _coordinator = coordinator ?? new RecommendationCoordinator(
                logger,
                _cache,
                _pipeline,
                _sanitizer,
                _schemaValidator,
                _history,
                // Avoid ServiceLocator: fall back to a minimal LibraryProfileService that returns an empty
                // profile when artist/album services are not available (tests can inject a coordinator).
                new LibraryProfileService(new LibraryContextBuilder(logger), logger, artistService: null, albumService: null),
                new RecommendationCacheKeyBuilder(new DefaultPlannerVersionProvider()));
            _styleCatalog = styleCatalog ?? new StyleCatalogService(logger, httpClient);
            _reviewQueueHandler = new ReviewQueueActionHandler(_reviewQueue, _history, _styleCatalog, _persistSettingsCallback, logger);
            _observability = new ObservabilityService(_reviewQueue, _metrics, GetProviderStatus, logger);
#if DEBUG
            // Test-only fallback: allows direct construction in unit tests without DI.
            // In production (Release), null registry throws to prevent silent split-brain.
            _breakerRegistry = breakerRegistry ?? new CommonBreakerRegistry();
#else
            _breakerRegistry = breakerRegistry ?? throw new ArgumentNullException(nameof(breakerRegistry),
                "IBreakerRegistry must be injected via DI. Direct construction without registry is not supported in Release builds.");
#endif
            _recommendationGenerator = new RecommendationGenerator(
                _logger,
                _providerLifecycle,
                _libraryAnalyzer,
                _promptBuilder,
                _providerHealth,
                _metrics,
                _breakerRegistry,
                _providerInvoker);
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
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Non-critical: Failed to log iteration profile");
                }
                if (settings.EnableDebugLogging)
                {
                    _logger.InfoWithCorrelation("[Brainarr Debug] Provider payload logging ENABLED for this run");
                }

                try
                {
                    // Step 1: Initialize provider if needed
                    InitializeProvider(settings);

                    // Step 2a: Validate provider configuration (hard fail)
                    if (!ConfigurationValidator.IsValidProviderConfiguration(settings))
                    {
                        _logger.Warn("Invalid provider configuration; aborting recommendation fetch");
                        return new List<ImportListItemInfo>();
                    }
                    // Step 2b: Health gating — if no dedicated invoker is injected, use a hard gate; otherwise soft-gate
                    var hardHealthGate = _providerInvoker == null || _providerInvoker.GetType() == typeof(ProviderInvoker);
                    if (!IsProviderHealthy())
                    {
                        if (hardHealthGate)
                        {
                            _logger.Warn("Provider reported unhealthy; aborting before orchestration");
                            return new List<ImportListItemInfo>();
                        }
                        else
                        {
                            _logger.Warn("Provider reported unhealthy; proceeding best-effort for resilience");
                        }
                    }

                    // Step 3: Library profile handled by coordinator

                    // Step 4‑6: Delegate cache + sanitize + pipeline orchestration to coordinator
                    var importItems = await _coordinator.RunAsync(
                        settings,
                        async (lp, ct) => await _recommendationGenerator.GenerateRecommendationsAsync(settings, lp),
                        _reviewQueue,
                        _providerLifecycle.CurrentProvider,
                        _promptBuilder,
                        default);

                    // Apply approvals selected via settings (Approve Suggestions Tag field) after pipeline
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
                            var add = _recommendationGenerator.ConvertToImportListItems(approvedNow);
                            importItems.AddRange(add);
                            settings.ReviewApproveKeys = Array.Empty<string>();
                            _reviewQueueHandler.TryPersistSettings();
                            _logger.Info($"Applied {applied} approvals from settings and cleared selections");
                        }
                    }
                    // importItems now contains the final set (coordinator handled caching)

                    _logger.Info($"Generated {importItems.Count} validated recommendations");
                    try
                    {
                        _metrics.RecordRecommendationCount(importItems.Count);
                        var snap = _metrics.GetSnapshot();
                        var pm = _providerHealth.GetMetrics(_providerLifecycle.CurrentProvider?.ProviderName ?? settings.Provider.ToString());
                        _logger.InfoWithCorrelation($"Run summary: provider={_providerLifecycle.CurrentProvider?.ProviderName ?? settings.Provider.ToString()}, items={importItems.Count}, cache=miss, successRate={pm.SuccessRate:F1}%, avgMs={pm.AverageResponseTimeMs:F0}, cacheHitRate={snap.CacheHitRate:P0}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Non-critical: Failed to record run summary metrics");
                    }
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
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Non-critical: Failed to log iteration profile");
                    }

                    if (!ConfigurationValidator.IsValidProviderConfiguration(settings))
                    {
                        _logger.Warn("Invalid provider configuration; aborting recommendation fetch");
                        return new List<ImportListItemInfo>();
                    }
                    if (!IsProviderHealthy())
                    {
                        _logger.Warn("Provider reported unhealthy; proceeding best-effort for resilience");
                    }

                    var importItems = await _coordinator.RunAsync(
                        settings,
                        async (lp, ct) => await _recommendationGenerator.GenerateRecommendationsAsync(settings, lp, cancellationToken),
                        _reviewQueue,
                        _providerLifecycle.CurrentProvider,
                        _promptBuilder,
                        cancellationToken);

                    // Coordinator handled caching

                    try
                    {
                        _metrics.RecordRecommendationCount(importItems.Count);
                        var snap = _metrics.GetSnapshot();
                        var pm = _providerHealth.GetMetrics(_providerLifecycle.CurrentProvider?.ProviderName ?? settings.Provider.ToString());
                        _logger.InfoWithCorrelation($"Run summary: provider={_providerLifecycle.CurrentProvider?.ProviderName ?? settings.Provider.ToString()}, items={importItems.Count}, cache=miss, successRate={pm.SuccessRate:F1}%, avgMs={pm.AverageResponseTimeMs:F0}, cacheHitRate={snap.CacheHitRate:P0}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Non-critical: Failed to record run summary metrics");
                    }
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

        public IList<ImportListItemInfo> FetchRecommendations(BrainarrSettings settings)
        {
            // Synchronous wrapper for backward compatibility (avoid direct GetAwaiter().GetResult())
            return NzbDrone.Core.ImportLists.Brainarr.Utils.SafeAsyncHelper.RunSafeSync(() => FetchRecommendationsAsync(settings));
        }

        // ====== PROVIDER MANAGEMENT ======

        public void InitializeProvider(BrainarrSettings settings)
        {
            _providerLifecycle.InitializeProvider(settings);
        }

        public void UpdateProviderConfiguration(BrainarrSettings settings)
        {
            _providerLifecycle.UpdateProviderConfiguration(settings);
        }

        public async Task<bool> TestProviderConnectionAsync(BrainarrSettings settings)
        {
            return await _providerLifecycle.TestProviderConnectionAsync(settings);
        }

        public bool IsProviderHealthy()
        {
            return _providerLifecycle.IsProviderHealthy();
        }

        public string GetProviderStatus()
        {
            return _providerLifecycle.GetProviderStatus();
        }

        // ====== CONFIGURATION VALIDATION ======

        public void ValidateConfiguration(BrainarrSettings settings, List<ValidationFailure> failures)
        {
            _configValidator.Validate(settings, failures);
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
                    "getmodeloptions" => SafeAsyncHelper.RunSafeSync(() => _modelOptionsProvider.GetModelOptionsAsync(settings, query)),
                    "detectmodels" => SafeAsyncHelper.RunSafeSync(() => _modelOptionsProvider.DetectModelsAsync(settings, query)),
                    "testconnection" => SafeAsyncHelper.RunSafeSync(() => TestProviderConnectionAsync(settings)),
                    "getproviderstatus" => GetProviderStatus(),
                    // Review Queue actions
                    "review/getqueue" => new { items = _reviewQueue.GetPending() },
                    "review/accept" => _reviewQueueHandler.HandleReviewUpdate(query, ReviewQueueService.ReviewStatus.Accepted),
                    "review/reject" => _reviewQueueHandler.HandleReviewUpdate(query, ReviewQueueService.ReviewStatus.Rejected),
                    "review/never" => _reviewQueueHandler.HandleReviewNever(query),
                    "review/apply" => _reviewQueueHandler.ApplyApprovalsNow(settings, query),
                    "review/clear" => _reviewQueueHandler.ClearApprovalSelections(settings),
                    "review/rejectselected" => _reviewQueueHandler.RejectOrNeverSelected(settings, query, ReviewQueueService.ReviewStatus.Rejected),
                    "review/neverselected" => _reviewQueueHandler.RejectOrNeverSelected(settings, query, ReviewQueueService.ReviewStatus.Never),
                    // Metrics snapshot (lightweight)
                    "metrics/get" => _observability.GetMetricsSnapshot(),
                    // Prometheus export (plain text)
                    "metrics/prometheus" => NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.ExportPrometheus(),
                    // Observability (respect feature flag)
                    "observability/get" => settings.EnableObservabilityPreview ? _observability.GetObservabilitySummary(query) : new { disabled = true },
                    "observability/getoptions" => settings.EnableObservabilityPreview ? _observability.GetObservabilityOptions() : new { options = Array.Empty<object>() },
                    "observability/html" => settings.EnableObservabilityPreview ? _observability.GetObservabilityHtml(query) : "<html><body><p>Observability preview is disabled.</p></body></html>",
                    // Styles TagSelect options
                    "styles/getoptions" => _reviewQueueHandler.GetStylesOptions(query),
                    // Options for Approve Suggestions Select field
                    "review/getoptions" => _reviewQueueHandler.GetReviewOptions(),
                    // Read-only Review Summary options
                    "review/getsummaryoptions" => _reviewQueueHandler.GetReviewSummaryOptions(),
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

        // Library profile retrieval moved to RecommendationCoordinator

        // Optional: allow host to attach Lidarr's artist search for stronger MBID mapping
        public void AttachArtistSearchService(NzbDrone.Core.MetadataSource.ISearchForNewArtist search)
        {
            _artistSearchService = search;
            // Recreate resolver if using default implementation to take advantage of search service
            if (_artistResolver is NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver)
            {
                _artistResolver = new NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver(_logger, httpClient: null);
                if (_artistResolver is NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver resolver)
                {
                    resolver.AttachSearchService(_artistSearchService);
                }
                _logger.Info("Attached Lidarr artist search to MBID resolver");
            }
        }

    }
}
