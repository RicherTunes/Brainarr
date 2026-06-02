using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;

// Use Services namespace explicitly for clarity (avoids confusion with Services.Validation types)
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class TopUpPlanner : ITopUpPlanner
    {
        private readonly Logger _logger;
        private readonly IDuplicateFilterService _duplicateFilter;

        public TopUpPlanner(Logger logger, IDuplicateFilterService duplicateFilter)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _duplicateFilter = duplicateFilter ?? throw new ArgumentNullException(nameof(duplicateFilter));
        }

        public async Task<List<ImportListItemInfo>> TopUpAsync(
            BrainarrSettings settings,
            IAIProvider provider,
            ILibraryAnalyzer libraryAnalyzer,
            ILibraryAwarePromptBuilder promptBuilder,
            IDuplicationPrevention duplicationPrevention,
            LibraryProfile libraryProfile,
            int needed,
            NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult? initialValidation,
            CancellationToken cancellationToken,
            IReadOnlyList<ImportListItemInfo>? alreadyAccepted = null,
            IArtistMbidResolver? artistResolver = null)
        {
            var importItems = new List<ImportListItemInfo>();
            if (provider == null)
            {
                _logger.Warn("Top-up requested without an active provider");
                return importItems;
            }

            // Inherit/align timeout behavior with the initial provider call.
            // For local providers (Ollama/LM Studio), if the configured timeout is near the
            // conservative default, elevate to a more realistic ceiling to accommodate
            // large prompts and slow first-token latency.
            var isLocal = settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio;
            var requested = settings.AIRequestTimeoutSeconds;
            var effectiveTimeout = (isLocal && requested <= Configuration.BrainarrConstants.DefaultAITimeout)
                ? Configuration.BrainarrConstants.LocalProviderDefaultTimeout
                : requested;
            if (settings.EnableDebugLogging)
            {
                try { _logger.Info($"[Brainarr Debug] Top-Up Effective timeout: {effectiveTimeout}s"); }
                catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log top-up timeout"); }
            }

            var strategy = new IterativeRecommendationStrategy(_logger, promptBuilder, new ProviderInvoker());

            // Temporarily adjust MaxRecommendations to the deficit only for this top-up
            using var _maxScope = SettingScope.Apply(
                getter: () => settings.MaxRecommendations,
                setter: v => settings.MaxRecommendations = v,
                newValue: Math.Max(1, needed));

            // Push timeout + output-token budget AFTER scoping MaxRecommendations to the deficit so the
            // token budget scales to the smaller top-up request rather than the full target.
            using var _timeoutScope = TimeoutContext.Push(effectiveTimeout, settings.GetOutputTokenBudget());

            try
            {
                var shouldRecommendArtists = settings.RecommendationMode == RecommendationMode.Artists;
                var allArtists = libraryAnalyzer.GetAllArtists();
                var allAlbums = libraryAnalyzer.GetAllAlbums();

                try
                {
                    var msg = $"Top-Up Planner: need={needed}, mode={(shouldRecommendArtists ? "artists" : "albums")}, library sizes: artists={allArtists?.Count ?? 0}, albums={allAlbums?.Count ?? 0}";
                    if (settings.EnableDebugLogging) _logger.Info(msg); else _logger.Debug(msg);
                }
                catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log top-up planner state"); }

                // T1: the recommendations already delivered to the user in this run (the initial batch).
                // Threaded into the strategy so the top-up prompt's [[SYSTEM_AVOID]] + dedup baseline
                // exclude them — otherwise the provider re-emits delivered artists and the iteration is
                // wasted (they are dropped post-hoc). Bounded by MaxRecommendations.
                var priorRecs = (alreadyAccepted ?? Array.Empty<ImportListItemInfo>())
                    .Where(i => i != null && !string.IsNullOrWhiteSpace(i.Artist))
                    .Select(i => new Recommendation { Artist = i.Artist, Album = i.Album ?? string.Empty })
                    .ToList();

                var topUpRecs = await strategy.GetIterativeRecommendationsAsync(
                    provider,
                    libraryProfile,
                    allArtists,
                    allAlbums,
                    settings,
                    shouldRecommendArtists,
                    initialValidation?.FilterReasons,
                    initialValidation?.FilteredRecommendations,
                    // For top-up, prefer aggressive guarantee to actually fill deficits
                    aggressiveGuarantee: true,
                    alreadyProvided: priorRecs,
                    cancellationToken: cancellationToken);

                if (topUpRecs != null && topUpRecs.Count > 0)
                {
                    var suggested = topUpRecs.Count;
                    var noMbidDropped = 0;
                    var enrichmentDropped = 0;
                    // Enforce MBID requirement in artist-mode for top-ups as well.
                    if (shouldRecommendArtists && settings.RequireMbids)
                    {
                        // T2: top-up recs arrive straight from the provider WITHOUT MBIDs and are never
                        // enrichment-resolved elsewhere on the top-up path (the initial-batch enrichment
                        // at RecommendationPipeline runs BEFORE top-up). Resolve them here first, reusing
                        // the SAME resolver as the initial batch (injected, not a parallel impl), so a
                        // resolvable artist survives the require-MBID filter instead of being dropped
                        // wholesale — the gap that made top-up contribute 0 under RequireMbids. The
                        // resolver carries its own rate-limit/LRU cache; the input is bounded by the
                        // deficit, so this cannot loop or grow unbounded.
                        if (artistResolver != null)
                        {
                            topUpRecs = await artistResolver.EnrichArtistsAsync(topUpRecs, cancellationToken).ConfigureAwait(false);
                        }
                        var beforeMbidFilter = topUpRecs.Count;
                        // Enrichment itself can drop a rec (e.g. a blank artist name), so account for that
                        // separately to keep the summary identity exact (suggested == sum of all drops + returned).
                        enrichmentDropped = suggested - beforeMbidFilter;
                        topUpRecs = topUpRecs.Where(r => !string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId)).ToList();
                        // Genuinely-unresolvable artists still drop (real gate, not a bug) — but the drop
                        // is now ATTRIBUTED in the summary below instead of silently lost.
                        noMbidDropped = beforeMbidFilter - topUpRecs.Count;
                    }

                    importItems.AddRange(topUpRecs
                        .Select(r => new ImportListItemInfo
                        {
                            Artist = r.Artist,
                            Album = r.Album,
                            ArtistMusicBrainzId = string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) ? null : r.ArtistMusicBrainzId,
                            AlbumMusicBrainzId = string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId) ? null : r.AlbumMusicBrainzId,
                            // Guard year range [1,9999]; untrusted LLM Year out of range previously threw
                            // ArgumentOutOfRangeException, aborting the top-up batch.
                            ReleaseDate = (r.Year is int y && y >= 1 && y <= 9999) ? new DateTime(y, 1, 1) : DateTime.MinValue
                        }));

                    var preHistory = importItems.Count;
                    importItems = duplicationPrevention.FilterPreviouslyRecommended(importItems) ?? new List<ImportListItemInfo>();
                    var postHistory = importItems.Count;
                    var historyDropped = preHistory - postHistory;

                    var preSession = importItems.Count;
                    importItems = importItems
                        .GroupBy(i => new
                        {
                            Artist = NormalizeTopUpKey(i?.Artist),
                            Album = NormalizeTopUpKey(i?.Album)
                        })
                        .Select(g => g.First())
                        .ToList();
                    var postSession = importItems.Count;
                    var sessionDropped = preSession - postSession;

                    var preLibrary = importItems.Count;
                    importItems = _duplicateFilter.FilterDuplicates(importItems) ?? new List<ImportListItemInfo>();
                    var libraryDropped = preLibrary - importItems.Count;
                    var after = importItems.Count;
                    try
                    {
                        // Accounting reconciles exactly: suggested = enrichment-dropped + no-MBID + history + session + library + returned.
                        var msg = $"Top-Up Planner summary: provider suggested={suggested}, enrichment-dropped={enrichmentDropped}, no-MBID removed={noMbidDropped}, history-duplicates removed={historyDropped}, session-duplicates removed={sessionDropped}, library-duplicates removed={libraryDropped}, returned={after}";
                        if (settings.EnableDebugLogging) _logger.Info(msg); else _logger.Debug(msg);
                    }
                    catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log top-up planner summary"); }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The RUN was cancelled — propagate so the cancellation-aware orchestrator path maps
                // it to a cancelled fetch. Without this guard the broad catch below re-swallows the
                // OperationCanceledException that IterativeRecommendationStrategy now re-throws,
                // returning a partial list as if the run succeeded (the intermediate re-swallow an
                // adversarial review flagged). A provider's OWN request timeout (run token NOT
                // cancelled) still falls through to the broad catch as a recoverable failure.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Top-up iteration failed; returning collected items so far");
            }

            return importItems;
        }

        private static string NormalizeTopUpKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var decoded = WebUtility.HtmlDecode(value);
            return System.Text.RegularExpressions.Regex.Replace(decoded.Trim(), @"\s+", " ").ToLowerInvariant();
        }
    }
}
