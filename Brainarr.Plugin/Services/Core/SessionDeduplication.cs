using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Pure static utilities for session-level recommendation deduplication.
    /// Normalizes artist/album names and manages exclusion sets.
    /// Extracted from BrainarrOrchestrator (M6-3).
    /// </summary>
    internal static class SessionDeduplication
    {
        public static void AddExclusion(HashSet<string> sessionExclusions, string artist, string album = null)
        {
            if (sessionExclusions == null) return;

            var label = string.IsNullOrWhiteSpace(album)
                ? NormalizeValue(artist)
                : BuildAlbumLabel(artist, album);

            if (!string.IsNullOrWhiteSpace(label))
            {
                sessionExclusions.Add(label);
            }
        }

        public static string BuildAlbumKey(string normalizedArtist, string normalizedAlbum)
        {
            if (string.IsNullOrWhiteSpace(normalizedArtist) || string.IsNullOrWhiteSpace(normalizedAlbum))
            {
                return string.Empty;
            }

            return $"{normalizedArtist}::{normalizedAlbum}";
        }

        public static string NormalizeValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var decoded = System.Net.WebUtility.HtmlDecode(value).Trim();
            return System.Text.RegularExpressions.Regex.Replace(decoded, "\\s+", " ");
        }

        public static string BuildAlbumLabel(string artist, string album)
        {
            var artistLabel = NormalizeValue(artist);
            var albumLabel = NormalizeValue(album);

            if (string.IsNullOrWhiteSpace(albumLabel))
            {
                return artistLabel;
            }

            if (string.IsNullOrWhiteSpace(artistLabel))
            {
                return albumLabel;
            }

            return $"{artistLabel} - {albumLabel}";
        }
    }
}
