using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Styles
{
    /// <summary>
    /// Normalizes the selected style slugs and appends adjacent expansions so ExpandedSlugs retains the selected order first
    /// followed by additional styles.
    /// </summary>
    internal sealed class LibraryStyleSelection
    {
        private static readonly StringComparer SlugComparer = StringComparer.OrdinalIgnoreCase;

        private readonly bool _hasRelaxationExpansion;

        public LibraryStyleSelection(
            IEnumerable<string>? selectedSlugs,
            IEnumerable<string>? expandedSlugs,
            bool relaxAdjacentStyles)
        {
            var selectedList = CreateOrderedSlugList(selectedSlugs);
            var expandedList = new List<string>(selectedList);
            var expandedSet = new HashSet<string>(selectedList, SlugComparer);

            if (expandedSlugs != null)
            {
                foreach (var slug in expandedSlugs)
                {
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        continue;
                    }

                    if (expandedSet.Add(slug))
                    {
                        expandedList.Add(slug);
                    }
                }
            }

            SelectedSlugs = selectedList.Count == 0
                ? Array.Empty<string>()
                : selectedList.ToArray();

            ExpandedSlugs = expandedList.Count == 0
                ? Array.Empty<string>()
                : expandedList.ToArray();

            RelaxAdjacentStyles = relaxAdjacentStyles;
            _hasRelaxationExpansion = ExpandedSlugs.Count > SelectedSlugs.Count;
        }

        public IReadOnlyList<string> SelectedSlugs { get; }

        public IReadOnlyList<string> ExpandedSlugs { get; }

        public bool RelaxAdjacentStyles { get; }

        public bool ShouldUseRelaxedMatches => RelaxAdjacentStyles && _hasRelaxationExpansion;

        private static List<string> CreateOrderedSlugList(IEnumerable<string>? source)
        {
            var list = new List<string>();
            if (source == null)
            {
                return list;
            }

            var seen = new HashSet<string>(SlugComparer);
            foreach (var slug in source)
            {
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                if (seen.Add(slug))
                {
                    list.Add(slug);
                }
            }

            return list;
        }
    }
}
