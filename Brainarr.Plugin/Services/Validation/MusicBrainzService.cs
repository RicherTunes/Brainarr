using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    public interface IMusicBrainzService
    {
        Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle);
        Task<bool> ValidateArtistAsync(string artistName);
        Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle);

        Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle, CancellationToken cancellationToken);
        Task<bool> ValidateArtistAsync(string artistName, CancellationToken cancellationToken);
        Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle, CancellationToken cancellationToken);
    }

    public class MusicBrainzService : IMusicBrainzService
    {
        private readonly HttpClient _httpClient;
        private readonly Logger _logger;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.IRateLimiter _rateLimiter;

        private const string MusicBrainzBaseUrl = "https://musicbrainz.org/ws/2";
        private const string UserAgent = "Brainarr/1.0 (+https://github.com/your-repo/brainarr)";

        private static readonly TimeSpan PositiveTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(2);
        private const int MaxEntries = 1000;

        private readonly ConcurrentDictionary<string, CacheEntry<bool>> _artistExistsCache = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CacheEntry<MusicBrainzSearchResult?>> _artistAlbumCache = new(StringComparer.Ordinal);

        private sealed class CacheEntry<T>
        {
            public T Value { get; init; }
            public DateTime ExpiresUtc { get; init; }
            public DateTime CreatedUtc { get; init; }
        }

        public MusicBrainzService(HttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rateLimiter = new NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiter(logger);
            NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiterConfiguration.ConfigureDefaults(_rateLimiter);

            try
            {
                if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
                {
                    _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                }
            }
            catch { /* best-effort */ }
        }

        public Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle)
            => ValidateArtistAlbumAsync(artistName, albumTitle, CancellationToken.None);

        public Task<bool> ValidateArtistAsync(string artistName)
            => ValidateArtistAsync(artistName, CancellationToken.None);

        public Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle)
            => SearchArtistAlbumAsync(artistName, albumTitle, CancellationToken.None);

        public async Task<bool> ValidateArtistAsync(string artistName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(artistName)) return false;

            var key = $"artist:{NormalizeKey(artistName)}";
            if (TryGet(_artistExistsCache, key, out var cached))
            {
                return cached;
            }

            var encodedArtist = Uri.EscapeDataString(artistName);
            var url = $"{MusicBrainzBaseUrl}/artist/?query=artist:{encodedArtist}&fmt=json&limit=1";

            try
            {
                using var resp = await _rateLimiter.ExecuteAsync("musicbrainz", async (ct) => await _httpClient.GetAsync(url, ct), cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.Debug($"MusicBrainz artist search failed: {resp.StatusCode}");
                    Set(_artistExistsCache, key, false, NegativeTtl);
                    return false;
                }

                var content = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var has = HasAnyResults(content);
                Set(_artistExistsCache, key, has, has ? PositiveTtl : NegativeTtl);
                return has;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"MusicBrainz artist validation error for '{artistName}'");
                return false;
            }
        }

        public async Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle, CancellationToken cancellationToken)
        {
            var res = await SearchArtistAlbumAsync(artistName, albumTitle, cancellationToken).ConfigureAwait(false);
            return res.Found;
        }

        public async Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle))
            {
                return new MusicBrainzSearchResult { Found = false };
            }

            var cacheKey = $"pair:{NormalizeKey(artistName)}|{NormalizeKey(albumTitle)}";
            if (TryGet(_artistAlbumCache, cacheKey, out var cached))
            {
                return cached ?? new MusicBrainzSearchResult { Found = false };
            }

            var encodedArtist = Uri.EscapeDataString(artistName);
            var encodedAlbum = Uri.EscapeDataString(albumTitle);
            var url = $"{MusicBrainzBaseUrl}/release-group/?query=artist:{encodedArtist} AND releasegroup:{encodedAlbum}&fmt=json&limit=5";

            try
            {
                using var resp = await _rateLimiter.ExecuteAsync("musicbrainz", async (ct) => await _httpClient.GetAsync(url, ct), cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.Debug($"MusicBrainz release-group search failed: {resp.StatusCode}");
                    Set(_artistAlbumCache, cacheKey, null, NegativeTtl);
                    return new MusicBrainzSearchResult { Found = false };
                }

                var content = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var result = ParseSearchResponse(content, artistName, albumTitle);
                Set(_artistAlbumCache, cacheKey, result, result.Found ? PositiveTtl : NegativeTtl);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"MusicBrainz search error for '{artistName} - {albumTitle}'");
                return new MusicBrainzSearchResult { Found = false };
            }
        }

        private static string NormalizeKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var arr = s.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(arr);
        }

        private static bool HasAnyResults(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("count", out var countEl) && countEl.ValueKind == JsonValueKind.Number)
                {
                    if (countEl.TryGetInt32(out var c)) return c > 0;
                }
                return json.Contains("\"release-groups\":") && !json.Contains("\"release-groups\":[]");
            }
            catch
            {
                return json.Contains("\"count\":") && !json.Contains("\"count\":0");
            }
        }

        private MusicBrainzSearchResult ParseSearchResponse(string json, string artist, string album)
        {
            var result = new MusicBrainzSearchResult { Found = false, MatchCount = 0 };
            if (string.IsNullOrEmpty(json)) return result;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                JsonElement groups;
                if (root.TryGetProperty("release-groups", out groups) || root.TryGetProperty("release_groups", out groups))
                {
                    if (groups.ValueKind == JsonValueKind.Array)
                    {
                        var count = groups.GetArrayLength();
                        result.MatchCount = count;
                        result.Found = count > 0;
                    }
                }

                result.RawResponse = json.Length > 1000 ? json.Substring(0, 1000) + "..." : json;
                return result;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error parsing MusicBrainz JSON");
                result.Found = HasAnyResults(json);
                result.MatchCount = result.Found ? 1 : 0;
                return result;
            }
        }

        private static bool TryGet<T>(ConcurrentDictionary<string, CacheEntry<T>> dict, string key, out T value)
        {
            value = default;
            if (dict.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow <= entry.ExpiresUtc)
                {
                    value = entry.Value;
                    return true;
                }
                dict.TryRemove(key, out _);
            }
            return false;
        }

        private static void Set<T>(ConcurrentDictionary<string, CacheEntry<T>> dict, string key, T value, TimeSpan ttl)
        {
            var now = DateTime.UtcNow;
            dict[key] = new CacheEntry<T> { Value = value, ExpiresUtc = now + ttl, CreatedUtc = now };
            if (dict.Count > MaxEntries)
            {
                foreach (var k in dict.OrderBy(e => e.Value.CreatedUtc).Take(Math.Max(1, dict.Count - MaxEntries + 1)).Select(e => e.Key))
                {
                    dict.TryRemove(k, out _);
                }
            }
        }
    }

    public class MusicBrainzSearchResult
    {
        public bool Found { get; set; }
        public int MatchCount { get; set; }
        public double ConfidenceScore { get; set; }
        public MusicBrainzMatch BestMatch { get; set; }
        public string RawResponse { get; set; }
    }

    public class MusicBrainzMatch
    {
        public string ArtistId { get; set; } = string.Empty;
        public string ReleaseGroupId { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string AlbumTitle { get; set; } = string.Empty;
        public DateTime? ReleaseDate { get; set; }
        public string PrimaryType { get; set; } = string.Empty;
    }
}
