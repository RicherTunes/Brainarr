using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.MetadataSource; // ISearchForNewArtist

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment
{
    public interface IArtistMbidResolver
    {
        System.Threading.Tasks.Task<System.Collections.Generic.List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation>> EnrichArtistsAsync(System.Collections.Generic.List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation> recommendations, System.Threading.CancellationToken ct = default);
    }
    /// <summary>
    /// Resolves MusicBrainz Artist MBIDs for artist-only recommendations.
    /// Filters out items that cannot be confidently resolved.
    /// </summary>
    public class ArtistMbidResolver : IArtistMbidResolver
    {
        private const string BaseUrl = "https://musicbrainz.org/ws/2";
        private readonly HttpClient _httpClient;
        private readonly Logger _logger;
        private ISearchForNewArtist _artistSearch; // optional
        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, (string id, DateTime at)> _cache = new Dictionary<string, (string id, DateTime at)>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

        public ArtistMbidResolver(Logger logger, HttpClient httpClient = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? new HttpClient();
            _artistSearch = null;
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Brainarr/1.0 (https://github.com/openarr/brainarr)");
            }
        }

        public async Task<List<Recommendation>> EnrichArtistsAsync(List<Recommendation> recommendations, CancellationToken ct = default)
        {
            if (recommendations == null || recommendations.Count == 0)
                return new List<Recommendation>();

            var result = new List<Recommendation>(recommendations.Count);

            foreach (var rec in recommendations)
            {
                if (ct.IsCancellationRequested) break;

                if (string.IsNullOrWhiteSpace(rec.Artist))
                {
                    continue;
                }

                try
                {
                    var enriched = await ResolveArtistAsync(rec, ct);
                    if (enriched != null)
                    {
                        result.Add(enriched);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Artist MBID resolution error for '{rec.Artist}'");
                }

                // Be nice to MusicBrainz
                await Task.Delay(200, ct);
            }

            _logger.Info($"Artist MBID resolution complete: {result.Count}/{recommendations.Count} resolvable artists");
            return result;
        }

        private async Task<Recommendation> ResolveArtistAsync(Recommendation rec, CancellationToken ct)
        {
            var encodedArtist = Uri.EscapeDataString(rec.Artist);
            var url = $"{BaseUrl}/artist/?query={encodedArtist}&fmt=json&limit=5";
            // 1) Check in-memory cache
            string cachedId = null;
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(rec.Artist, out var entry) && DateTime.UtcNow - entry.at < CacheTtl)
                {
                    cachedId = entry.id;
                }
            }
            if (!string.IsNullOrWhiteSpace(cachedId))
            {
                return CopyWithArtistId(rec, cachedId);
            }

            // 2) Try Lidarr-side search for robust MBID mapping (if available)
            if (_artistSearch != null)
            {
                try
                {
                    var results = _artistSearch.SearchForNewArtist(rec.Artist) ?? new List<NzbDrone.Core.Music.Artist>();
                    var match = results
                        .Where(a => a?.Metadata?.Value != null)
                        .OrderByDescending(a => string.Equals(a.Metadata.Value.Name, rec.Artist, StringComparison.OrdinalIgnoreCase) ? 2 : 1)
                        .FirstOrDefault();
                    var foreignId = match?.Metadata?.Value?.ForeignArtistId;
                    if (!string.IsNullOrWhiteSpace(foreignId))
                    {
                        Cache(rec.Artist, foreignId);
                        return CopyWithArtistId(rec, foreignId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Lidarr artist search failed for '{rec.Artist}', falling back to MusicBrainz");
                }
            }

            // 3) Fallback: direct MusicBrainz query
            using var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.Debug($"MusicBrainz artist query failed: {resp.StatusCode} for {rec.Artist}");
                return null;
            }

            var content = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("artists", out var artistsElem) || artistsElem.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string bestId = null;
            int bestScore = -1;
            foreach (var a in artistsElem.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var id = a.TryGetProperty("id", out var i) ? i.GetString() : null;
                var score = a.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;

                // Prefer exact name match or high score
                var exact = string.Equals(name, rec.Artist, StringComparison.OrdinalIgnoreCase);
                var effective = score + (exact ? 50 : 0);
                if (effective > bestScore)
                {
                    bestScore = effective;
                    bestId = id;
                }
            }

            if (bestId == null) return null;

            // Confidence: accept base score >= 60 or exact match
            if (bestScore < 60)
            {
                if (bestScore < 50) return null;
            }

            Cache(rec.Artist, bestId);
            return CopyWithArtistId(rec, bestId);
        }

        private void Cache(string artist, string id)
        {
            lock (_cacheLock)
            {
                _cache[artist] = (id, DateTime.UtcNow);
            }
        }

        public void AttachSearchService(ISearchForNewArtist artistSearch)
        {
            _artistSearch = artistSearch;
        }

        private Recommendation CopyWithArtistId(Recommendation rec, string id)
        {
            return new Recommendation
            {
                Artist = rec.Artist,
                Album = rec.Album,
                Genre = rec.Genre,
                Confidence = rec.Confidence,
                Reason = rec.Reason,
                Year = rec.Year,
                ReleaseYear = rec.ReleaseYear,
                ArtistMusicBrainzId = id,
                AlbumMusicBrainzId = rec.AlbumMusicBrainzId,
                MusicBrainzId = rec.MusicBrainzId,
                Source = rec.Source,
                Provider = rec.Provider,
                SpotifyId = rec.SpotifyId
            };
        }
    }
}
