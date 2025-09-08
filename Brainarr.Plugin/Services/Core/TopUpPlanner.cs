using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;

// Use Services namespace explicitly for clarity (avoids confusion with Services.Validation types)
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

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
            NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult? initialValidation)
        {
            var importItems = new List<ImportListItemInfo>();
            if (provider == null)
            {
                _logger.Warn("Top-up requested without an active provider");
                return importItems;
            }

            var strategy = new IterativeRecommendationStrategy(_logger, promptBuilder);

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
                    aggressiveGuarantee: true);

                if (topUpRecs != null && topUpRecs.Count > 0)
                {
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

                    importItems = duplicationPrevention.DeduplicateRecommendations(importItems);
                    importItems = libraryAnalyzer.FilterDuplicates(importItems);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Top-up iteration failed; returning collected items so far");
            }

            return importItems;
        }
    }
}
