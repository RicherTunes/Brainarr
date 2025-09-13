using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IStyleCatalogService
    {
        IReadOnlyList<Style> GetAll();
        IEnumerable<Style> Search(string query, int limit = 50);
        ISet<string> Normalize(IEnumerable<string> selected);
        bool IsMatch(ICollection<string> genres, ISet<string> selectedSlugs, bool relaxParentMatch = false);
        Task RefreshAsync(CancellationToken token = default);
    }

    public class StyleCatalogService : IStyleCatalogService
    {
        private readonly Logger _logger;
        private readonly HttpClient _http;
        private readonly string _remoteUrl;
        private readonly TimeSpan _refreshInterval;
        private readonly int _timeoutMs;

        private readonly object _lock = new();
        private volatile List<Style> _styles = new();
        private volatile Dictionary<string, string> _aliasToSlug = new(StringComparer.OrdinalIgnoreCase);
        private volatile Dictionary<string, Style> _bySlug = new(StringComparer.OrdinalIgnoreCase);
        private string _lastEtag;
        private DateTime _nextRefreshUtc = DateTime.MinValue;

        public StyleCatalogService(Logger logger, HttpClient httpClient, string remoteUrl = null, TimeSpan? refresh = null, int? timeoutMs = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _http = httpClient ?? new HttpClient();
            _remoteUrl = remoteUrl ?? BrainarrConstants.StylesCatalogUrl;
            _refreshInterval = refresh ?? TimeSpan.FromHours(BrainarrConstants.StylesCatalogRefreshHours);
            _timeoutMs = timeoutMs ?? BrainarrConstants.StylesCatalogTimeoutMs;

            try
            {
                LoadEmbedded();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to load embedded styles catalog");
            }
        }

        public IReadOnlyList<Style> GetAll()
        {
            MaybeRefreshAsync().ConfigureAwait(false);
            return _styles;
        }

        public IEnumerable<Style> Search(string query, int limit = 50)
        {
            MaybeRefreshAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(query))
            {
                return _styles.Take(limit);
            }
            var q = query.Trim();
            return _styles
                .Select(s => new { s, score = Score(s, q) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Take(limit)
                .Select(x => x.s);
        }

        private static int Score(Style s, string q)
        {
            if (s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 3;
            if (s.Slug.Contains(q, StringComparison.OrdinalIgnoreCase)) return 2;
            if (s.Aliases != null && s.Aliases.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase))) return 1;
            return 0;
        }

        public ISet<string> Normalize(IEnumerable<string> selected)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selected == null) return set;

            foreach (var item in selected)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                var key = item.Trim();
                if (_bySlug.ContainsKey(key)) { set.Add(_bySlug[key].Slug); continue; }
                if (_aliasToSlug.TryGetValue(key, out var slug1)) { set.Add(slug1); continue; }
                var slug2 = Slugify(key);
                if (_bySlug.ContainsKey(slug2)) { set.Add(slug2); continue; }
                if (_aliasToSlug.TryGetValue(slug2, out var slug3)) { set.Add(slug3); continue; }
            }
            return set;
        }

        public bool IsMatch(ICollection<string> genres, ISet<string> selectedSlugs, bool relaxParentMatch = false)
        {
            if (selectedSlugs == null || selectedSlugs.Count == 0) return true;
            if (genres == null || genres.Count == 0) return false;

            foreach (var g in genres)
            {
                if (string.IsNullOrWhiteSpace(g)) continue;
                var key = g.Trim();
                // direct alias/name/slug
                if (_aliasToSlug.TryGetValue(key, out var slug) && selectedSlugs.Contains(slug)) return true;
                var s = Slugify(key);
                if (selectedSlugs.Contains(s)) return true;
                if (_aliasToSlug.TryGetValue(s, out var slug2) && selectedSlugs.Contains(slug2)) return true;

                // relax: allow parent matching (genre is parent of selected)
                if (relaxParentMatch)
                {
                    if (_bySlug.TryGetValue(s, out var style))
                    {
                        foreach (var child in _styles)
                        {
                            if (child.Parents != null && child.Parents.Any(p => EqualsIgnoreCase(p, style.Name) || EqualsIgnoreCase(p, style.Slug)))
                            {
                                if (selectedSlugs.Contains(child.Slug)) return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static bool EqualsIgnoreCase(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        public async Task RefreshAsync(CancellationToken token = default)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, _remoteUrl);
                if (!string.IsNullOrWhiteSpace(_lastEtag)) req.Headers.TryAddWithoutValidation("If-None-Match", _lastEtag);
                using var cts = new CancellationTokenSource(_timeoutMs);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token);
                using var resp = await _http.SendAsync(req, linked.Token).ConfigureAwait(false);
                if (resp.StatusCode == HttpStatusCode.NotModified)
                {
                    _nextRefreshUtc = DateTime.UtcNow.Add(_refreshInterval);
                    return;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.Warn($"Styles catalog fetch failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    return;
                }
                var json = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
                var styles = JsonSerializer.Deserialize<List<Style>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<Style>();
                if (styles.Count > 0)
                {
                    lock (_lock) { ApplyCatalog(styles); _lastEtag = resp.Headers.ETag?.Tag; _nextRefreshUtc = DateTime.UtcNow.Add(_refreshInterval); }
                    _logger.Info($"Styles catalog updated: {styles.Count} entries");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to refresh styles catalog; keeping last-good snapshot");
            }
        }

        private async Task MaybeRefreshAsync()
        {
            if (DateTime.UtcNow < _nextRefreshUtc) return;
            await RefreshAsync().ConfigureAwait(false);
        }

        private void LoadEmbedded()
        {
            var asm = Assembly.GetExecutingAssembly();
            var resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("music_styles.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                _logger.Warn("Embedded styles catalog not found (music_styles.json)");
                return;
            }
            using var s = asm.GetManifestResourceStream(resourceName);
            if (s == null) return;
            using var rdr = new System.IO.StreamReader(s, Encoding.UTF8);
            var json = rdr.ReadToEnd();
            var styles = JsonSerializer.Deserialize<List<Style>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<Style>();
            ApplyCatalog(styles);
            // Schedule next refresh after the configured interval to avoid immediate network calls in tests
            _nextRefreshUtc = DateTime.UtcNow.Add(_refreshInterval);
            _logger.Debug($"Loaded embedded styles catalog: {styles.Count} entries");
        }

        private void ApplyCatalog(List<Style> styles)
        {
            _styles = styles;
            var alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var bySlug = new Dictionary<string, Style>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in styles)
            {
                var slug = string.IsNullOrWhiteSpace(s.Slug) ? Slugify(s.Name) : s.Slug.Trim();
                bySlug[slug] = s with { Slug = slug };
                alias[s.Name] = slug;
                alias[slug] = slug;
                if (s.Aliases != null)
                {
                    foreach (var a in s.Aliases)
                    {
                        if (string.IsNullOrWhiteSpace(a)) continue;
                        alias[a.Trim()] = slug;
                        var sa = Slugify(a);
                        alias[sa] = slug;
                    }
                }
            }
            _aliasToSlug = alias;
            _bySlug = bySlug;
        }

        private static string Slugify(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var s = new string(text.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            while (s.Contains("--", StringComparison.Ordinal)) s = s.Replace("--", "-");
            return s.Trim('-');
        }
    }
}
