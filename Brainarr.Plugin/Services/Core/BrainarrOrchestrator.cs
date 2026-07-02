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
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Lidarr.Plugin.Common.Observability;

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
        private readonly ReviewActionAuditService _auditService;
        private readonly LibraryGapPlannerService _gapPlannerService;
        private readonly LibraryHealerActionHandler _healerActionHandler;
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
            IDuplicateFilterService duplicateFilter = null,
            LibraryHealerActionHandler healerActionHandler = null)
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
            _auditService = new ReviewActionAuditService(logger);
            _reviewQueueHandler = new ReviewQueueActionHandler(_reviewQueue, _history, _styleCatalog, new RecommendationTriageAdvisor(), _persistSettingsCallback, logger, _auditService);
            _gapPlannerService = new LibraryGapPlannerService();
            _healerActionHandler = healerActionHandler;
            _observability = new ObservabilityService(_reviewQueue, _metrics, GetProviderStatus, logger);
            _breakerRegistry = breakerRegistry ?? throw new ArgumentNullException(nameof(breakerRegistry),
                "IBreakerRegistry must be injected via DI. Use PassThroughBreakerRegistry.CreateMock().Object in tests.");
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
            var operationKey = $"fetch_{settings.Provider}_{settings.GetHashCode()}";

            return await _duplicationPrevention.PreventConcurrentFetch(operationKey, async () =>
            {
                using var _ctx = PluginLogContext.Push("Brainarr", "Recommend", provider: settings.Provider.ToString().ToLowerInvariant());
                using var _corr = CorrelationContext.BeginScope();
                using var _dbg = DebugFlags.PushFromSettings(settings);
                _logger.InfoWithCorrelation($"{PluginLogContext.Current?.LinePrefix()}Starting consolidated recommendation workflow");
                if (settings.EnableDebugLogging)
                {
                    _logger.InfoWithCorrelation("[Brainarr Debug] Provider payload logging ENABLED for this run");
                }

                try
                {
                    // Hard health gate for non-cancellable path
                    var hardHealthGate = _providerInvoker == null || _providerInvoker.GetType() == typeof(ProviderInvoker);
                    var importItems = await ExecuteWorkflowCoreAsync(settings, hardHealthGate, default).ConfigureAwait(false);

                    // Apply approvals selected via settings (Approve Suggestions Tag field) after pipeline
                    ApplyPendingReviewApprovals(settings, importItems);

                    _logger.Info($"Generated {importItems.Count} validated recommendations");
                    return (IList<ImportListItemInfo>)importItems;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in consolidated recommendation workflow");
                    return new List<ImportListItemInfo>();
                }
            }).ConfigureAwait(false);
        }

        public async Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings, CancellationToken cancellationToken)
        {
            var operationKey = $"fetch_{settings.Provider}_{settings.GetHashCode()}";

            return await _duplicationPrevention.PreventConcurrentFetch(operationKey, async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var importItems = await ExecuteWorkflowCoreAsync(settings, hardHealthGate: false, cancellationToken).ConfigureAwait(false);
                    return (IList<ImportListItemInfo>)importItems;
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
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Shared workflow core: initialize, validate, coordinate, record metrics.
        /// </summary>
        private async Task<List<ImportListItemInfo>> ExecuteWorkflowCoreAsync(
            BrainarrSettings settings, bool hardHealthGate, CancellationToken cancellationToken)
        {
            // Step 1: Initialize provider
            InitializeProvider(settings);
            if (cancellationToken.IsCancellationRequested) return new List<ImportListItemInfo>();

            // Log iteration profile (non-critical)
            try
            {
                var ip = settings.GetIterationProfile();
                _logger.Info($"Backfill Plan => Strategy={settings.BackfillStrategy}, Enabled={ip.EnableRefinement}, MaxIterations={ip.MaxIterations}, ZeroStop={ip.ZeroStop}, LowStop={ip.LowStop}, GuaranteeExactTarget={ip.GuaranteeExactTarget}");
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Non-critical: Failed to log iteration profile");
            }

            // Step 2a: Validate provider configuration
            if (!ConfigurationValidator.IsValidProviderConfiguration(settings))
            {
                _logger.Warn("Invalid provider configuration; aborting recommendation fetch");
                return new List<ImportListItemInfo>();
            }

            // Step 2b: Health gating
            if (!IsProviderHealthy())
            {
                if (hardHealthGate)
                {
                    _logger.Warn("Provider reported unhealthy; aborting before orchestration");
                    return new List<ImportListItemInfo>();
                }

                _logger.Warn("Provider reported unhealthy; proceeding best-effort for resilience");
            }

            // Per-run, race-free accumulation of safety-gate drop reasons. The scope's holder flows
            // down to the gate via AsyncLocal (the gate runs inside the awaited coordinator call), so
            // its writes are visible here afterward — and concurrent fetches get isolated holders, so
            // one run's floor drops never inflate another's hint (a shared cumulative counter would).
            List<ImportListItemInfo> importItems;
            int floorDropsThisRun;
            using (GateMetricsContext.BeginScope())
            {
                // Step 3-6: Delegate to coordinator
                importItems = await _coordinator.RunAsync(
                    settings,
                    async (lp, ct) => await _recommendationGenerator.GenerateRecommendationsAsync(settings, lp, cancellationToken).ConfigureAwait(false),
                    _reviewQueue,
                    _providerLifecycle.CurrentProvider,
                    _promptBuilder,
                    cancellationToken).ConfigureAwait(false);

                floorDropsThisRun = GateMetricsContext.ConfidenceFloorDrops;
            }

            // Record metrics (non-critical)
            RecordRunSummary(settings, importItems, floorDropsThisRun);

            return importItems;
        }

        private void ApplyPendingReviewApprovals(BrainarrSettings settings, List<ImportListItemInfo> importItems)
        {
            if (settings.ReviewApproveKeys == null) return;

            int applied = 0;
            foreach (var key in settings.ReviewApproveKeys)
            {
                // Split on the LAST '|' to match SafetyGateService's key contract: an artist that
                // contains '|' (e.g. "AC|DC") encodes as "AC|DC|Album", so a first-pipe split
                // misparsed artist/album and the approval silently never matched a pending entry.
                var k = key ?? "";
                var lastPipe = k.LastIndexOf('|');
                if (lastPipe > 0 && _reviewQueue.SetStatus(k.Substring(0, lastPipe), k.Substring(lastPipe + 1), ReviewQueueService.ReviewStatus.Accepted))
                {
                    applied++;
                }
            }

            if (applied > 0)
            {
                var approvedNow = _reviewQueue.DequeueAccepted();
                importItems.AddRange(_recommendationGenerator.ConvertToImportListItems(approvedNow));
                settings.ReviewApproveKeys = Array.Empty<string>();
                _reviewQueueHandler.TryPersistSettings();
                _logger.Info($"Applied {applied} approvals from settings and cleared selections");
            }
        }

        private void RecordRunSummary(BrainarrSettings settings, List<ImportListItemInfo> importItems, int confidenceFloorDropsThisRun = 0)
        {
            try
            {
                _metrics.RecordRecommendationCount(importItems.Count);
                var snap = _metrics.GetSnapshot();
                var providerName = _providerLifecycle.CurrentProvider?.ProviderName ?? settings.Provider.ToString();
                var pm = _providerHealth.GetMetrics(providerName);
                var target = settings.MaxRecommendations > 0
                    ? settings.MaxRecommendations
                    : BrainarrConstants.DefaultRecommendations;
                var attainment = AttainmentPercent(importItems.Count, target);
                // `providerSuccess` is the provider-health success rate (successful HTTP calls /
                // total) — it can read 100% even when we deliver far fewer items than asked. Report
                // target attainment separately so the two are never conflated (the "100% but 17/50"
                // confusion). When under target, say why so the user knows which knob to turn.
                _logger.InfoWithCorrelation($"Run summary: provider={providerName}, items={importItems.Count}, target={target}, attainment={attainment}%, providerSuccess={pm.SuccessRate:F1}%, cache=miss, avgMs={pm.AverageResponseTimeMs:F0}, cacheHitRate={snap.CacheHitRate:P0}");
                if (importItems.Count < target)
                {
                    if (confidenceFloorDropsThisRun > 0)
                    {
                        // The floor demonstrably gated items THIS run — name it as the concrete cause
                        // and the exact knob, rather than only the generic typical-causes list.
                        var floor = Math.Max(0.0, Math.Min(1.0, settings.MinConfidence));
                        // "held below" (not "dropped"): with Queue Borderline Items on these go to the
                        // review queue (recoverable); either way, lowering the floor re-admits them.
                        _logger.InfoWithCorrelation($"Under target: delivered {importItems.Count}/{target}. {confidenceFloorDropsThisRun} recommendation(s) were held below your Minimum Confidence floor ({floor:F2}) this run — lower it (Settings → Minimum Confidence) to include them. Other causes: provider truncation/timeout, dedup, or MBID gating.");
                    }
                    else
                    {
                        _logger.InfoWithCorrelation($"Under target: delivered {importItems.Count}/{target}. Typical causes: provider truncation/timeout (raise AI Request Timeout), dedup against your library/history as it grows, or confidence/MBID gating. Widen Discovery (Adjacent/Exploratory) or add style filters to surface more.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Non-critical: Failed to record run summary metrics");
            }
        }

        /// <summary>
        /// Target attainment as a whole-number percent of delivered items vs the requested count,
        /// clamped to [0,100]. Distinct from provider-health success rate. Pure for testability.
        /// </summary>
        internal static int AttainmentPercent(int items, int target)
        {
            if (target <= 0) return 0;
            if (items <= 0) return 0;
            // Truncate (floor) rather than round: rounding 199/200 up to 100% while the run is still
            // under target would contradict the "Under target" explainer printed alongside it.
            var pct = (int)(100.0 * items / target);
            return pct > 100 ? 100 : pct;
        }

        public IList<ImportListItemInfo> FetchRecommendations(BrainarrSettings settings)
        {
            // Synchronous wrapper for backward compatibility (avoid direct GetAwaiter().GetResult()).
            // Budget the overall fetch from the user's per-request timeout × the iterations the run
            // may make, rather than the hardcoded 120s default — otherwise a raised "AI Request
            // Timeout" (e.g. 360s for slow GLM-5.x reasoning models) was silently guillotined at 2
            // minutes mid-top-up, capping results well under target.
            var overallTimeoutMs = settings.GetOverallFetchTimeoutMs();
            return NzbDrone.Core.ImportLists.Brainarr.Utils.SafeAsyncHelper.RunSafeSync(
                () => FetchRecommendationsAsync(settings), overallTimeoutMs);
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
            return await _providerLifecycle.TestProviderConnectionAsync(settings).ConfigureAwait(false);
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
            var isHealerAction = action != null && action.StartsWith("healer/", StringComparison.OrdinalIgnoreCase);
            _logger.Debug($"Handling UI action: {(isHealerAction ? "healer/*" : action)}");

            if (isHealerAction)
            {
                if (_healerActionHandler == null)
                {
                    return new { error = "Library Healer is not available in this runtime" };
                }

                try
                {
                    return _healerActionHandler.Handle(action, query);
                }
                catch (Exception ex)
                {
                    var redactedError = LibraryHealerActionHandler.SanitizeBoundaryString(ex.Message) ?? ex.GetType().Name;
                    _logger.Error($"Error handling healer action: {redactedError}");
                    return new { error = redactedError };
                }
            }

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
                    "review/applysimulation" => _reviewQueueHandler.SimulateReviewApply(settings, query),
                    "review/simulateapply" => _reviewQueueHandler.SimulateReviewApply(settings, query),
                    "review/applytriage" => _reviewQueueHandler.ApplyTriageSuggestions(settings, query),
                    "review/rollbacktriage" => _reviewQueueHandler.RollbackTriageApplication(query),
                    "review/explain" => _reviewQueueHandler.ExplainItem(settings, query, settings?.EnableProviderCalibration == true ? settings?.Provider : null),
                    "review/clear" => _reviewQueueHandler.ClearApprovalSelections(settings),
                    "review/rejectselected" => _reviewQueueHandler.RejectOrNeverSelected(settings, query, ReviewQueueService.ReviewStatus.Rejected),
                    "review/neverselected" => _reviewQueueHandler.RejectOrNeverSelected(settings, query, ReviewQueueService.ReviewStatus.Never),
                    // Metrics snapshot (lightweight)
                    "metrics/get" => _observability.GetMetricsSnapshot(),
                    // Prometheus export (plain text)
                    "metrics/prometheus" => NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.ExportPrometheus(),
                    // Observability (respect feature flag). get/html are the metric VIEWS — apply the
                    // configured default provider/model filter when the request omits one. getoptions is
                    // the picker (lists ALL series), so it is deliberately NOT pre-filtered.
                    "observability/get" => settings.EnableObservabilityPreview ? _observability.GetObservabilitySummary(WithObservabilityFilterDefaults(query, settings)) : new { disabled = true },
                    "observability/getoptions" => settings.EnableObservabilityPreview ? _observability.GetObservabilityOptions() : new { options = Array.Empty<object>() },
                    "observability/html" => settings.EnableObservabilityPreview ? _observability.GetObservabilityHtml(WithObservabilityFilterDefaults(query, settings)) : "<html><body><p>Observability preview is disabled.</p></body></html>",
                    // Styles TagSelect options
                    "styles/getoptions" => _reviewQueueHandler.GetStylesOptions(query),
                    // Options for Approve Suggestions Select field
                    "review/getoptions" => _reviewQueueHandler.GetReviewOptions(),
                    "review/gettriageoptions" => _reviewQueueHandler.GetReviewTriageOptions(settings),
                    // Read-only Review Summary options
                    "review/getsummaryoptions" => _reviewQueueHandler.GetReviewSummaryOptions(),
                    "review/getaudit" => _reviewQueueHandler.GetReviewActionAudit(query),
                    "review/getrollbackoptions" => _reviewQueueHandler.GetRollbackOptions(query),
                    "planning/getgapplan" => new
                    {
                        options = _gapPlannerService.BuildPlan(_libraryAnalyzer.AnalyzeLibrary(), 5)
                            .Select(item => new
                            {
                                value = $"{item.Category}:{item.Target}",
                                name = $"{item.Target} · P{item.Priority} · Lift {item.ExpectedLift:P0}",
                                category = item.Category,
                                target = item.Target,
                                priority = item.Priority,
                                confidence = item.Confidence,
                                rationale = item.Rationale,
                                suggestedAction = item.SuggestedAction,
                                evidence = item.Evidence,
                                expectedLift = item.ExpectedLift,
                                whyNow = item.WhyNow
                            })
                            .ToList()
                    },
                    "planning/simulategapplan" => SimulateGapPlan(settings, query),
                    "planning/applygapplan" => ApplyGapPlan(settings, query),
                    "planning/rollbackgapplan" => RollbackGapPlan(query),
                    _ => throw new NotSupportedException($"Action '{action}' is not supported")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling action: {action}");
                return new { error = ex.Message };
            }
        }

        /// <summary>
        /// Merges the configured <c>ObservabilityProviderFilter</c>/<c>ObservabilityModelFilter</c>
        /// defaults into an observability-view query: a default is applied only when the request did
        /// not already specify that key (an explicit request filter always wins). Returns a fresh,
        /// case-insensitive copy so the caller's query is never mutated. Static + internal so the
        /// wiring is unit-testable without constructing the orchestrator.
        /// </summary>
        internal static IDictionary<string, string> WithObservabilityFilterDefaults(
            IDictionary<string, string> query, BrainarrSettings settings)
        {
            // Build defensively (last-wins) rather than the copy-constructor, which THROWS on a query
            // that contains case-colliding duplicate keys (e.g. both "provider" and "Provider") once
            // re-hashed under OrdinalIgnoreCase. Case-insensitive so an explicit "Provider" filter is
            // still seen as present and not clobbered by the default.
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (query != null)
            {
                foreach (var kv in query) merged[kv.Key] = kv.Value;
            }

            if (settings == null) return merged;

            static bool Missing(IDictionary<string, string> q, string key)
                => !q.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v);

            if (!string.IsNullOrWhiteSpace(settings.ObservabilityProviderFilter) && Missing(merged, "provider"))
                merged["provider"] = settings.ObservabilityProviderFilter;
            if (!string.IsNullOrWhiteSpace(settings.ObservabilityModelFilter) && Missing(merged, "model"))
                merged["model"] = settings.ObservabilityModelFilter;

            return merged;
        }

        // ====== GAP PLANNER v2 ======

        private object SimulateGapPlan(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var budget = TryParseQueryInt(query, "budget");
            var maxItems = TryParseQueryInt(query, "max") ?? 5;
            var minConf = TryParseQueryDouble(query, "minConfidence") ?? 0.0;
            var simulation = _gapPlannerService.Simulate(_libraryAnalyzer.AnalyzeLibrary(), maxItems, budget, minConf);
            return new
            {
                ok = true,
                dryRun = true,
                items = simulation.Items.Select(FormatGapPlanItem).ToList(),
                totalItems = simulation.TotalItems,
                budgetApplied = simulation.BudgetApplied,
                budgetRemaining = simulation.BudgetRemaining,
                averageConfidence = simulation.AverageConfidence,
                totalExpectedLift = simulation.TotalExpectedLift
            };
        }

        private object ApplyGapPlan(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var idempotencyKey = ReviewActionAuditService.SanitizeIdempotencyKey(
                query != null && query.TryGetValue("idempotencyKey", out var rawKey) ? rawKey : null);

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return new { ok = false, error = "idempotencyKey is required for gap plan apply" };
            }

            if (_auditService.TryGetByIdempotencyKey("planning/applygapplan", idempotencyKey, out var previous))
            {
                return new { ok = true, replay = true, id = previous.Id, appliedCount = previous.ApprovedCount };
            }

            var budget = TryParseQueryInt(query, "budget");
            var maxItems = TryParseQueryInt(query, "max") ?? 5;
            var minConf = TryParseQueryDouble(query, "minConfidence") ?? 0.0;
            var actor = ReviewActionAuditService.SanitizeActor(
                query != null && query.TryGetValue("actor", out var rawActor) ? rawActor : null);
            var plan = _gapPlannerService.BuildPlan(_libraryAnalyzer.AnalyzeLibrary(), maxItems, budget, minConf);

            var auditId = Guid.NewGuid().ToString("N");
            _auditService.Write(new ReviewActionAuditEvent(
                Id: auditId,
                Action: "planning/applygapplan",
                Actor: actor,
                DryRun: false,
                Mode: "gap-plan",
                PendingCount: 0,
                CandidateCount: plan.Count,
                ApprovedCount: plan.Count,
                ReleasedCount: 0,
                Cap: maxItems,
                Capped: false,
                ReasonCodes: plan.SelectMany(p => p.Evidence ?? Array.Empty<string>()).Take(10).ToList(),
                OccurredAtUtc: DateTime.UtcNow,
                IdempotencyKey: idempotencyKey,
                Items: plan.Select(p => new ReviewActionAuditItem(
                    p.Target, p.Category, p.Category, p.Confidence, p.SuggestedAction, null, null, null)).ToList()));

            return new
            {
                ok = true,
                id = auditId,
                appliedCount = plan.Count,
                budgetApplied = budget.HasValue,
                items = plan.Select(FormatGapPlanItem).ToList()
            };
        }

        private object RollbackGapPlan(IDictionary<string, string> query)
        {
            var targetId = query != null && query.TryGetValue("id", out var rawId) ? rawId : null;

            if (string.IsNullOrWhiteSpace(targetId))
            {
                var recentApplies = _auditService.GetRecent("planning/applygapplan", 1);
                if (recentApplies.Count == 0)
                {
                    return new { ok = false, error = "No gap plan applications found to rollback" };
                }

                targetId = recentApplies[0].Id;
            }

            if (!_auditService.TryGetById(targetId, out var auditEntry))
            {
                return new { ok = false, error = $"Audit entry '{targetId}' not found" };
            }

            var rollbackId = Guid.NewGuid().ToString("N");
            _auditService.Write(new ReviewActionAuditEvent(
                Id: rollbackId,
                Action: "planning/rollbackgapplan",
                Actor: ReviewActionAuditService.SanitizeActor(
                    query != null && query.TryGetValue("actor", out var rawActor) ? rawActor : null),
                DryRun: false,
                Mode: "gap-plan-rollback",
                PendingCount: 0,
                CandidateCount: auditEntry.Items?.Count ?? 0,
                ApprovedCount: 0,
                ReleasedCount: 0,
                Cap: 0,
                Capped: false,
                ReasonCodes: new List<string> { $"rollback_of={targetId}" },
                OccurredAtUtc: DateTime.UtcNow,
                RollbackOfId: targetId));

            return new { ok = true, rollbackId, rolledBackId = targetId, restoredCount = auditEntry.Items?.Count ?? 0 };
        }

        private static object FormatGapPlanItem(LibraryGapPlanItem item) => new
        {
            value = $"{item.Category}:{item.Target}",
            name = $"{item.Target} · P{item.Priority} · Lift {item.ExpectedLift:P0}",
            category = item.Category,
            target = item.Target,
            priority = item.Priority,
            confidence = item.Confidence,
            rationale = item.Rationale,
            suggestedAction = item.SuggestedAction,
            evidence = item.Evidence,
            expectedLift = item.ExpectedLift,
            whyNow = item.WhyNow
        };

        private static int? TryParseQueryInt(IDictionary<string, string> query, string key)
        {
            if (query != null && query.TryGetValue(key, out var raw) && int.TryParse(raw, out var value))
            {
                return value;
            }

            return null;
        }

        private static double? TryParseQueryDouble(IDictionary<string, string> query, string key)
        {
            if (query != null && query.TryGetValue(key, out var raw) && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return null;
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
