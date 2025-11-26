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

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class TopUpPlanner : ITopUpPlanner
    {
        private readonly Logger _logger;

        public TopUpPlanner(Logger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
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
            CancellationToken cancellationToken)
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
            using var _timeoutScope = TimeoutContext.Push(effectiveTimeout);

            var strategy = new IterativeRecommendationStrategy(_logger, promptBuilder, new ProviderInvoker());

            // Temporarily adjust MaxRecommendations to the deficit only for this top-up
            using var _maxScope = SettingScope.Apply(
                getter: () => settings.MaxRecommendations,
                setter: v => settings.MaxRecommendations = v,
                newValue: Math.Max(1, needed));

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
                    cancellationToken: cancellationToken);

                if (topUpRecs != null && topUpRecs.Count > 0)
                {
                    var suggested = topUpRecs.Count;
                    // Enforce MBID requirement in artist-mode for top-ups as well
                    if (shouldRecommendArtists && settings.RequireMbids)
                    {
                        topUpRecs = topUpRecs.Where(r => !string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId)).ToList();
                    }

                    importItems.AddRange(topUpRecs
                        .Select(r => new ImportListItemInfo
                        {
                            Artist = r.Artist,
                            Album = r.Album,
                            ArtistMusicBrainzId = string.IsNullOrWhiteSpace(r.ArtistMusicBrainzId) ? null : r.ArtistMusicBrainzId,
                            AlbumMusicBrainzId = string.IsNullOrWhiteSpace(r.AlbumMusicBrainzId) ? null : r.AlbumMusicBrainzId,
                            ReleaseDate = r.Year.HasValue ? new DateTime(r.Year.Value, 1, 1) : DateTime.MinValue
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

                    importItems = libraryAnalyzer.FilterDuplicates(importItems) ?? new List<ImportListItemInfo>();
                    var after = importItems.Count;
                    try
                    {
                        var msg = $"Top-Up Planner summary: provider suggested={suggested}, history-duplicates removed={historyDropped}, session-duplicates removed={sessionDropped}, returned={after}";
                        if (settings.EnableDebugLogging) _logger.Info(msg); else _logger.Debug(msg);
                    }
                    catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log top-up planner summary"); }
                }
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

            return WebUtility.HtmlDecode(value).Trim().ToLowerInvariant();
        }
    }
}
