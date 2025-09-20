using System;
using System.Collections.Generic;
using System.Threading;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Styles
{
    internal sealed class LibraryStyleIndex
    {
        private static readonly StringComparer SlugComparer = StringComparer.OrdinalIgnoreCase;
        private const int MaxMatchesPerStyle = 500;

        private readonly Dictionary<string, int[]> _artistMatches;
        private readonly Dictionary<string, int[]> _albumMatches;
        private readonly Logger? _logger;

        public LibraryStyleIndex(
            IDictionary<string, IEnumerable<int>>? artistMatches,
            IDictionary<string, IEnumerable<int>>? albumMatches,
            Logger? logger = null)
        {
            _logger = logger;
            _artistMatches = BuildLookup(artistMatches);
            _albumMatches = BuildLookup(albumMatches);
        }

        public IReadOnlyList<int> GetArtistMatches(IEnumerable<string>? slugs, CancellationToken cancellationToken = default)
        {
            return ResolveMatches(slugs, _artistMatches, cancellationToken);
        }

        public IReadOnlyList<int> GetAlbumMatches(IEnumerable<string>? slugs, CancellationToken cancellationToken = default)
        {
            return ResolveMatches(slugs, _albumMatches, cancellationToken);
        }

        private Dictionary<string, int[]> BuildLookup(IDictionary<string, IEnumerable<int>>? source)
        {
            var lookup = new Dictionary<string, int[]>(SlugComparer);
            if (source == null)
            {
                return lookup;
            }

            foreach (var kvp in source)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    continue;
                }

                lookup[kvp.Key] = CreateOrderedIdArray(kvp.Key, kvp.Value);
            }

            return lookup;
        }

        private int[] CreateOrderedIdArray(string slug, IEnumerable<int>? ids)
        {
            if (ids == null)
            {
                return Array.Empty<int>();
            }

            var seen = new HashSet<int>();
            var ordered = new List<int>();

            foreach (var id in ids)
            {
                if (seen.Add(id))
                {
                    ordered.Add(id);
                    if (ordered.Count == MaxMatchesPerStyle)
                    {
                        _logger?.Debug(
                            "Style '{0}' matches truncated at {1} items to limit relaxed expansion.",
                            slug,
                            MaxMatchesPerStyle);
                        break;
                    }
                }
            }

            return ordered.Count == 0 ? Array.Empty<int>() : ordered.ToArray();
        }

        private static IReadOnlyList<int> ResolveMatches(
            IEnumerable<string>? slugs,
            Dictionary<string, int[]> lookup,
            CancellationToken cancellationToken)
        {
            if (slugs == null)
            {
                return Array.Empty<int>();
            }

            var perSlugMatches = new List<int[]>();
            var estimated = 0;

            foreach (var slug in slugs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                if (lookup.TryGetValue(slug, out var matches) && matches.Length > 0)
                {
                    perSlugMatches.Add(matches);
                    estimated += matches.Length;
                }
            }

            if (estimated == 0)
            {
                return Array.Empty<int>();
            }

            var seen = new HashSet<int>(estimated);
            var ordered = new List<int>(estimated);

            foreach (var matches in perSlugMatches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var id in matches)
                {
                    if (seen.Add(id))
                    {
                        ordered.Add(id);
                    }
                }
            }

            return ordered.Count == 0 ? Array.Empty<int>() : ordered.ToArray();
        }
    }
}
