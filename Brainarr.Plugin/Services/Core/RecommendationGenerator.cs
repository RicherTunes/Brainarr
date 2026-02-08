using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.ImportLists.Brainarr.Performance;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Encapsulates the batch recommendation generation pipeline previously housed
    /// in <see cref="BrainarrOrchestrator"/>.  Handles provider invocation, token
    /// budget management, session deduplication, and result aggregation.
    /// </summary>
    internal sealed class RecommendationGenerator
    {
        private readonly Logger _logger;
        private readonly ProviderLifecycleService _providerLifecycle;
        private readonly ILibraryAnalyzer _libraryAnalyzer;
        private readonly ILibraryAwarePromptBuilder _promptBuilder;
        private readonly IProviderHealthMonitor _providerHealth;
        private readonly IPerformanceMetrics _metrics;
        private readonly IBreakerRegistry _breakerRegistry;
        private readonly IProviderInvoker _providerInvoker;

        // Lightweight shared limiter registry (mirrors the static instance from the orchestrator)
        private static readonly Lazy<ILimiterRegistry> _limiterRegistry = new(() => new LimiterRegistry());

        public RecommendationGenerator(
            Logger logger,
            ProviderLifecycleService providerLifecycle,
            ILibraryAnalyzer libraryAnalyzer,
            ILibraryAwarePromptBuilder promptBuilder,
            IProviderHealthMonitor providerHealth,
            IPerformanceMetrics metrics,
            IBreakerRegistry breakerRegistry,
            IProviderInvoker providerInvoker)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _providerLifecycle = providerLifecycle ?? throw new ArgumentNullException(nameof(providerLifecycle));
            _libraryAnalyzer = libraryAnalyzer ?? throw new ArgumentNullException(nameof(libraryAnalyzer));
            _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
            _providerHealth = providerHealth ?? throw new ArgumentNullException(nameof(providerHealth));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _breakerRegistry = breakerRegistry ?? throw new ArgumentNullException(nameof(breakerRegistry));
            _providerInvoker = providerInvoker ?? throw new ArgumentNullException(nameof(providerInvoker));
        }

        // ------------------------------------------------------------------
        //  Public API
        // ------------------------------------------------------------------

        public Task<List<Recommendation>> GenerateRecommendationsAsync(BrainarrSettings settings, LibraryProfile libraryProfile)
        {
            return GenerateRecommendationsAsync(settings, libraryProfile, default);
        }

        public async Task<List<Recommendation>> GenerateRecommendationsAsync(
            BrainarrSettings settings,
            LibraryProfile libraryProfile,
            CancellationToken cancellationToken)
        {
            if (_providerLifecycle.CurrentProvider == null) throw new InvalidOperationException("Provider not initialized");

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
            var providerName = _providerLifecycle.CurrentProvider.ProviderName;
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
                            samplingScope = SettingScope.Apply(
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
                        async () => await _providerInvoker.InvokeAsync(_providerLifecycle.CurrentProvider, promptRes.Prompt, _logger, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    sw.Stop();

                    lastBatch = batchResult ?? new List<Recommendation>();
                    if (lastBatch.Count == 0)
                    {
                        continue;
                    }

                    _providerHealth.RecordSuccess(providerName, sw.Elapsed.TotalMilliseconds);
                    if (_providerLifecycle.CurrentProvider is Providers.BaseCloudProvider cloud && cloud.LastRateLimitInfo is { } rateLimit)
                    {
                        _providerHealth.RecordRateLimitInfo(providerName, rateLimit.Remaining, rateLimit.ResetAt);
                    }
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

                        var artistName = SessionDeduplication.NormalizeValue(rec.Artist);
                        if (string.IsNullOrWhiteSpace(artistName))
                        {
                            continue;
                        }

                        if (artistMode)
                        {
                            if (seenArtistKeys.Add(artistName))
                            {
                                aggregated.Add(rec);
                                SessionDeduplication.AddExclusion(sessionExclusions, rec.Artist);
                                if (aggregated.Count >= targetCount) break;
                            }
                            continue;
                        }

                        var albumName = SessionDeduplication.NormalizeValue(rec.Album);
                        if (string.IsNullOrWhiteSpace(albumName))
                        {
                            if (seenArtistKeys.Add(artistName))
                            {
                                aggregated.Add(rec);
                                SessionDeduplication.AddExclusion(sessionExclusions, rec.Artist);
                                if (aggregated.Count >= targetCount) break;
                            }
                            continue;
                        }

                        var albumKey = SessionDeduplication.BuildAlbumKey(artistName, albumName);
                        if (string.IsNullOrWhiteSpace(albumKey))
                        {
                            continue;
                        }

                        if (seenAlbumKeys.Add(albumKey))
                        {
                            aggregated.Add(rec);
                            SessionDeduplication.AddExclusion(sessionExclusions, rec.Artist, rec.Album);
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

            if (downgradeSampling && _providerLifecycle.CurrentProvider is GeminiProvider geminiProvider)
            {
                geminiProvider.SetUserMessage("Gemini used balanced sampling to stay within the safe token budget; recommendations may be slightly narrower than comprehensive mode.", BrainarrConstants.DocsGeminiSection);
            }

            LogProviderScoreboard(providerName);
            return aggregated.Count > 0 ? aggregated : lastBatch;
        }

        public List<ImportListItemInfo> ConvertToImportListItems(List<Recommendation> recommendations)
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

        // ------------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------------

        public static IEnumerable<int> BuildBatchPlan(BrainarrSettings settings, int targetCount, bool artistMode)
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

        public static int EstimateCompletionTokens(int count, bool artistMode)
        {
            var perItem = artistMode ? 48 : 64;
            var overhead = artistMode ? 96 : 128;
            return (count * perItem) + overhead;
        }

        public void LogProviderScoreboard(string providerName)
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
    }
}
