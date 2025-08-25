using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Analysis;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Refactored LibraryAnalyzer using decomposed analysis services.
    /// Orchestrates multiple specialized analyzers for comprehensive library profiling.
    /// </summary>
    public class LibraryAnalyzerRefactored : ILibraryAnalyzer
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly ILibraryMetadataAnalyzer _metadataAnalyzer;
        private readonly ITemporalAnalyzer _temporalAnalyzer;
        private readonly ICollectionDepthAnalyzer _depthAnalyzer;
        private readonly ILibraryProfileBuilder _profileBuilder;
        private readonly Logger _logger;

        public LibraryAnalyzerRefactored(
            IArtistService artistService,
            IAlbumService albumService,
            ILibraryMetadataAnalyzer metadataAnalyzer,
            ITemporalAnalyzer temporalAnalyzer,
            ICollectionDepthAnalyzer depthAnalyzer,
            ILibraryProfileBuilder profileBuilder,
            Logger logger)
        {
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            _metadataAnalyzer = metadataAnalyzer ?? throw new ArgumentNullException(nameof(metadataAnalyzer));
            _temporalAnalyzer = temporalAnalyzer ?? throw new ArgumentNullException(nameof(temporalAnalyzer));
            _depthAnalyzer = depthAnalyzer ?? throw new ArgumentNullException(nameof(depthAnalyzer));
            _profileBuilder = profileBuilder ?? throw new ArgumentNullException(nameof(profileBuilder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Analyzes the music library using specialized analysis services.
        /// </summary>
        public LibraryProfile AnalyzeLibrary()
        {
            try
            {
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();

                // Run analysis tasks in parallel for performance
                var analysisTask = Task.Run(async () =>
                {
                    var genreTask = _metadataAnalyzer.AnalyzeGenresAsync(artists, albums);
                    var temporalAnalysis = _temporalAnalyzer.AnalyzeTemporalPatterns(albums);
                    var depthAnalysis = _depthAnalyzer.AnalyzeDepth(artists, albums);
                    var qualityMetrics = _depthAnalyzer.AnalyzeQuality(artists, albums);

                    var genres = await genreTask;

                    return await _profileBuilder.BuildProfileAsync(
                        genres,
                        temporalAnalysis,
                        depthAnalysis,
                        qualityMetrics,
                        artists,
                        albums);
                });

                return analysisTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to analyze library: {ex.Message}");
                return GetFallbackProfile();
            }
        }

        /// <summary>
        /// Builds an AI prompt using the profile builder service.
        /// </summary>
        public string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode)
        {
            return _profileBuilder.BuildPrompt(profile, maxRecommendations, discoveryMode);
        }

        /// <summary>
        /// Filters duplicate recommendations using optimized matching.
        /// </summary>
        public List<ImportListItemInfo> FilterDuplicates(List<ImportListItemInfo> recommendations)
        {
            var existingAlbums = _albumService.GetAllAlbums();
            var existingArtists = _artistService.GetAllArtists();

            // Build optimized lookup set
            var albumKeys = BuildAlbumKeySet(existingAlbums, existingArtists);

            var uniqueItems = new List<ImportListItemInfo>();
            var duplicatesFound = 0;

            foreach (var item in recommendations)
            {
                if (!IsDuplicate(item, albumKeys))
                {
                    uniqueItems.Add(item);
                }
                else
                {
                    duplicatesFound++;
                }
            }

            if (duplicatesFound > 0)
            {
                _logger.Info($"Filtered {duplicatesFound} duplicate recommendations");
            }

            return uniqueItems;
        }

        private HashSet<string> BuildAlbumKeySet(List<Album> albums, List<Artist> artists)
        {
            var albumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var artistLookup = artists.ToDictionary(a => a.Id, a => a.Name);

            foreach (var album in albums)
            {
                if (artistLookup.TryGetValue(album.ArtistId, out var artistName))
                {
                    // Add multiple key variations for robust matching
                    albumKeys.Add($"{artistName}_{album.Title}");
                    albumKeys.Add($"{artistName.Replace(" ", "")}_{album.Title?.Replace(" ", "")}");

                    // Handle "The" prefix variations
                    if (artistName.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                    {
                        var nameWithoutThe = artistName.Substring(4);
                        albumKeys.Add($"{nameWithoutThe}_{album.Title}");
                    }
                }
            }

            return albumKeys;
        }

        private bool IsDuplicate(ImportListItemInfo item, HashSet<string> albumKeys)
        {
            var keys = new[]
            {
                $"{item.Artist}_{item.Album}",
                $"{item.Artist?.Replace(" ", "")}_{item.Album?.Replace(" ", "")}",
                item.Artist?.StartsWith("The ", StringComparison.OrdinalIgnoreCase) == true
                    ? $"{item.Artist.Substring(4)}_{item.Album}"
                    : null
            }.Where(k => k != null);

            return keys.Any(key => albumKeys.Contains(key));
        }

        private LibraryProfile GetFallbackProfile()
        {
            return new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = GetFallbackGenres(),
                TopArtists = new List<string> { "Radiohead", "Pink Floyd", "Miles Davis" },
                RecentlyAdded = new List<string>(),
                Metadata = GetFallbackMetadata()
            };
        }

        private Dictionary<string, int> GetFallbackGenres()
        {
            var genreCounts = new Dictionary<string, int>();
            var fallbackGenres = new[] { "Rock", "Electronic", "Jazz", "Indie", "Pop" };
            
            for (int i = 0; i < fallbackGenres.Length; i++)
            {
                genreCounts[fallbackGenres[i]] = 20 - (i * 3);
            }
            
            return genreCounts;
        }

        private Dictionary<string, object> GetFallbackMetadata()
        {
            return new Dictionary<string, object>
            {
                ["GenreDistribution"] = new Dictionary<string, double>
                {
                    { "Rock", 30.0 },
                    { "Electronic", 20.0 },
                    { "Jazz", 15.0 }
                },
                ["CollectionSize"] = "growing",
                ["CollectionFocus"] = "eclectic-mixed",
                ["DiscoveryTrend"] = "stable collection",
                ["ReleaseDecades"] = new List<string> { "2010s", "2000s", "1990s" },
                ["PreferredEras"] = new List<string> { "Modern", "Contemporary" },
                ["NewReleaseRatio"] = 0.15,
                ["MonitoredRatio"] = 0.8,
                ["CollectionCompleteness"] = 0.7,
                ["AverageAlbumsPerArtist"] = 5.0,
                ["AlbumTypes"] = new Dictionary<string, int>
                {
                    { "Album", 400 },
                    { "EP", 50 },
                    { "Single", 50 }
                }
            };
        }
    }
}