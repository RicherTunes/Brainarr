using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment
{
    public interface IMusicBrainzResolver
    {
        Task<List<Recommendation>> EnrichWithMbidsAsync(List<Recommendation> recommendations, CancellationToken ct = default);
    }

    /// <summary>
    /// Resolves MusicBrainz IDs for artist/album pairs to make import mapping reliable.
    /// Filters out items that cannot be confidently resolved.
    /// </summary>
    public class MusicBrainzResolver : IMusicBrainzResolver
    {
        private const string BaseUrl = "https://musicbrainz.org/ws/2";
        private readonly HttpClient _httpClient;
        private readonly Logger _logger;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.IRateLimiter _rateLimiter;
        private readonly object _lruLock = new object();
        private readonly Dictionary<string, (Recommendation rec, DateTime at)> _recent;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
        private const int CacheMax = 512;

        public MusicBrainzResolver(Logger logger, HttpClient httpClient = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? NzbDrone.Core.ImportLists.Brainarr.Services.Http.SecureHttpClientFactory.Create(Configuration.Policy.Providers.MusicBrainz);
            _rateLimiter = new NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiter(logger);
            NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiterConfiguration.ConfigureDefaults(_rateLimiter);
            _recent = new Dictionary<string, (Recommendation rec, DateTime at)>(StringComparer.Ordinal);
        }

        public async Task<List<Recommendation>> EnrichWithMbidsAsync(List<Recommendation> recommendations, CancellationToken ct = default)
        {
            if (recommendations == null || recommendations.Count == 0)
                return new List<Recommendation>();

            var result = new List<Recommendation>(recommendations.Count);
            var resolvableCount = 0;
            var resolvedCount = 0;
            // Deduplicate within this batch to avoid repeated queries
            foreach (var rec in recommendations)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    if (string.IsNullOrWhiteSpace(rec.Artist) || string.IsNullOrWhiteSpace(rec.Album))
                    {
                        continue;
                    }

                    resolvableCount++;

                    // Check LRU cache first
                    var key = MakeKey(rec.Artist, rec.Album);
                    Recommendation cached = null;
                    lock (_lruLock)
                    {
                        if (_recent.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.at < CacheTtl)
                        {
                            cached = entry.rec;
                        }
                    }

                    var enriched = cached ?? await ResolveMbidAsync(rec, ct);
                    if (enriched != null)
                    {
                        resolvedCount++;
                        // Update LRU
                        lock (_lruLock)
                        {
                            _recent[key] = (enriched, DateTime.UtcNow);
                            if (_recent.Count > CacheMax)
                            {
                                // Evict oldest ~10% (simple pass)
                                foreach (var old in _recent.OrderBy(kv => kv.Value.at).Take(Math.Max(1, CacheMax / 10)).ToList())
                                    _recent.Remove(old.Key);
                            }
                        }
                        result.Add(enriched);
                    }
                    else
                    {
                        // Best-effort: preserve the original recommendation when MBID resolution fails.
                        // Downstream safety gates (RequireMbids) can still enforce MBID presence when configured.
                        result.Add(rec);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"MBID resolution error for '{rec.Artist} - {rec.Album}'");
                }

                // Throttling handled centrally via RateLimiter("musicbrainz")
            }

            _logger.Info($"MBID resolution complete: resolved {resolvedCount}/{resolvableCount} recommendations (returned {result.Count})");
            return result;
        }

        private static string MakeKey(string artist, string album)
        {
            static string Norm(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                return new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            }
            return Norm(artist) + "|" + Norm(album);
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var lower = s.ToLowerInvariant();
            var cleaned = new string(lower.Where(char.IsLetterOrDigit).ToArray());
            return cleaned;
        }

        private async Task<Recommendation> ResolveMbidAsync(Recommendation rec, CancellationToken ct)
        {
            var encodedArtist = Uri.EscapeDataString(rec.Artist);
            var encodedAlbum = Uri.EscapeDataString(rec.Album);

            // Search release-groups for artist/title
            var url = $"{BaseUrl}/release-group/?query=artist:{encodedArtist}%20AND%20releasegroup:{encodedAlbum}&fmt=json&limit=5";

            using var resp = await _rateLimiter.ExecuteAsync("musicbrainz", async (token) =>
            {
                return await _httpClient.GetAsync(url, token);
            }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.Debug($"MusicBrainz query failed: {resp.StatusCode} for {rec.Artist} - {rec.Album}");
                return null;
            }

            var content = await resp.Content.ReadAsStringAsync(ct);
            var json = JObject.Parse(content);

            var groups = (json["release-groups"] as JArray) ?? (json["release_groups"] as JArray);
            if (groups == null || groups.Count == 0)
            {
                return null;
            }

            // Choose best candidate by score, then by normalized title match
            var normAlbum = Normalize(rec.Album);
            var normArtist = Normalize(rec.Artist);

            JToken best = null;
            int bestScore = -1;

            foreach (var g in groups)
            {
                var score = g.Value<int?>("score") ?? 0;
                var title = g.Value<string>("title") ?? string.Empty;
                var ac = g["artist-credit"] as JArray;
                var acName = ac?.First?["artist"]?["name"]?.ToString() ?? string.Empty;

                var titleMatch = Normalize(title) == normAlbum;
                var artistMatch = !string.IsNullOrEmpty(acName) && Normalize(acName) == normArtist;

                // Prefer exact matches first
                var effectiveScore = score + (titleMatch ? 50 : 0) + (artistMatch ? 50 : 0);
                if (effectiveScore > bestScore)
                {
                    bestScore = effectiveScore;
                    best = g;
                }
            }

            if (best == null)
                return null;

            // Confidence threshold: base MusicBrainz score >= 60 OR exact title+artist match
            var bestBaseScore = best.Value<int?>("score") ?? 0;
            var bestTitle = best.Value<string>("title") ?? string.Empty;
            var bestTitleMatch = Normalize(bestTitle) == normAlbum;
            var bestAc = best["artist-credit"] as JArray;
            var bestArtistName = bestAc?.First?["artist"]?["name"]?.ToString() ?? string.Empty;
            var bestArtistMatch = Normalize(bestArtistName) == normArtist;

            var confident = bestBaseScore >= 60 || (bestTitleMatch && bestArtistMatch);
            if (!confident)
            {
                return null;
            }

            var releaseGroupId = best.Value<string>("id");
            var artistId = bestAc?.First?["artist"]?["id"]?.ToString();
            var firstReleaseDate = best.Value<string>("first-release-date");
            int? year = null;
            if (DateTime.TryParse(firstReleaseDate, out var dt))
            {
                year = dt.Year;
            }

            return new Recommendation
            {
                Artist = rec.Artist,
                Album = rec.Album,
                Genre = rec.Genre,
                Confidence = rec.Confidence,
                Reason = rec.Reason,
                Year = rec.Year ?? year,
                ReleaseYear = rec.ReleaseYear ?? year,
                Source = rec.Source,
                Provider = rec.Provider,
                ArtistMusicBrainzId = artistId,
                AlbumMusicBrainzId = releaseGroupId,
                MusicBrainzId = releaseGroupId,
                SpotifyId = rec.SpotifyId
            };
        }
    }
}
