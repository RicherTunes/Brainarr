using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class RecommendationPipeline : IRecommendationPipeline
    {
        private readonly Logger _logger;
        private readonly ILibraryAnalyzer _libraryAnalyzer;
        private readonly IDuplicateFilterService _duplicateFilter;
        private readonly IRecommendationValidator _validator;
        private readonly ISafetyGateService _safetyGates;
        private readonly ITopUpPlanner _topUpPlanner;
        private readonly IMusicBrainzResolver _mbidResolver;
        private readonly IArtistMbidResolver _artistResolver;
        private readonly IDuplicationPrevention _duplicationPrevention;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics _metrics;
        private readonly RecommendationHistory _history;
        private readonly IStyleCatalogService _styleCatalog;

        public RecommendationPipeline(
            Logger logger,
            ILibraryAnalyzer libraryAnalyzer,
            IDuplicateFilterService duplicateFilter,
            IRecommendationValidator validator,
            ISafetyGateService safetyGates,
            ITopUpPlanner topUpPlanner,
            IMusicBrainzResolver mbidResolver,
            IArtistMbidResolver artistResolver,
            IDuplicationPrevention duplicationPrevention,
            NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics metrics,
            RecommendationHistory history,
            IStyleCatalogService styleCatalog = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryAnalyzer = libraryAnalyzer ?? throw new ArgumentNullException(nameof(libraryAnalyzer));
            _duplicateFilter = duplicateFilter ?? throw new ArgumentNullException(nameof(duplicateFilter));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _safetyGates = safetyGates ?? throw new ArgumentNullException(nameof(safetyGates));
            _topUpPlanner = topUpPlanner ?? throw new ArgumentNullException(nameof(topUpPlanner));
            _mbidResolver = mbidResolver ?? throw new ArgumentNullException(nameof(mbidResolver));
            _artistResolver = artistResolver ?? throw new ArgumentNullException(nameof(artistResolver));
            _duplicationPrevention = duplicationPrevention ?? throw new ArgumentNullException(nameof(duplicationPrevention));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _styleCatalog = styleCatalog; // Optional for backwards compatibility
        }

        public async Task<List<ImportListItemInfo>> ProcessAsync(
            BrainarrSettings settings,
            List<Recommendation> recommendations,
            LibraryProfile libraryProfile,
            ReviewQueueService reviewQueue,
            IAIProvider currentProvider,
            ILibraryAwarePromptBuilder promptBuilder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Run header: concise one‑liner to frame the run
            var target = Math.Max(1, settings.MaxRecommendations);
            try
            {
                var modelLabel = settings?.ModelSelection ?? settings?.EffectiveModel ?? "";
                var header = $"Start: provider={currentProvider?.ProviderName ?? "?"}, model={modelLabel}, target={target}, mode={settings.RecommendationMode}, sampling={settings.SamplingStrategy}, discovery={settings.DiscoveryMode}";
                if (settings.EnableDebugLogging) _logger.Info(header); else _logger.Debug(header);
            }
            catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log run header"); }

            var allowArtistOnly = settings.RecommendationMode == RecommendationMode.Artists;
            var validationSummary = await ValidateAsync(recommendations, allowArtistOnly, settings.EnableDebugLogging, settings.LogPerItemDecisions, settings.CustomFilterPatterns, settings.EnableStrictValidation).ConfigureAwait(false);
            try
            {
                var msg = $"Validation produced {validationSummary.ValidCount}/{validationSummary.TotalCount} candidates (pre-dedup). Duplicates will be removed next.";
                if (settings.EnableDebugLogging) _logger.Info(msg); else _logger.Debug(msg);
            }
            catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log validation summary"); }
            var validated = _duplicateFilter.FilterExistingRecommendations(validationSummary.ValidRecommendations, allowArtistOnly)
                           ?? validationSummary.ValidRecommendations;
            if (validationSummary.ValidRecommendations.Count != validated.Count)
            {
                var removed = validationSummary.ValidRecommendations.Count - validated.Count;
                if (removed > 0)
                {
                    _logger.Info($"Filtered {removed} candidate(s) already present in the library before enrichment.");
                }
            }

            // Style filtering (if style catalog is available and filters are configured)
            if (_styleCatalog != null && settings.StyleFilters?.Any() == true)
            {
                var preStyleFilter = validated.Count;
                var slugs = _styleCatalog.Normalize(settings.StyleFilters);
                if (slugs.Count > 0)
                {
                    // Style-seeded ("genre-first") discovery: the user asked for styles their library
                    // doesn't contain (e.g. "lo-fi" over a rock library). The prompt already enforces
                    // style membership; the LLM's free-text genre labels are approximate (lo-fi vs
                    // chillhop vs downtempo) and would be wrongly dropped here, gutting the whole point.
                    // So skip the hard drop in that case and trust the prompt.
                    if (IsStyleSeededDiscovery(_styleCatalog, settings, libraryProfile))
                    {
                        _logger.Info("Style-seeded discovery (library lacks the selected styles): skipping post-validation style filter; style membership is enforced at the prompt level.");
                    }
                    else
                    {
                        var relax = settings.RelaxStyleMatching;
                        validated = validated
                            .Where(r =>
                            {
                                // Build genre list from recommendation's genre (may have multiple comma-separated)
                                var genres = new List<string>();
                                if (!string.IsNullOrWhiteSpace(r.Genre))
                                {
                                    genres.AddRange(r.Genre.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(g => g.Trim())
                                        .Where(g => !string.IsNullOrWhiteSpace(g)));
                                }
                                return genres.Count == 0 || _styleCatalog.IsMatch(genres, slugs, relax);
                            })
                            .ToList();

                        var styleFiltered = preStyleFilter - validated.Count;
                        if (styleFiltered > 0)
                        {
                            _logger.Info($"Style filter removed {styleFiltered} candidate(s) not matching selected styles.");
                        }
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested) return new List<ImportListItemInfo>();

            // Enrichment (artist-only vs album mode)
            List<Recommendation> enriched;
            if (settings.RecommendationMode == RecommendationMode.Artists)
            {
                enriched = await _artistResolver.EnrichArtistsAsync(validated, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                enriched = await _mbidResolver.EnrichWithMbidsAsync(validated, cancellationToken).ConfigureAwait(false);
            }

            // Safety gates + review queue integration
            var gated = _safetyGates.ApplySafetyGates(enriched, settings, reviewQueue, _history, _logger, _metrics, cancellationToken);

            // Convert to import items and normalize
            var importItems = ConvertToImportListItems(gated);

            // Drop anything we've already surfaced in prior runs before library/session de-dup
            var preHistory = importItems.Count;
            importItems = _duplicationPrevention.FilterPreviouslyRecommended(importItems) ?? new List<ImportListItemInfo>();
            var postHistory = importItems.Count;
            var historyDropped = preHistory - postHistory;

            // Log dedup stages to clarify pipeline behavior for users
            var preLib = importItems.Count;
            importItems = _duplicateFilter.FilterDuplicates(importItems);
            var postLib = importItems.Count;
            var libraryDropped = preLib - postLib;
            var preSession = importItems.Count;
            importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);
            var postSession = importItems.Count;
            var sessionDropped = preSession - postSession;
            try
            {
                var msg = $"Deduplication summary: pre={preHistory}, history-duplicates removed={historyDropped}, library-duplicates removed={libraryDropped}, session-duplicates removed={sessionDropped}, remaining={postSession}.";
                if (settings.EnableDebugLogging) _logger.Info(msg); else _logger.Debug(msg);
            }
            catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log deduplication summary"); }

            // Iterative top-up if under target and refinement enabled
            if (!cancellationToken.IsCancellationRequested && settings.GetIterationProfile().EnableRefinement && importItems.Count < target)
            {
                var deficit = target - importItems.Count;
                var ip = settings.GetIterationProfile();
                try
                {
                    var msg = $"Top-up starting: under target {importItems.Count}/{target}. Deficit={deficit}. Plan: MaxIter={ip.MaxIterations}, ZeroStop={ip.ZeroStop}, LowStop={ip.LowStop}, AggressiveGuarantee={(ip.GuaranteeExactTarget ? "On" : "Off")}.";
                    if (settings.EnableDebugLogging) _logger.Info(msg); else _logger.Debug(msg);
                }
                catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log top-up start"); }
                var topUp = await _topUpPlanner.TopUpAsync(
                    settings,
                    currentProvider,
                    _libraryAnalyzer,
                    promptBuilder,
                    _duplicationPrevention,
                    libraryProfile,
                    deficit,
                    validationSummary,
                    cancellationToken).ConfigureAwait(false) ?? new List<ImportListItemInfo>();
                if (topUp.Count > 0)
                {
                    var beforeAdd = importItems.Count;
                    importItems.AddRange(topUp);
                    var afterAdd = importItems.Count;
                    importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);
                    importItems = _duplicateFilter.FilterDuplicates(importItems);
                    var afterAll = importItems.Count;
                    try
                    {
                        var msg = $"Top-up applied: added={afterAdd - beforeAdd} candidates; final after de-dup={afterAll}.";
                        if (settings.EnableDebugLogging) _logger.Info(msg); else _logger.Debug(msg);
                    }
                    catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log top-up applied"); }
                }
            }

            // If still under target after (optional) top-up, emit a concise explanation
            if (!cancellationToken.IsCancellationRequested && settings.GetIterationProfile().EnableRefinement && importItems.Count < target)
            {
                try
                {
                    var remaining = target - importItems.Count;
                    _logger.Warn($"Top-up completed with remaining deficit {remaining} (unique candidates limited by duplicates/validation).");
                }
                catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log top-up deficit warning"); }
            }

            // Final summary to make outcomes obvious in logs
            try
            {
                _logger.Info($"Final recommendation count: {importItems.Count}/{target} (after de-dup and optional top-up)");
            }
            catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log final recommendation count"); }

            return importItems;
        }

        /// <summary>
        /// Style-seeded ("genre-first") discovery is when the user selected styles their library does
        /// NOT contain — the recommendations should come from those styles outright, with the library
        /// used only for dedup.
        ///
        /// CRITICAL: this MUST be computed from the exact same signal the prompt renderer uses to
        /// decide genre-first mode (<c>LibraryPromptRenderer</c>: sum of <c>StyleContext.StyleCoverage</c>
        /// over the selected slugs == 0). An earlier version compared against <c>LibraryProfile.TopGenres</c>
        /// with parent-relaxed matching, which could DISAGREE with the renderer — e.g. selecting a
        /// parent style ("rock") over a library that only has a child genre ("art rock"): the renderer
        /// sees coverage["rock"]==0 → genre-first prompt, but parent-relaxed IsMatch matched → the filter
        /// ran and gutted the genre-first results. Reading the same slug-keyed coverage keeps the prompt
        /// mode and the post-filter in lockstep. Pure for testability.
        /// </summary>
        internal static bool IsStyleSeededDiscovery(IStyleCatalogService catalog, BrainarrSettings settings, LibraryProfile profile)
        {
            if (catalog == null || settings?.StyleFilters == null || !settings.StyleFilters.Any())
            {
                return false;
            }

            var slugs = catalog.Normalize(settings.StyleFilters);
            if (slugs.Count == 0)
            {
                // Pure freestyle (no catalog slugs): the outer filter guard already skips on empty
                // Normalize, so this value is only the prompt-consistency signal — freestyle is always
                // genre-first by nature.
                return true;
            }

            var coverage = profile?.StyleContext?.StyleCoverage;
            if (coverage == null || coverage.Count == 0)
            {
                // No library style index at all → genre-first (matches renderer's coverage==0).
                return true;
            }

            var selectedCoverage = slugs.Sum(s => coverage.TryGetValue(s, out var c) ? c : 0);
            return selectedCoverage == 0;
        }

        /// <summary>
        /// Resolves the validator for this run. <c>CustomFilterPatterns</c> / <c>EnableStrictValidation</c>
        /// are per-import-list-definition settings, but the injected <see cref="_validator"/> is a
        /// process-wide singleton constructed without them. When the user configures either, build a
        /// per-run <see cref="RecommendationValidator"/> carrying those settings; otherwise reuse the
        /// injected default (zero behavior change for the common case, and a mock validator stays in
        /// effect under test). Patterns are matched as lowercased substrings (no regex), so there is no
        /// ReDoS/injection surface, and blank entries are dropped in the validator ctor.
        /// </summary>
        internal IRecommendationValidator ResolveValidator(string customPatterns, bool strictMode)
        {
            if (string.IsNullOrWhiteSpace(customPatterns) && !strictMode)
            {
                return _validator;
            }

            return new NzbDrone.Core.ImportLists.Brainarr.Services.RecommendationValidator(_logger, customPatterns, strictMode);
        }

        private Task<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult> ValidateAsync(List<Recommendation> recommendations, bool allowArtistOnly, bool debug = false, bool logPerItem = true, string customPatterns = null, bool strictMode = false)
        {
            _logger.Debug($"Validating {recommendations.Count} recommendations");
            var result = ResolveValidator(customPatterns, strictMode).ValidateBatch(recommendations, allowArtistOnly);
            _logger.Debug($"Validation result: {result.ValidCount}/{result.TotalCount} passed ({result.PassRate:F1}%)");

            try
            {
                if (logPerItem)
                {
                    foreach (var r in result.FilteredRecommendations)
                    {
                        var name = string.IsNullOrWhiteSpace(r.Album) ? r.Artist : $"{r.Artist} - {r.Album}";
                        if (!result.FilterDetails.TryGetValue(name, out var reason)) reason = "filtered";
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Rejected: {name} (conf={r.Confidence:F2}) because {reason}");
                    }
                    if (debug)
                    {
                        foreach (var r in result.ValidRecommendations)
                        {
                            var name = string.IsNullOrWhiteSpace(r.Album) ? r.Artist : $"{r.Artist} - {r.Album}";
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Accepted: {name} (conf={r.Confidence:F2})");
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log validation details"); }

            return Task.FromResult(result);
        }

        private static List<ImportListItemInfo> ConvertToImportListItems(List<Recommendation> recommendations)
        {
            return (recommendations ?? new List<Recommendation>())
                .Select(r => new ImportListItemInfo
                {
                    Artist = r.Artist,
                    Album = r.Album,
                    ArtistMusicBrainzId = string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) ? null : r.ArtistMusicBrainzId,
                    AlbumMusicBrainzId = string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId) ? null : r.AlbumMusicBrainzId,
                    // Guard the year range: DateTime requires [1,9999], but Year comes from untrusted
                    // LLM JSON and is not range-checked upstream. An out-of-range value (0, negative,
                    // 50000, ...) previously threw ArgumentOutOfRangeException here, and the orchestrator's
                    // broad catch then discarded the ENTIRE recommendation batch. Out-of-range -> MinValue.
                    ReleaseDate = (r.Year is int y && y >= 1 && y <= 9999) ? new DateTime(y, 1, 1) : DateTime.MinValue
                })
                .ToList();
        }
    }
}
