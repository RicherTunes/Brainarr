using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Models
{
    /// <summary>
    /// Represents a music recommendation from an AI provider.
    /// </summary>
    public class Recommendation
    {
        /// <summary>
        /// The artist name.
        /// </summary>
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// The album name.
        /// </summary>
        public string Album { get; set; } = string.Empty;

        /// <summary>
        /// The genre (optional).
        /// </summary>
        public string? Genre { get; set; }

        /// <summary>
        /// Confidence score from 0.0 to 1.0.
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Reason for the recommendation (optional).
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// Year of release (optional).
        /// </summary>
        public int? Year { get; set; }

        /// <summary>
        /// Release year (alternative property name).
        /// </summary>
        public int? ReleaseYear { get; set; }

        /// <summary>
        /// Source provider that made this recommendation.
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// Provider that made this recommendation.
        /// </summary>
        public string? Provider { get; set; }

        /// <summary>
        /// MusicBrainz ID for the recommendation.
        /// </summary>
        public string? MusicBrainzId { get; set; }

        /// <summary>
        /// Artist MusicBrainz ID.
        /// </summary>
        public string? ArtistMusicBrainzId { get; set; }

        /// <summary>
        /// Album MusicBrainz ID.
        /// </summary>
        public string? AlbumMusicBrainzId { get; set; }

        /// <summary>
        /// Spotify ID.
        /// </summary>
        public string? SpotifyId { get; set; }
    }

    /// <summary>
    /// Library profile information for generating targeted recommendations.
    /// Contains core library statistics and rich metadata for enhanced AI context.
    /// </summary>
    public class LibraryProfile
    {
        /// <summary>
        /// Total number of artists in the library.
        /// </summary>
        public int TotalArtists { get; set; }

        /// <summary>
        /// Total number of albums in the library.
        /// </summary>
        public int TotalAlbums { get; set; }

        /// <summary>
        /// Top genres with their counts.
        /// </summary>
        public Dictionary<string, int> TopGenres { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Most popular artists in the library.
        /// </summary>
        public List<string> TopArtists { get; set; } = new List<string>();

        /// <summary>
        /// Recently added artists.
        /// </summary>
        public List<string> RecentlyAdded { get; set; } = new List<string>();
        
        /// <summary>
        /// Enhanced metadata for rich library analysis.
        /// Includes genre distribution, temporal patterns, quality metrics, and user preferences.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Artist profile for recommendation context.
    /// </summary>
    public class ArtistProfile
    {
        /// <summary>
        /// Artist name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Number of albums by this artist.
        /// </summary>
        public int AlbumCount { get; set; }

        /// <summary>
        /// Primary genre.
        /// </summary>
        public string Genre { get; set; }

        /// <summary>
        /// Whether this artist is heavily represented.
        /// </summary>
        public bool IsHighlyRepresented { get; set; }

        /// <summary>
        /// Genres associated with this artist.
        /// </summary>
        public List<string> Genres { get; set; } = new List<string>();

        /// <summary>
        /// Tags associated with this artist.
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();
    }

    /// <summary>
    /// Album profile for recommendation context.
    /// </summary>
    public class AlbumProfile
    {
        /// <summary>
        /// Album title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Artist name.
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// Release year.
        /// </summary>
        public int Year { get; set; }

        /// <summary>
        /// Genre.
        /// </summary>
        public string Genre { get; set; }

        /// <summary>
        /// Whether this is a recent addition.
        /// </summary>
        public bool IsRecentAddition { get; set; }

        /// <summary>
        /// Release date.
        /// </summary>
        public DateTime? ReleaseDate { get; set; }

        /// <summary>
        /// Genres associated with this album.
        /// </summary>
        public List<string> Genres { get; set; } = new List<string>();
    }
}