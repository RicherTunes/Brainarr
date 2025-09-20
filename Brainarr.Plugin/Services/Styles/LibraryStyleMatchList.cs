using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Styles
{
    internal sealed class LibraryStyleMatchList
    {
        public LibraryStyleMatchList(IReadOnlyList<int>? strictMatches, IReadOnlyList<int>? relaxedMatches)
        {
            var strict = Normalize(strictMatches);
            IReadOnlyList<int> relaxed;

            if (ReferenceEquals(strictMatches, relaxedMatches))
            {
                relaxed = strict;
            }
            else
            {
                relaxed = Normalize(relaxedMatches);
            }

            StrictMatches = strict;
            RelaxedMatches = relaxed;
        }

        public IReadOnlyList<int> StrictMatches { get; }

        public IReadOnlyList<int> RelaxedMatches { get; }

        public bool HasRelaxedMatches => !ReferenceEquals(StrictMatches, RelaxedMatches) && RelaxedMatches.Count > StrictMatches.Count;

        private static IReadOnlyList<int> Normalize(IReadOnlyList<int>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<int>();
            }

            if (source is int[] array)
            {
                return array;
            }

            var copy = new int[source.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
