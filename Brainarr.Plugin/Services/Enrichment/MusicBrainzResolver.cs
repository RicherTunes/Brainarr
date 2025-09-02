using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment
{
    /// <summary>
    /// Resolves MusicBrainz artist and album MBIDs for album recommendations.
    /// Tries to find a matching release-group for the given artist + album.
    /// Returns the original list when resolution fails (caller may filter).
    /// </summary>
    public class MusicBrainzResolver
    {
        private const string BaseUrl = "https://musicbrainz.org/ws/2";
        private readonly HttpClient _httpClient;
        private readonly Logger _logger;

        public MusicBrainzResolver(Logger logger, HttpClient httpClient = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? new HttpClient();
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Brainarr/1.0 (https://github.com/RicherTunes/Brainarr)");
            }
        }

        public async Task<List<Recommendation>> EnrichWithMbidsAsync(List<Recommendation> recommendations, CancellationToken ct = default)
        {
            if (recommendations == null || recommendations.Count == 0)
                return new List<Recommendation>();

            var result = new List<Recommendation>(recommendations.Count);

            foreach (var rec in recommendations)
            {
                if (ct.IsCancellationRequested) break;

                if (string.IsNullOrWhiteSpace(rec.Artist) || string.IsNullOrWhiteSpace(rec.Album))
                {
                    result.Add(rec);
                    continue;
                }

                try
                {
                    var enriched = await ResolveAlbumAsync(rec, ct);
                    result.Add(enriched ?? rec);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Album MBID resolution error for '{rec.Artist} - {rec.Album}'");
                    result.Add(rec);
                }

                // Be nice to MusicBrainz
                await Task.Delay(200, ct);
            }

            _logger.Info($"Album MBID resolution pass complete: {result.Count}/{recommendations.Count} processed");
            return result;
        }

        private async Task<Recommendation> ResolveAlbumAsync(Recommendation rec, CancellationToken ct)
        {
            // 1) Resolve artist ID (MBID)
            string artistId = rec.ArtistMusicBrainzId;
            if (string.IsNullOrWhiteSpace(artistId))
            {
                artistId = await FindArtistId(rec.Artist, ct);
            }

            // 2) Resolve release-group (album) by title and artist
            string albumRgId = await FindReleaseGroupId(rec.Album, artistId, rec.Artist, ct);

            if (artistId == null && albumRgId == null)
                return null;

            return new Recommendation
            {
                Artist = rec.Artist,
                Album = rec.Album,
                Genre = rec.Genre,
                Confidence = rec.Confidence,
                Reason = rec.Reason,
                Year = rec.Year,
                ReleaseYear = rec.ReleaseYear,
                ArtistMusicBrainzId = artistId ?? rec.ArtistMusicBrainzId,
                AlbumMusicBrainzId = albumRgId ?? rec.AlbumMusicBrainzId,
                MusicBrainzId = albumRgId ?? rec.MusicBrainzId,
                Source = rec.Source,
                Provider = rec.Provider,
                SpotifyId = rec.SpotifyId
            };
        }

        private async Task<string> FindArtistId(string artist, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(artist)) return null;
            var url = $"{BaseUrl}/artist/?query={Uri.EscapeDataString(artist)}&fmt=json&limit=5";
            using var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var content = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("artists", out var artistsElem) || artistsElem.ValueKind != JsonValueKind.Array) return null;

            string bestId = null; int bestScore = -1;
            foreach (var a in artistsElem.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var id = a.TryGetProperty("id", out var i) ? i.GetString() : null;
                var score = a.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var exact = string.Equals(name, artist, StringComparison.OrdinalIgnoreCase);
                var effective = score + (exact ? 50 : 0);
                if (effective > bestScore) { bestScore = effective; bestId = id; }
            }
            return bestId;
        }

        private async Task<string> FindReleaseGroupId(string album, string artistId, string artistName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(album)) return null;
            // Prefer artistId when available for precision
            var query = !string.IsNullOrWhiteSpace(artistId)
                ? $"release-group/?query=artistid:{artistId}%20AND%20releasegroup:{Uri.EscapeDataString(album)}&fmt=json&limit=5"
                : $"release-group/?query=artist:{Uri.EscapeDataString(artistName ?? string.Empty)}%20AND%20releasegroup:{Uri.EscapeDataString(album)}&fmt=json&limit=5";
            var url = $"{BaseUrl}/{query}";
            using var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var content = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("release-groups", out var rgsElem) || rgsElem.ValueKind != JsonValueKind.Array) return null;

            string bestId = null; int bestScore = -1;
            foreach (var rg in rgsElem.EnumerateArray())
            {
                var title = rg.TryGetProperty("title", out var t) ? t.GetString() : null;
                var id = rg.TryGetProperty("id", out var i) ? i.GetString() : null;
                var score = rg.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var exact = string.Equals(title, album, StringComparison.OrdinalIgnoreCase);
                var effective = score + (exact ? 50 : 0);
                if (effective > bestScore) { bestScore = effective; bestId = id; }
            }
            return bestId;
        }
    }
}

