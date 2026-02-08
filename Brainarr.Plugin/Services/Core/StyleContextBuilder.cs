using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Builds a <see cref="LibraryStyleContext"/> from artist and album collections,
    /// extracting and normalising genre/style slugs via the style catalog.
    /// Supports both sequential and parallel aggregation paths.
    /// </summary>
    internal sealed class StyleContextBuilder
    {
        private readonly IStyleCatalogService _styleCatalog;
        private readonly LibraryAnalyzerOptions _options;
        private readonly Logger _logger;

        public StyleContextBuilder(
            IStyleCatalogService styleCatalog,
            LibraryAnalyzerOptions options,
            Logger logger)
        {
            _styleCatalog = styleCatalog;
            _options = options ?? new LibraryAnalyzerOptions();
            _logger = logger;
        }

        public LibraryStyleContext Build(List<Artist> artists, List<Album> albums)
        {
            var context = new LibraryStyleContext();

            artists ??= new List<Artist>();
            albums ??= new List<Album>();

            if (_styleCatalog == null)
            {
                return context;
            }

            try
            {
                var total = artists.Count + albums.Count;
                var useParallel = _options.EnableParallelStyleContext &&
                                  total >= Math.Max(1, _options.ParallelizationThreshold) &&
                                  total > 1;

                if (useParallel)
                {
                    PopulateStyleContextParallel(context, artists, albums);
                }
                else
                {
                    PopulateStyleContextSequential(context, artists, albums);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Style context extraction failed; continuing without detailed style data");
                return new LibraryStyleContext();
            }

            return context;
        }

        private void PopulateStyleContextSequential(LibraryStyleContext context, List<Artist> artists, List<Album> albums)
        {
            var coverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var artistIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var albumIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var artist in artists)
            {
                var styles = ExtractArtistStyles(artist);
                if (styles.Count == 0)
                {
                    continue;
                }

                context.ArtistStyles[artist.Id] = styles;
                IncrementCoverage(coverage, styles);
                AddToIndex(artistIndex, styles, artist.Id);
            }

            foreach (var album in albums)
            {
                var styles = ExtractAlbumStyles(album);
                if (styles.Count == 0 && album.ArtistId != 0 && context.ArtistStyles.TryGetValue(album.ArtistId, out var fallback))
                {
                    styles = new HashSet<string>(fallback, StringComparer.OrdinalIgnoreCase);
                }

                if (styles.Count == 0)
                {
                    continue;
                }

                context.AlbumStyles[album.Id] = styles;
                IncrementCoverage(coverage, styles);
                AddToIndex(albumIndex, styles, album.Id);
            }

            FinalizeStyleContext(context, coverage, artistIndex, albumIndex);
        }

        private void PopulateStyleContextParallel(
            LibraryStyleContext context,
            List<Artist> artists,
            List<Album> albums,
            CancellationToken cancellationToken = default)
        {
            // Touch LazyLoaded<T> only on the caller thread; do not do this inside Parallel loops.
            var artistPairs = new List<(int Id, HashSet<string> Styles)>(artists.Count);
            foreach (var artist in artists)
            {
                cancellationToken.ThrowIfCancellationRequested();
                artistPairs.Add((artist.Id, ExtractArtistStyles(artist)));
            }

            var albumPairs = new List<(int Id, int ArtistId, HashSet<string> Styles)>(albums.Count);
            foreach (var album in albums)
            {
                cancellationToken.ThrowIfCancellationRequested();
                albumPairs.Add((album.Id, album.ArtistId, ExtractAlbumStyles(album)));
            }

            var artistStyles = new Dictionary<int, HashSet<string>>();
            foreach (var (id, styles) in artistPairs)
            {
                if (styles.Count > 0)
                {
                    artistStyles[id] = styles;
                }
            }

            var albumStyles = new Dictionary<int, HashSet<string>>();
            var coverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var artistIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var albumIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var options = CreateParallelOptions();

            Parallel.ForEach(
                artistPairs,
                options,
                localInit: () => (
                    cov: new Dictionary<string, int>(64, StringComparer.OrdinalIgnoreCase),
                    idx: new Dictionary<string, List<int>>(64, StringComparer.OrdinalIgnoreCase)
                ),
                body: (pair, _, state) =>
                {
                    if (pair.Styles.Count == 0)
                    {
                        return state;
                    }

                    foreach (var style in pair.Styles)
                    {
                        state.cov[style] = state.cov.TryGetValue(style, out var count) ? count + 1 : 1;
                        if (!state.idx.TryGetValue(style, out var list))
                        {
                            list = new List<int>(8);
                            state.idx[style] = list;
                        }

                        list.Add(pair.Id);
                    }

                    return state;
                },
                localFinally: state =>
                {
                    lock (coverage)
                    {
                        foreach (var kvp in state.cov)
                        {
                            coverage[kvp.Key] = coverage.TryGetValue(kvp.Key, out var current) ? current + kvp.Value : kvp.Value;
                        }
                    }

                    lock (artistIndex)
                    {
                        foreach (var kvp in state.idx)
                        {
                            if (!artistIndex.TryGetValue(kvp.Key, out var list))
                            {
                                list = new List<int>(kvp.Value.Count);
                                artistIndex[kvp.Key] = list;
                            }

                            list.AddRange(kvp.Value);
                        }
                    }
                });

            Parallel.ForEach(
                albumPairs,
                options,
                localInit: () => (
                    cov: new Dictionary<string, int>(64, StringComparer.OrdinalIgnoreCase),
                    idx: new Dictionary<string, List<int>>(64, StringComparer.OrdinalIgnoreCase),
                    items: new List<(int Id, HashSet<string> Styles)>()
                ),
                body: (pair, _, state) =>
                {
                    var styles = pair.Styles;
                    if (styles.Count == 0 && pair.ArtistId != 0 && artistStyles.TryGetValue(pair.ArtistId, out var fallback))
                    {
                        styles = fallback;
                    }

                    if (styles.Count == 0)
                    {
                        return state;
                    }

                    state.items.Add((pair.Id, styles));

                    foreach (var style in styles)
                    {
                        state.cov[style] = state.cov.TryGetValue(style, out var count) ? count + 1 : 1;
                        if (!state.idx.TryGetValue(style, out var list))
                        {
                            list = new List<int>(8);
                            state.idx[style] = list;
                        }

                        list.Add(pair.Id);
                    }

                    return state;
                },
                localFinally: state =>
                {
                    lock (albumStyles)
                    {
                        foreach (var (id, styles) in state.items)
                        {
                            albumStyles[id] = styles;
                        }
                    }

                    lock (coverage)
                    {
                        foreach (var kvp in state.cov)
                        {
                            coverage[kvp.Key] = coverage.TryGetValue(kvp.Key, out var current) ? current + kvp.Value : kvp.Value;
                        }
                    }

                    lock (albumIndex)
                    {
                        foreach (var kvp in state.idx)
                        {
                            if (!albumIndex.TryGetValue(kvp.Key, out var list))
                            {
                                list = new List<int>(kvp.Value.Count);
                                albumIndex[kvp.Key] = list;
                            }

                            list.AddRange(kvp.Value);
                        }
                    }
                });

            foreach (var kvp in artistIndex)
            {
                var list = kvp.Value;
                if (list.Count > 1)
                {
                    list.Sort();
                    var write = 1;
                    for (var read = 1; read < list.Count; read++)
                    {
                        if (list[read] != list[write - 1])
                        {
                            list[write++] = list[read];
                        }
                    }

                    if (write != list.Count)
                    {
                        list.RemoveRange(write, list.Count - write);
                    }
                }
            }

            foreach (var kvp in albumIndex)
            {
                var list = kvp.Value;
                if (list.Count > 1)
                {
                    list.Sort();
                    var write = 1;
                    for (var read = 1; read < list.Count; read++)
                    {
                        if (list[read] != list[write - 1])
                        {
                            list[write++] = list[read];
                        }
                    }

                    if (write != list.Count)
                    {
                        list.RemoveRange(write, list.Count - write);
                    }
                }
            }

            foreach (var (id, styles) in artistStyles)
            {
                context.ArtistStyles[id] = styles;
            }

            foreach (var (id, styles) in albumStyles)
            {
                context.AlbumStyles[id] = styles;
            }

            FinalizeStyleContext(context, coverage, artistIndex, albumIndex);
        }

        private ParallelOptions CreateParallelOptions()
        {
            if (_options.MaxDegreeOfParallelism.HasValue && _options.MaxDegreeOfParallelism.Value > 0)
            {
                return new ParallelOptions { MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism.Value };
            }

            return new ParallelOptions();
        }

        private static void FinalizeStyleContext(
            LibraryStyleContext context,
            IDictionary<string, int> coverage,
            IDictionary<string, List<int>> artistIndex,
            IDictionary<string, List<int>> albumIndex)
        {
            var coverageDict = new Dictionary<string, int>(coverage, StringComparer.OrdinalIgnoreCase);
            context.SetCoverage(coverageDict);

            var dominant = coverageDict
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .Select(kvp => kvp.Key);

            context.SetDominantStyles(dominant);

            var artistReadOnly = ConvertIndexToReadOnly(artistIndex);
            var albumReadOnly = ConvertIndexToReadOnly(albumIndex);

            context.SetStyleIndex(new LibraryStyleIndex(artistReadOnly, albumReadOnly));
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<int>> ConvertIndexToReadOnly(IDictionary<string, List<int>> source)
        {
            var result = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in source)
            {
                if (kvp.Value == null || kvp.Value.Count == 0)
                {
                    continue;
                }

                var ordered = kvp.Value.Distinct().ToList();
                ordered.Sort();
                result[kvp.Key] = ordered.ToArray();
            }

            return result;
        }

        private static void IncrementCoverage(IDictionary<string, int> coverage, IEnumerable<string> styles)
        {
            foreach (var style in styles)
            {
                if (string.IsNullOrWhiteSpace(style))
                {
                    continue;
                }

                if (coverage.TryGetValue(style, out var count))
                {
                    coverage[style] = count + 1;
                }
                else
                {
                    coverage[style] = 1;
                }
            }
        }

        private static void AddToIndex(IDictionary<string, List<int>> index, IEnumerable<string> styles, int id)
        {
            foreach (var style in styles)
            {
                if (string.IsNullOrWhiteSpace(style))
                {
                    continue;
                }

                if (!index.TryGetValue(style, out var list))
                {
                    list = new List<int>();
                    index[style] = list;
                }

                list.Add(id);
            }
        }

        private HashSet<string> ExtractArtistStyles(Artist artist)
        {
            if (artist == null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var candidates = new List<string>();

            try
            {
                var metadata = artist.Metadata?.Value;
                if (metadata?.Genres?.Any() == true)
                {
                    candidates.AddRange(metadata.Genres);
                }
            }
            catch
            {
                // Ignore metadata access issues
            }

            return NormalizeStyles(candidates);
        }

        private HashSet<string> ExtractAlbumStyles(Album album)
        {
            if (album == null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var candidates = album.Genres ?? new List<string>();
            return NormalizeStyles(candidates);
        }

        private HashSet<string> NormalizeStyles(IEnumerable<string> values)
        {
            var normalized = _styleCatalog.Normalize(values ?? Array.Empty<string>());
            if (normalized == null || normalized.Count == 0)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(normalized, StringComparer.OrdinalIgnoreCase);
        }
    }
}
