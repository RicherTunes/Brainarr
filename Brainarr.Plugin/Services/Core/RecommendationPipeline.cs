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

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class RecommendationPipeline : IRecommendationPipeline
    {
        private readonly Logger _logger;
        private readonly ILibraryAnalyzer _libraryAnalyzer;
        private readonly IRecommendationValidator _validator;
        private readonly ISafetyGateService _safetyGates;
        private readonly ITopUpPlanner _topUpPlanner;
        private readonly IMusicBrainzResolver _mbidResolver;
        private readonly IArtistMbidResolver _artistResolver;
        private readonly IDuplicationPrevention _duplicationPrevention;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics _metrics;
        private readonly RecommendationHistory _history;

        public RecommendationPipeline(
            Logger logger,
            ILibraryAnalyzer libraryAnalyzer,
            IRecommendationValidator validator,
            ISafetyGateService safetyGates,
            ITopUpPlanner topUpPlanner,
            IMusicBrainzResolver mbidResolver,
            IArtistMbidResolver artistResolver,
            IDuplicationPrevention duplicationPrevention,
            NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics metrics,
            RecommendationHistory history)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryAnalyzer = libraryAnalyzer ?? throw new ArgumentNullException(nameof(libraryAnalyzer));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _safetyGates = safetyGates ?? throw new ArgumentNullException(nameof(safetyGates));
            _topUpPlanner = topUpPlanner ?? throw new ArgumentNullException(nameof(topUpPlanner));
            _mbidResolver = mbidResolver ?? throw new ArgumentNullException(nameof(mbidResolver));
            _artistResolver = artistResolver ?? throw new ArgumentNullException(nameof(artistResolver));
            _duplicationPrevention = duplicationPrevention ?? throw new ArgumentNullException(nameof(duplicationPrevention));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _history = history ?? throw new ArgumentNullException(nameof(history));
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

            var allowArtistOnly = settings.RecommendationMode == RecommendationMode.Artists;
            var validationSummary = await ValidateAsync(recommendations, allowArtistOnly, settings.EnableDebugLogging, settings.LogPerItemDecisions).ConfigureAwait(false);
            var validated = validationSummary.ValidRecommendations;

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
            importItems = _libraryAnalyzer.FilterDuplicates(importItems);
            importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);

            // Iterative top-up if under target and refinement enabled
            var target = Math.Max(1, settings.MaxRecommendations);
            if (!cancellationToken.IsCancellationRequested && settings.EnableIterativeRefinement && importItems.Count < target)
            {
                var deficit = target - importItems.Count;
                var topUp = await _topUpPlanner.TopUpAsync(settings, currentProvider, _libraryAnalyzer, promptBuilder, _duplicationPrevention, libraryProfile, deficit, validationSummary).ConfigureAwait(false);
                if (topUp.Count > 0)
                {
                    importItems.AddRange(topUp);
                    importItems = _duplicationPrevention.DeduplicateRecommendations(importItems);
                    importItems = _libraryAnalyzer.FilterDuplicates(importItems);
                }
            }

            return importItems;
        }

        private async Task<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult> ValidateAsync(List<Recommendation> recommendations, bool allowArtistOnly, bool debug = false, bool logPerItem = true)
        {
            _logger.Debug($"Validating {recommendations.Count} recommendations");
            var result = _validator.ValidateBatch(recommendations, allowArtistOnly);
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
            catch { }

            return await Task.FromResult(result);
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
                    ReleaseDate = r.Year.HasValue ? new DateTime(r.Year.Value, 1, 1) : DateTime.MinValue
                })
                .ToList();
        }
    }
}
