using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Models
{
    /// <summary>
    /// Captures normalized style information for the user's library so sampling and prompts
    /// can remain grounded in the listener's actual collection.
    /// </summary>
    public sealed class LibraryStyleContext
    {
        public Dictionary<int, HashSet<string>> ArtistStyles { get; } = new Dictionary<int, HashSet<string>>();

        public Dictionary<int, HashSet<string>> AlbumStyles { get; } = new Dictionary<int, HashSet<string>>();

        public Dictionary<string, int> StyleCoverage { get; private set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> AllStyleSlugs { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> DominantStyles { get; private set; } = Array.Empty<string>();

        public LibraryStyleIndex StyleIndex { get; private set; } = LibraryStyleIndex.Empty;

        public bool HasStyles => AllStyleSlugs.Count > 0;

        public void SetCoverage(Dictionary<string, int> coverage)
        {
            StyleCoverage = coverage ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            AllStyleSlugs.Clear();
            foreach (var key in StyleCoverage.Keys)
            {
                AllStyleSlugs.Add(key);
            }
        }

        public void SetDominantStyles(IEnumerable<string> dominant)
        {
            DominantStyles = dominant?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? Array.Empty<string>();
        }

        public void SetStyleIndex(LibraryStyleIndex index)
        {
            StyleIndex = index ?? LibraryStyleIndex.Empty;
        }
    }

    public sealed class LibrarySampleArtist
    {
        public int ArtistId { get; set; }
        public string Name { get; set; } = string.Empty;
        public HashSet<string> MatchedStyles { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public double MatchScore { get; set; }
        public DateTime? Added { get; set; }
        public double Weight { get; set; }
        public List<LibrarySampleAlbum> Albums { get; } = new List<LibrarySampleAlbum>();
    }

    public sealed class LibrarySampleAlbum
    {
        public int AlbumId { get; set; }
        public int ArtistId { get; set; }
        public string ArtistName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public HashSet<string> MatchedStyles { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public double MatchScore { get; set; }
        public DateTime? Added { get; set; }
        public int? Year { get; set; }
    }

    public sealed class LibrarySample
    {
        public List<LibrarySampleArtist> Artists { get; } = new List<LibrarySampleArtist>();
        public List<LibrarySampleAlbum> Albums { get; } = new List<LibrarySampleAlbum>();

        public int ArtistCount => Artists.Count;
        public int AlbumCount => Albums.Count;
    }

    public sealed class LibraryStyleIndex
    {
        public static LibraryStyleIndex Empty { get; } = new LibraryStyleIndex(
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase));

        public LibraryStyleIndex(
            IReadOnlyDictionary<string, IReadOnlyList<int>> artistsByStyle,
            IReadOnlyDictionary<string, IReadOnlyList<int>> albumsByStyle)
        {
            ArtistsByStyle = artistsByStyle ?? new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
            AlbumsByStyle = albumsByStyle ?? new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, IReadOnlyList<int>> ArtistsByStyle { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<int>> AlbumsByStyle { get; }

        public IReadOnlyList<int> GetArtistsForStyles(IEnumerable<string> styleSlugs)
        {
            return Combine(styleSlugs, ArtistsByStyle);
        }

        public IReadOnlyList<int> GetAlbumsForStyles(IEnumerable<string> styleSlugs)
        {
            return Combine(styleSlugs, AlbumsByStyle);
        }

        private static IReadOnlyList<int> Combine(IEnumerable<string> styleSlugs, IReadOnlyDictionary<string, IReadOnlyList<int>> source)
        {
            if (styleSlugs == null)
            {
                return Array.Empty<int>();
            }

            var set = new SortedSet<int>();
            foreach (var slug in styleSlugs)
            {
                if (string.IsNullOrWhiteSpace(slug)) continue;
                if (source.TryGetValue(slug, out var ids))
                {
                    foreach (var id in ids)
                    {
                        set.Add(id);
                    }
                }
            }

            return set.Count == 0 ? Array.Empty<int>() : set.ToArray();
        }
    }
}
