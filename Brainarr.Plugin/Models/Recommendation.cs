using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Models
{
    /// <summary>
    /// Represents a music recommendation from an AI provider.
    /// Converted to record type for immutability and value semantics.
    /// </summary>
    public record Recommendation
    {
        /// <summary>
        /// The artist name.
        /// </summary>
        public string Artist { get; init; } = string.Empty;

        /// <summary>
        /// The album name.
        /// </summary>
        public string Album { get; init; } = string.Empty;

        /// <summary>
        /// The genre (optional).
        /// </summary>
        public string? Genre { get; init; }

        /// <summary>
        /// Confidence score from 0.0 to 1.0.
        /// </summary>
        public double Confidence { get; init; }

        /// <summary>
        /// Reason for the recommendation (optional).
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// Year of release (optional).
        /// </summary>
        public int? Year { get; init; }

        /// <summary>
        /// Release year (alternative property name).
        /// </summary>
        public int? ReleaseYear { get; init; }

        /// <summary>
        /// Source provider that made this recommendation.
        /// </summary>
        public string? Source { get; init; }

        /// <summary>
        /// Provider that made this recommendation.
        /// </summary>
        public string? Provider { get; init; }

        /// <summary>
        /// MusicBrainz ID for the recommendation.
        /// </summary>
        public string? MusicBrainzId { get; init; }

        /// <summary>
        /// Artist MusicBrainz ID.
        /// </summary>
        public string? ArtistMusicBrainzId { get; init; }

        /// <summary>
        /// Album MusicBrainz ID.
        /// </summary>
        public string? AlbumMusicBrainzId { get; init; }

        /// <summary>
        /// Spotify ID.
        /// </summary>
        public string? SpotifyId { get; init; }
    }

    /// <summary>
    /// Library profile information for generating targeted recommendations.
    /// Contains core library statistics and rich metadata for enhanced AI context.
    /// Converted to record type for immutability and value semantics.
    /// </summary>
    public record LibraryProfile
    {
        /// <summary>
        /// Total number of artists in the library.
        /// </summary>
        public int TotalArtists { get; init; }

        /// <summary>
        /// Total number of albums in the library.
        /// </summary>
        public int TotalAlbums { get; init; }

        /// <summary>
        /// Top genres with their counts.
        /// </summary>
        public Dictionary<string, int> TopGenres { get; init; } = new Dictionary<string, int>();

        /// <summary>
        /// Most popular artists in the library.
        /// </summary>
        public List<string> TopArtists { get; init; } = new List<string>();

        /// <summary>
        /// Recently added artists.
        /// </summary>
        public List<string> RecentlyAdded { get; init; } = new List<string>();
        
        /// <summary>
        /// Enhanced metadata for rich library analysis.
        /// Includes genre distribution, temporal patterns, quality metrics, and user preferences.
        /// </summary>
        public Dictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Artist profile for recommendation context.
    /// Converted to record type for immutability and value semantics.
    /// </summary>
    public record ArtistProfile
    {
        /// <summary>
        /// Artist name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Number of albums by this artist.
        /// </summary>
        public int AlbumCount { get; init; }

        /// <summary>
        /// Primary genre.
        /// </summary>
        public string Genre { get; init; } = string.Empty;

        /// <summary>
        /// Whether this artist is heavily represented.
        /// </summary>
        public bool IsHighlyRepresented { get; init; }

        /// <summary>
        /// Genres associated with this artist.
        /// </summary>
        public List<string> Genres { get; init; } = new List<string>();

        /// <summary>
        /// Tags associated with this artist.
        /// </summary>
        public List<string> Tags { get; init; } = new List<string>();
    }

    /// <summary>
    /// Album profile for recommendation context.
    /// Converted to record type for immutability and value semantics.
    /// </summary>
    public record AlbumProfile
    {
        /// <summary>
        /// Album title.
        /// </summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>
        /// Artist name.
        /// </summary>
        public string Artist { get; init; } = string.Empty;

        /// <summary>
        /// Release year.
        /// </summary>
        public int Year { get; init; }

        /// <summary>
        /// Genre.
        /// </summary>
        public string Genre { get; init; } = string.Empty;

        /// <summary>
        /// Whether this is a recent addition.
        /// </summary>
        public bool IsRecentAddition { get; init; }

        /// <summary>
        /// Release date.
        /// </summary>
        public DateTime? ReleaseDate { get; init; }

        /// <summary>
        /// Genres associated with this album.
        /// </summary>
        public List<string> Genres { get; init; } = new List<string>();
    }
}