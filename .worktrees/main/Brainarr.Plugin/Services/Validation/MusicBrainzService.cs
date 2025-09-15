using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Microsoft.Extensions.Caching.Memory;

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

    public partial class MusicBrainzService : IMusicBrainzService
    {
        private readonly HttpClient _httpClient;
        private readonly Logger _logger;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.IRateLimiter _rateLimiter;

        private const string MusicBrainzBaseUrl = "https://musicbrainz.org/ws/2";

        private static readonly TimeSpan PositiveTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(2);
        private const int CacheMaxEntries = 1000;

        private static readonly MemoryCache Cache = new(new MemoryCacheOptions
        {
            SizeLimit = CacheMaxEntries
        });

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
                    _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(global::Brainarr.Plugin.Services.Security.UserAgentHelper.Build());
                }
            }
            catch { /* best-effort */ }
        }

        public Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle)
            => ValidateArtistAlbumAsync(artistName, albumTitle, default);

        public Task<bool> ValidateArtistAsync(string artistName)
            => ValidateArtistAsync(artistName, default);

        public Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle)
            => SearchArtistAlbumAsync(artistName, albumTitle, default);

        public async Task<bool> ValidateArtistAsync(string artistName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(artistName)) return false;

            var key = $"artist:{NormalizeKey(artistName)}";
            if (TryGet(key, out bool cached))
            {
                return cached;
            }

            var encodedArtist = Uri.EscapeDataString(artistName);
            var url = $"{MusicBrainzBaseUrl}/artist/?query=artist:{encodedArtist}&fmt=json&limit=1";

            try
            {
                var content = await SendWithRetryForContentAsync(url, cancellationToken).ConfigureAwait(false);
                if (content == null)
                {
                    Set(key, false, NegativeTtl);
                    return false;
                }

                var has = HasAnyResults(content);
                Set(key, has, has ? PositiveTtl : NegativeTtl);
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
            if (TryGet(cacheKey, out MusicBrainzSearchResult cachedPair))
            {
                return cachedPair;
            }

            var encodedArtist = Uri.EscapeDataString(artistName);
            var encodedAlbum = Uri.EscapeDataString(albumTitle);
            var url = $"{MusicBrainzBaseUrl}/release-group/?query=artist:{encodedArtist} AND releasegroup:{encodedAlbum}&fmt=json&limit=5";

            try
            {
                var content = await SendWithRetryForContentAsync(url, cancellationToken).ConfigureAwait(false);
                if (content == null)
                {
                    _logger.Debug($"MusicBrainz release-group search failed: no content");
                    var negative = new MusicBrainzSearchResult { Found = false };
                    Set(cacheKey, negative, NegativeTtl);
                    return negative;
                }

                var result = ParseSearchResponse(content, artistName, albumTitle);
                Set(cacheKey, result, result.Found ? PositiveTtl : NegativeTtl);
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

        private static bool TryGet<T>(string key, out T value)
        {
            return Cache.TryGetValue(key, out value!);
        }

        private static void Set<T>(string key, T value, TimeSpan ttl)
        {
            Cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
                Size = 1
            });
        }
    }

    // Helpers
    partial class MusicBrainzService
    {
        private async Task<string?> SendWithRetryForContentAsync(string url, CancellationToken cancellationToken)
        {
            int maxAttempts = NzbDrone.Core.ImportLists.Brainarr.Configuration.BrainarrConstants.MaxRetryAttempts;
            int baseDelayMs = NzbDrone.Core.ImportLists.Brainarr.Configuration.BrainarrConstants.InitialRetryDelayMs;
            int maxDelayMs = NzbDrone.Core.ImportLists.Brainarr.Configuration.BrainarrConstants.MaxRetryDelayMs;

            for (int attempt = 0; attempt < Math.Max(1, maxAttempts); attempt++)
            {
                using (var resp = await _rateLimiter.ExecuteAsync("musicbrainz", ct => _httpClient.GetAsync(url, ct), cancellationToken).ConfigureAwait(false))
                {
                    if (resp.IsSuccessStatusCode)
                    {
                        return await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    }

                    // Only retry on transient 429 / 503
                    if (resp.StatusCode != System.Net.HttpStatusCode.TooManyRequests &&
                        resp.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable)
                    {
                        return null;
                    }
                }

                if (attempt < maxAttempts - 1)
                {
                    var jitter = Random.Shared.Next(50, 200);
                    var delay = Math.Min(maxDelayMs, baseDelayMs * (int)Math.Pow(2, attempt)) + jitter;
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            return null;
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
