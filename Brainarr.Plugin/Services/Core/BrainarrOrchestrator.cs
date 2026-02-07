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
using System.Diagnostics;
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

        private IAIProvider _currentProvider;
        private AIProvider? _currentProviderType;

        // Lightweight shared registries (internal defaults; can be DI-wired later)
        private static readonly Lazy<ILimiterRegistry> _limiterRegistry = new(() => new LimiterRegistry());
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
            IBreakerRegistry breakerRegistry = null)
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
            _topUpPlanner = topUpPlanner ?? new TopUpPlanner(logger);
            _pipeline = pipeline ?? new RecommendationPipeline(logger, _libraryAnalyzer, _validator, _safetyGates, _topUpPlanner, _mbidResolver, _artistResolver, _duplicationPrevention, _metrics, _history);
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
#if DEBUG
            // Test-only fallback: allows direct construction in unit tests without DI.
            // In production (Release), null registry throws to prevent silent split-brain.
            _breakerRegistry = breakerRegistry ?? new CommonBreakerRegistry();
#else
            _breakerRegistry = breakerRegistry ?? throw new ArgumentNullException(nameof(breakerRegistry),
                "IBreakerRegistry must be injected via DI. Direct construction without registry is not supported in Release builds.");
#endif
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
                    if (!IsValidProviderConfiguration(settings))
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
                        async (lp, ct) => await GenerateRecommendationsAsync(settings, lp),
                        _reviewQueue,
                        _currentProvider,
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
                            var add = ConvertToImportListItems(approvedNow);
                            importItems.AddRange(add);
                            settings.ReviewApproveKeys = Array.Empty<string>();
                            TryPersistSettings();
                            _logger.Info($"Applied {applied} approvals from settings and cleared selections");
                        }
                    }
                    // importItems now contains the final set (coordinator handled caching)

                    _logger.Info($"Generated {importItems.Count} validated recommendations");
                    try
                    {
                        _metrics.RecordRecommendationCount(importItems.Count);
                        var snap = _metrics.GetSnapshot();
                        var pm = _providerHealth.GetMetrics(_currentProvider?.ProviderName ?? settings.Provider.ToString());
                        _logger.InfoWithCorrelation($"Run summary: provider={_currentProvider?.ProviderName ?? settings.Provider.ToString()}, items={importItems.Count}, cache=miss, successRate={pm.SuccessRate:F1}%, avgMs={pm.AverageResponseTimeMs:F0}, cacheHitRate={snap.CacheHitRate:P0}");
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

                    if (!IsValidProviderConfiguration(settings))
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
                        async (lp, ct) => await GenerateRecommendationsAsync(settings, lp, cancellationToken),
                        _reviewQueue,
                        _currentProvider,
                        _promptBuilder,
                        cancellationToken);

                    // Coordinator handled caching

                    try
                    {
                        _metrics.RecordRecommendationCount(importItems.Count);
                        var snap = _metrics.GetSnapshot();
                        var pm = _providerHealth.GetMetrics(_currentProvider?.ProviderName ?? settings.Provider.ToString());
                        _logger.InfoWithCorrelation($"Run summary: provider={_currentProvider?.ProviderName ?? settings.Provider.ToString()}, items={importItems.Count}, cache=miss, successRate={pm.SuccessRate:F1}%, avgMs={pm.AverageResponseTimeMs:F0}, cacheHitRate={snap.CacheHitRate:P0}");
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

        private async Task<List<Recommendation>> GenerateRecommendationsAsync(BrainarrSettings settings, LibraryProfile libraryProfile, System.Threading.CancellationToken cancellationToken)
        {
            if (_currentProvider == null) throw new InvalidOperationException("Provider not initialized");

            var artistMode = settings.RecommendationMode == RecommendationMode.Artists;
            var allArtistsForPrompt = _libraryAnalyzer.GetAllArtists();
            var allAlbumsForPrompt = _libraryAnalyzer.GetAllAlbums();

            var targetCount = Math.Max(1, settings.MaxRecommendations);
            var batchPlan = BuildBatchPlan(settings, targetCount, artistMode).ToList();
            if (batchPlan.Count == 0) batchPlan.Add(targetCount);

            var aggregated = new List<Recommendation>(targetCount + 4);
            var seenArtistKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenAlbumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sessionExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var providerName = _currentProvider.ProviderName;
            var effectiveModel = settings?.EffectiveModel ?? settings?.ModelSelection ?? string.Empty;
            var key = ModelKey.From(providerName, effectiveModel);
            var breaker = _breakerRegistry.Get(key, _logger);

            var localProvider = settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio;
            var requestedTimeout = settings.AIRequestTimeoutSeconds;
            var effectiveTimeout = (localProvider && requestedTimeout <= BrainarrConstants.DefaultAITimeout)
                ? BrainarrConstants.LocalProviderDefaultTimeout
                : requestedTimeout;

            var tokenLimit = _promptBuilder.GetEffectiveTokenLimit(settings.SamplingStrategy, settings.Provider);
            var downgradeSampling = false;
            IDisposable? samplingScope = null;
            var lastBatch = new List<Recommendation>();

            try { LimiterRegistry.ConfigureFromSettings(settings); }
            catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to configure rate limiter from settings"); }

            using var _timeout = TimeoutContext.Push(effectiveTimeout);
            using (await _limiterRegistry.Value.AcquireAsync(key, cancellationToken).ConfigureAwait(false))
            {
                foreach (var batchHint in batchPlan)
                {
                    if (aggregated.Count >= targetCount) break;

                    var remaining = targetCount - aggregated.Count;
                    var desiredBatch = Math.Min(Math.Max(1, batchHint), remaining);
                    var adjustedBatch = desiredBatch;
                    LibraryPromptResult promptRes = null;
                    var attempts = 0;

                    while (true)
                    {
                        var originalMaxRecommendations = settings.MaxRecommendations;
                        try
                        {
                            settings.MaxRecommendations = adjustedBatch;
                            promptRes = _promptBuilder.BuildLibraryAwarePromptWithMetrics(
                                libraryProfile,
                                allArtistsForPrompt,
                                allAlbumsForPrompt,
                                settings,
                                artistMode,
                                cancellationToken);
                        }
                        finally
                        {
                            settings.MaxRecommendations = originalMaxRecommendations;
                        }

                        var estimatedTotal = promptRes.EstimatedTokens + EstimateCompletionTokens(adjustedBatch, artistMode);
                        if (estimatedTotal <= tokenLimit)
                        {
                            break;
                        }

                        if (samplingScope == null && settings.SamplingStrategy == SamplingStrategy.Comprehensive && settings.Provider == AIProvider.Gemini)
                        {
                            samplingScope = NzbDrone.Core.ImportLists.Brainarr.Services.Support.SettingScope.Apply(
                                getter: () => settings.SamplingStrategy,
                                setter: v => settings.SamplingStrategy = v,
                                newValue: SamplingStrategy.Balanced);
                            downgradeSampling = true;
                            if (settings.EnableDebugLogging)
                            {
                                try
                                {
                                    _logger.InfoWithCorrelation("[Brainarr Debug] Switched Gemini sampling to Balanced to stay within the safe token budget.");
                                }
                                catch (Exception ex)
                                {
                                    _logger.Debug(ex, "Non-critical: Failed to log sampling downgrade");
                                }
                            }
                            tokenLimit = _promptBuilder.GetEffectiveTokenLimit(settings.SamplingStrategy, settings.Provider);
                            attempts = 0;
                            continue;
                        }

                        if (adjustedBatch <= (settings.Provider == AIProvider.Gemini ? 6 : 3) || attempts >= 3)
                        {
                            if (settings.EnableDebugLogging)
                            {
                                try
                                {
                                    _logger.Warn($"[Brainarr Debug] Prompt estimate {estimatedTotal} tokens still above limit {tokenLimit}; proceeding with trimmed batch={adjustedBatch}.");
                                }
                                catch (Exception ex)
                                {
                                    _logger.Debug(ex, "Non-critical: Failed to log token estimate warning");
                                }
                            }
                            break;
                        }

                        adjustedBatch = Math.Max(settings.Provider == AIProvider.Gemini ? 6 : 3, adjustedBatch - 2);
                    }

                    if (promptRes == null)
                    {
                        continue;
                    }

                    if (settings.EnableDebugLogging)
                    {
                        try
                        {
                            var modelLabel = settings.ModelSelection;
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Model request => Provider={settings.Provider}, Model={modelLabel}, Mode={settings.RecommendationMode}, Sampling={settings.SamplingStrategy}, Discovery={settings.DiscoveryMode}, Batch={adjustedBatch}");
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Prompt ({promptRes.Prompt?.Length ?? 0} chars):\n{promptRes.Prompt}");
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Non-critical: Failed to log debug model request info");
                        }
                    }

                    var sw = Stopwatch.StartNew();
                    var batchResult = await breaker.ExecuteAsync(
                        async () => await _providerInvoker.InvokeAsync(_currentProvider, promptRes.Prompt, _logger, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    sw.Stop();

                    lastBatch = batchResult ?? new List<Recommendation>();
                    if (lastBatch.Count == 0)
                    {
                        continue;
                    }

                    _providerHealth.RecordSuccess(providerName, sw.Elapsed.TotalMilliseconds);
                    try { _metrics.RecordProviderResponseTime(providerName + ":" + effectiveModel, sw.Elapsed); } catch (Exception ex) { _logger.DebugWithCorrelation($"metrics_emit_failed provider_response_time: {ex.Message}"); }
                    try
                    {
                        var tags = new Dictionary<string, string>(NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.ProviderMetricsHelper.BuildTags(providerName, effectiveModel));
                        NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.RecordTiming(NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.ProviderMetricsHelper.ProviderLatencyMs, sw.Elapsed, tags);
                    }
                    catch (Exception ex) { _logger.DebugWithCorrelation($"metrics_emit_failed provider_latency: {ex.Message}"); }

                    foreach (var rec in lastBatch)
                    {
                        if (rec == null) continue;

                        var artistName = NormalizeSessionValue(rec.Artist);
                        if (string.IsNullOrWhiteSpace(artistName))
                        {
                            continue;
                        }

                        if (artistMode)
                        {
                            if (seenArtistKeys.Add(artistName))
                            {
                                aggregated.Add(rec);
                                AddSessionExclusion(sessionExclusions, rec.Artist);
                                if (aggregated.Count >= targetCount) break;
                            }
                            continue;
                        }

                        var albumName = NormalizeSessionValue(rec.Album);
                        if (string.IsNullOrWhiteSpace(albumName))
                        {
                            if (seenArtistKeys.Add(artistName))
                            {
                                aggregated.Add(rec);
                                AddSessionExclusion(sessionExclusions, rec.Artist);
                                if (aggregated.Count >= targetCount) break;
                            }
                            continue;
                        }

                        var albumKey = BuildAlbumSessionKey(artistName, albumName);
                        if (string.IsNullOrWhiteSpace(albumKey))
                        {
                            continue;
                        }

                        if (seenAlbumKeys.Add(albumKey))
                        {
                            aggregated.Add(rec);
                            AddSessionExclusion(sessionExclusions, rec.Artist, rec.Album);
                            if (aggregated.Count >= targetCount) break;
                        }
                    }

                    if (aggregated.Count >= targetCount)
                    {
                        break;
                    }
                }
            }

            samplingScope?.Dispose();

            if (aggregated.Count == 0 && (lastBatch == null || lastBatch.Count == 0))
            {
                _providerHealth.RecordFailure(providerName, "Empty recommendation result");
                try { _metrics.RecordProviderResponseTime(providerName + ":" + effectiveModel, TimeSpan.Zero); } catch (Exception ex) { _logger.DebugWithCorrelation($"metrics_emit_failed provider_response_time_zero: {ex.Message}"); }
                try
                {
                    var tags = new Dictionary<string, string>(NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.ProviderMetricsHelper.BuildTags(providerName, effectiveModel));
                    NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.IncrementCounter(NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.ProviderMetricsHelper.ProviderErrorsTotal, tags);
                }
                catch (Exception ex) { _logger.DebugWithCorrelation($"metrics_emit_failed provider_errors_total: {ex.Message}"); }
                LogProviderScoreboard(providerName);
                return new List<Recommendation>();
            }

            if (downgradeSampling && _currentProvider is GeminiProvider geminiProvider)
            {
                geminiProvider.SetUserMessage("Gemini used balanced sampling to stay within the safe token budget; recommendations may be slightly narrower than comprehensive mode.", BrainarrConstants.DocsGeminiSection);
            }

            LogProviderScoreboard(providerName);
            return aggregated.Count > 0 ? aggregated : lastBatch;
        }


        private static void AddSessionExclusion(HashSet<string> sessionExclusions, string artist, string album = null)
        {
            if (sessionExclusions == null) return;

            var label = string.IsNullOrWhiteSpace(album)
                ? NormalizeDisplayValue(artist)
                : BuildAlbumDisplayLabel(artist, album);

            if (!string.IsNullOrWhiteSpace(label))
            {
                sessionExclusions.Add(label);
            }
        }

        private static string BuildAlbumSessionKey(string normalizedArtist, string normalizedAlbum)
        {
            if (string.IsNullOrWhiteSpace(normalizedArtist) || string.IsNullOrWhiteSpace(normalizedAlbum))
            {
                return string.Empty;
            }

            return $"{normalizedArtist}::{normalizedAlbum}";
        }

        private static string NormalizeSessionValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var decoded = System.Net.WebUtility.HtmlDecode(value).Trim();
            return System.Text.RegularExpressions.Regex.Replace(decoded, "\\s+", " ");
        }

        private static string NormalizeDisplayValue(string value)
        {
            return NormalizeSessionValue(value);
        }

        private static string BuildAlbumDisplayLabel(string artist, string album)
        {
            var artistLabel = NormalizeDisplayValue(artist);
            var albumLabel = NormalizeDisplayValue(album);

            if (string.IsNullOrWhiteSpace(albumLabel))
            {
                return artistLabel;
            }

            if (string.IsNullOrWhiteSpace(artistLabel))
            {
                return albumLabel;
            }

            return $"{artistLabel} - {albumLabel}";
        }
        private static IEnumerable<int> BuildBatchPlan(BrainarrSettings settings, int targetCount, bool artistMode)
        {
            if (settings.Provider == AIProvider.Gemini)
            {
                var preferredBatch = settings.SamplingStrategy == SamplingStrategy.Comprehensive ? 12 : 15;
                preferredBatch = Math.Max(6, Math.Min(preferredBatch, targetCount));
                var remaining = targetCount;
                while (remaining > 0)
                {
                    var chunk = Math.Min(preferredBatch, remaining);
                    yield return chunk;
                    remaining -= chunk;
                }
            }
            else
            {
                yield return targetCount;
            }
        }

        private static int EstimateCompletionTokens(int count, bool artistMode)
        {
            var perItem = artistMode ? 48 : 64;
            var overhead = artistMode ? 96 : 128;
            return (count * perItem) + overhead;
        }

        private void LogProviderScoreboard(string providerName)
        {
            try
            {
                var m = _providerHealth.GetMetrics(providerName);
                _logger.InfoWithCorrelation($"[Scoreboard] {providerName} — success {m.SuccessRate:F1}% | avg {m.AverageResponseTimeMs:F0}ms | failures {m.FailedRequests}/{m.TotalRequests}");
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Non-critical: Failed to log provider scoreboard");
            }
        }

        public IList<ImportListItemInfo> FetchRecommendations(BrainarrSettings settings)
        {
            // Synchronous wrapper for backward compatibility (avoid direct GetAwaiter().GetResult())
            return NzbDrone.Core.ImportLists.Brainarr.Utils.SafeAsyncHelper.RunSafeSync(() => FetchRecommendationsAsync(settings));
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
                if (_currentProvider == null)
                {
                    throw new InvalidOperationException("ProviderFactory.CreateProvider returned null");
                }
                _currentProviderType = settings.Provider;
                try { NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.EventLogger.Log(_logger, NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.BrainarrEvent.ProviderSelected, $"provider={_currentProviderType} model={settings.EffectiveModel}"); }
                catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log provider selected event"); }
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
                    // If the currently initialized provider exposed a user-facing hint, surface it too
                    if (_currentProvider != null)
                    {
                        var hint = _currentProvider.GetLastUserMessage();
                        var docs = _currentProvider.GetLearnMoreUrl();
                        if (!string.IsNullOrWhiteSpace(hint))
                        {
                            // Include provider name for clarity and avoid duplicating generic error
                            var msg = _currentProvider.ProviderName + ": " + hint;
                            if (!string.IsNullOrWhiteSpace(docs))
                            {
                                msg += " (Learn more: " + docs + ")";
                            }
                            failures.Add(new ValidationFailure("Provider", msg));
                        }
                    }
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
                    "review/never" => HandleReviewNever(query),
                    "review/apply" => ApplyApprovalsNow(settings, query),
                    "review/clear" => ClearApprovalSelections(settings),
                    "review/rejectselected" => RejectOrNeverSelected(settings, query, Services.Support.ReviewQueueService.ReviewStatus.Rejected),
                    "review/neverselected" => RejectOrNeverSelected(settings, query, Services.Support.ReviewQueueService.ReviewStatus.Never),
                    // Metrics snapshot (lightweight)
                    "metrics/get" => GetMetricsSnapshot(),
                    // Prometheus export (plain text)
                    "metrics/prometheus" => NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.ExportPrometheus(),
                    // Observability (respect feature flag)
                    "observability/get" => settings.EnableObservabilityPreview ? GetObservabilitySummary(query, settings) : new { disabled = true },
                    "observability/getoptions" => settings.EnableObservabilityPreview ? GetObservabilityOptions() : new { options = Array.Empty<object>() },
                    "observability/html" => settings.EnableObservabilityPreview ? GetObservabilityHtml(query) : "<html><body><p>Observability preview is disabled.</p></body></html>",
                    // Styles TagSelect options
                    "styles/getoptions" => GetStylesOptions(query),
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

        private object GetStylesOptions(IDictionary<string, string> query)
        {
            try
            {
                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var q = Get(query, "query") ?? string.Empty;
                var items = _styleCatalog.Search(q, 50)
                    .Select(s => new { value = s.Slug, name = s.Name })
                    .ToList();
                return new { options = items };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "styles/getoptions failed");
                return new { options = Array.Empty<object>() };
            }
        }

        private object HandleReviewUpdate(IDictionary<string, string> query, Services.Support.ReviewQueueService.ReviewStatus status)
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

        private object HandleReviewNever(IDictionary<string, string> query)
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

        private object GetObservabilitySummary(IDictionary<string, string> query, BrainarrSettings settings)
        {
            try
            {
                var window = TimeSpan.FromMinutes(15);
                var lat = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.latency", window);
                var err = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.errors", window);
                var thr = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.429", window);

                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var prov = Get(query, "provider");
                var mod = Get(query, "model");
                string sf(string v) { try { return NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.ProviderMetricsHelper.SanitizeName(v); } catch { return v; } }
                var pf = string.IsNullOrWhiteSpace(prov) ? null : sf(prov);
                var mf = string.IsNullOrWhiteSpace(mod) ? null : sf(mod);
                bool Match(string name) { if (pf != null && !name.Contains($".{pf}.", StringComparison.Ordinal)) return false; if (mf != null && !name.EndsWith($".{mf}", StringComparison.Ordinal)) return false; return true; }
                lat = lat.Where(kv => Match(kv.Key)).ToDictionary(k => k.Key, v => v.Value);
                err = err.Where(kv => Match(kv.Key)).ToDictionary(k => k.Key, v => v.Value);
                thr = thr.Where(kv => Match(kv.Key)).ToDictionary(k => k.Key, v => v.Value);

                double GetP(System.Collections.Generic.Dictionary<string, NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsSummary> d, string k, double def = 0)
                    => d.TryGetValue(k, out var s) && s?.Percentiles != null ? s.Percentiles.P95 : def;
                int GetC(System.Collections.Generic.Dictionary<string, NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsSummary> d, string k)
                    => d.TryGetValue(k, out var s) ? s.Count : 0;

                var keys = lat.Keys.Union(err.Keys).Union(thr.Keys).ToList();
                var rows = keys.Select(k => new
                {
                    key = k.Replace("provider.", string.Empty),
                    p95 = GetP(lat, k),
                    errors = GetC(err, k),
                    throttles = GetC(thr, k)
                })
                .OrderByDescending(x => x.p95)
                .Take(25)
                .Select(x => new { value = x.key, name = $"{x.key} � p95={x.p95:F0}ms, errors={x.errors}, 429={x.throttles}" })
                .ToList();

                return new { options = rows };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "observability/getoptions failed");
                return new { options = new[] { new { value = "error", name = ex.Message } } };
            }
        }

        // Lightweight options provider for TagSelect. Uses default filters only.
        private object GetObservabilityOptions()
        {
            try
            {
                return GetObservabilitySummary(new Dictionary<string, string>(), null);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Non-critical: Failed to get observability options");
                return new { options = Array.Empty<object>() };
            }
        }

        private string GetObservabilityHtml(IDictionary<string, string> query)
        {
            try
            {
                var window = TimeSpan.FromMinutes(15);
                var lat = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.latency", window);
                var err = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.errors", window);
                var thr = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.429", window);

                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var prov = Get(query, "provider");
                var mod = Get(query, "model");
                string sf(string v) { try { return NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.ProviderMetricsHelper.SanitizeName(v); } catch { return v; } }
                var pf = string.IsNullOrWhiteSpace(prov) ? null : sf(prov);
                var mf = string.IsNullOrWhiteSpace(mod) ? null : sf(mod);
                bool Match(string name) { if (pf != null && !name.Contains($".{pf}.", StringComparison.Ordinal)) return false; if (mf != null && !name.EndsWith($".{mf}", StringComparison.Ordinal)) return false; return true; }

                var keys = new System.Collections.Generic.HashSet<string>();
                foreach (var k in lat.Keys) if (Match(k)) keys.Add(k);
                foreach (var k in err.Keys) if (Match(k)) keys.Add(k);
                foreach (var k in thr.Keys) if (Match(k)) keys.Add(k);

                var rows = new System.Text.StringBuilder();
                rows.AppendLine("<table style='font-family:Segoe UI,Arial,sans-serif;border-collapse:collapse;'>");
                rows.AppendLine("<tr><th style='text-align:left;padding:6px;border-bottom:1px solid #ddd'>Series</th><th style='text-align:right;padding:6px;border-bottom:1px solid #ddd'>p95 (ms)</th><th style='text-align:right;padding:6px;border-bottom:1px solid #ddd'>Errors</th><th style='text-align:right;padding:6px;border-bottom:1px solid #ddd'>429</th></tr>");
                foreach (var k in keys)
                {
                    var series = k.Replace("provider.", string.Empty);
                    var p95 = lat.TryGetValue(k, out var s1) && s1?.Percentiles != null ? s1.Percentiles.P95 : 0;
                    var ec = err.TryGetValue(k, out var s2) ? s2.Count : 0;
                    var tc = thr.TryGetValue(k, out var s3) ? s3.Count : 0;
                    rows.AppendLine($"<tr><td style='padding:6px;border-bottom:1px solid #f0f0f0'>{System.Net.WebUtility.HtmlEncode(series)}</td><td style='text-align:right;padding:6px;border-bottom:1px solid #f0f0f0'>{p95:F0}</td><td style='text-align:right;padding:6px;border-bottom:1px solid #f0f0f0'>{ec}</td><td style='text-align:right;padding:6px;border-bottom:1px solid #f0f0f0'>{tc}</td></tr>");
                }
                rows.AppendLine("</table>");
                return $"<html><body><h3>Observability (last 15m)</h3>{rows}</body></html>";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "observability/html failed");
                return $"<html><body><p>Error generating observability view: {System.Net.WebUtility.HtmlEncode(ex.Message)}</p></body></html>";
            }
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

        private object ApplyApprovalsNow(BrainarrSettings settings, IDictionary<string, string> query)
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

        private object RejectOrNeverSelected(BrainarrSettings settings, IDictionary<string, string> query, ReviewQueueService.ReviewStatus status)
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

        // Library profile retrieval moved to RecommendationCoordinator

        // ====== PRIVATE HELPER METHODS ======

        private async Task<List<Recommendation>> GenerateRecommendationsAsync(BrainarrSettings settings, LibraryProfile libraryProfile)
        {
            return await GenerateRecommendationsAsync(settings, libraryProfile, default);
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
            return provider switch
            {
                AIProvider.OpenAI => BuildEnumOptions<OpenAIModelKind>(),
                AIProvider.Anthropic => BuildEnumOptions<AnthropicModelKind>(),
                AIProvider.Perplexity => BuildEnumOptions<PerplexityModelKind>(),
                AIProvider.OpenRouter => BuildEnumOptions<OpenRouterModelKind>(),
                AIProvider.DeepSeek => BuildEnumOptions<DeepSeekModelKind>(),
                AIProvider.Gemini => BuildEnumOptions<GeminiModelKind>(),
                AIProvider.Groq => BuildEnumOptions<GroqModelKind>(),
                _ => new { options = Array.Empty<object>() }
            };
        }

        private static object BuildEnumOptions<TEnum>() where TEnum : Enum
        {
            var options = Enum.GetValues(typeof(TEnum))
                .Cast<Enum>()
                .Select(v => new { value = v.ToString(), name = FormatEnumName(v.ToString()) })
                .ToList();

            return new { options };
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
                _artistResolver = new NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver(_logger, httpClient: null);
                if (_artistResolver is NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment.ArtistMbidResolver resolver)
                {
                    resolver.AttachSearchService(_artistSearchService);
                }
                _logger.Info("Attached Lidarr artist search to MBID resolver");
            }
        }

        private static string FormatModelName(string modelId)
        {
            return NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatModelName(modelId);
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
