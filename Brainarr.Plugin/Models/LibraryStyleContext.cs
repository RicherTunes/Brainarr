using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Models
{
    /// <summary>
    /// Captures normalized style information for the user's library so sampling and prompts
    /// can remain grounded in the listener's actual collection.
    /// </summary>
    public class LibraryStyleContext
    {
        /// <summary>
        /// Mapping of artist id to the set of normalized style slugs detected for that artist.
        /// </summary>
        public Dictionary<int, HashSet<string>> ArtistStyles { get; set; } = new Dictionary<int, HashSet<string>>();

        /// <summary>
        /// Mapping of album id to the set of normalized style slugs detected for that album.
        /// </summary>
        public Dictionary<int, HashSet<string>> AlbumStyles { get; set; } = new Dictionary<int, HashSet<string>>();

        /// <summary>
        /// Aggregated coverage counts for each normalized style slug across the library.
        /// Counts include both artist and album level matches to provide a sense of breadth.
        /// </summary>
        public Dictionary<string, int> StyleCoverage { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Convenience set of every style slug observed in the library.
        /// </summary>
        public HashSet<string> AllStyleSlugs { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ordered list of dominant styles discovered in the library. Used for inference when
        /// the user has not explicitly selected styles.
        /// </summary>
        public List<string> DominantStyles { get; set; } = new List<string>();

        /// <summary>
        /// True when at least one normalized style could be extracted from the library.
        /// </summary>
        public bool HasStyles => AllStyleSlugs.Count > 0;
    }

    /// <summary>
    /// Represents an artist selected for inclusion in the prompt sample.
    /// Stores matched styles and sampling metadata to support compression and telemetry.
    /// </summary>
    public class LibrarySampleArtist
    {
        public int ArtistId { get; set; }
        public string Name { get; set; } = string.Empty;
        public HashSet<string> MatchedStyles { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public double MatchScore { get; set; }
        public DateTime? Added { get; set; }
        public double Weight { get; set; }
        public List<LibrarySampleAlbum> Albums { get; set; } = new List<LibrarySampleAlbum>();
    }

    /// <summary>
    /// Represents an album selected for inclusion in the prompt sample.
    /// Tracks artist association, matched styles, and recency metadata for compression.
    /// </summary>
    public class LibrarySampleAlbum
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

    /// <summary>
    /// Container for sampled artists and albums that will seed the prompt.
    /// </summary>
    public class LibrarySample
    {
        public List<LibrarySampleArtist> Artists { get; set; } = new List<LibrarySampleArtist>();
        public List<LibrarySampleAlbum> Albums { get; set; } = new List<LibrarySampleAlbum>();

        public int ArtistCount => Artists.Count;
        public int AlbumCount => Albums.Count;
    }
}
