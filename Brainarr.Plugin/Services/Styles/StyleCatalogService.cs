using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Styles
{
    public interface IStyleCatalogService
    {
        IReadOnlyList<StyleEntry> GetAll();
        IEnumerable<StyleEntry> Search(string query, int limit = 50);
        ISet<string> Normalize(IEnumerable<string> selected);
        bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs);
    }

    public class StyleCatalogService : IStyleCatalogService
    {
        private readonly Logger _logger;
        private readonly IHttpClient _httpClient;
        private List<StyleEntry> _cache = new List<StyleEntry>();
        private DateTime _nextRefreshUtc = DateTime.MinValue;
        private string _etag;

        public StyleCatalogService(Logger logger, IHttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient; // optional; may be null in tests
        }

        public IReadOnlyList<StyleEntry> GetAll()
        {
            EnsureLoaded();
            return _cache;
        }

        public IEnumerable<StyleEntry> Search(string query, int limit = 50)
        {
            EnsureLoaded();
            var q = (query ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(q))
            {
                return _cache.OrderBy(s => s.Name, StringComparer.InvariantCultureIgnoreCase)
                             .Take(Math.Max(1, limit));
            }

            var lower = q.ToLowerInvariant();
            return _cache
                .Select(s => new { s, score = Score(s, lower) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.s.Name, StringComparer.InvariantCultureIgnoreCase)
                .Take(Math.Max(1, limit))
                .Select(x => x.s);
        }

        public ISet<string> Normalize(IEnumerable<string> selected)
        {
            EnsureLoaded();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selected == null) return set;
            foreach (var raw in selected)
            {
                var s = (raw ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(s)) continue;
                // accept slug directly
                var bySlug = _cache.FirstOrDefault(x => string.Equals(x.Slug, s, StringComparison.OrdinalIgnoreCase));
                if (bySlug != null) { set.Add(bySlug.Slug); continue; }
                // try name then aliases
                var byName = _cache.FirstOrDefault(x => string.Equals(x.Name, s, StringComparison.OrdinalIgnoreCase) ||
                                                        (x.Aliases?.Any(a => string.Equals(a, s, StringComparison.OrdinalIgnoreCase)) == true));
                if (byName != null) { set.Add(byName.Slug); continue; }
            }
            return set;
        }

        public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs)
        {
            if (libraryGenres == null || libraryGenres.Count == 0 || selectedStyleSlugs == null || selectedStyleSlugs.Count == 0)
                return false;

            EnsureLoaded();
            foreach (var g in libraryGenres)
            {
                var genre = (g ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(genre)) continue;

                var hit = _cache.FirstOrDefault(x =>
                    string.Equals(x.Slug, genre, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Name, genre, StringComparison.OrdinalIgnoreCase) ||
                    (x.Aliases?.Any(a => string.Equals(a, genre, StringComparison.OrdinalIgnoreCase)) == true));

                if (hit != null && selectedStyleSlugs.Contains(hit.Slug)) return true;
            }
            return false;
        }

        private static int Score(StyleEntry s, string lowerQuery)
        {
            if (string.IsNullOrEmpty(lowerQuery)) return 1;
            var name = s.Name?.ToLowerInvariant() ?? string.Empty;
            if (name.StartsWith(lowerQuery)) return 1000 - (name.Length - lowerQuery.Length);
            if (name.Contains(lowerQuery)) return 500 - (name.Length - lowerQuery.Length);
            if (s.Aliases != null)
            {
                foreach (var a in s.Aliases)
                {
                    var al = a?.ToLowerInvariant() ?? string.Empty;
                    if (al.StartsWith(lowerQuery)) return 400 - (al.Length - lowerQuery.Length);
                    if (al.Contains(lowerQuery)) return 200 - (al.Length - lowerQuery.Length);
                }
            }
            return 0;
        }

        private void EnsureLoaded()
        {
            try
            {
                if (_cache.Count == 0)
                {
                    // first-time load from remote; fallback to built-ins on failure
                    TryRefreshFromRemote();
                    if (_cache.Count == 0) _cache = GetBuiltInFallback();
                }
                else if (DateTime.UtcNow >= _nextRefreshUtc)
                {
                    TryRefreshFromRemote();
                }
            }
            catch
            {
                if (_cache.Count == 0) _cache = GetBuiltInFallback();
            }
        }

        private void TryRefreshFromRemote()
        {
            // no HTTP client available (tests), skip
            if (_httpClient == null) { _nextRefreshUtc = DateTime.UtcNow.AddHours(BrainarrConstants.StylesCatalogRefreshHours); return; }

            try
            {
                var req = new HttpRequestBuilder(BrainarrConstants.StylesCatalogUrl).Build();
                req.RequestTimeout = TimeSpan.FromMilliseconds(BrainarrConstants.StylesCatalogTimeoutMs);
                if (!string.IsNullOrWhiteSpace(_etag))
                {
                    req.Headers["If-None-Match"] = _etag;
                }

                var resp = _httpClient.Execute(req);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    _logger.Debug("Styles catalog not modified (ETag)");
                    _nextRefreshUtc = DateTime.UtcNow.AddHours(BrainarrConstants.StylesCatalogRefreshHours);
                    return;
                }
                if (resp.StatusCode == System.Net.HttpStatusCode.OK && !string.IsNullOrWhiteSpace(resp.Content))
                {
                    var list = ParseStyles(resp.Content);
                    if (list.Count > 0)
                    {
                        _cache = list;
                        if (resp.Headers != null)
                        {
                            foreach (var h in resp.Headers)
                            {
                                if (string.Equals(h.Key, "ETag", StringComparison.OrdinalIgnoreCase))
                                {
                                    var et = (h.Value ?? string.Empty).Trim();
                                    if (!string.IsNullOrEmpty(et))
                                    {
                                        _etag = et;
                                    }
                                    break;
                                }
                            }
                        }
                        _logger.Info($"Loaded styles catalog: {list.Count} entries");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Styles catalog refresh failed: {ex.Message}");
            }
            finally
            {
                _nextRefreshUtc = DateTime.UtcNow.AddHours(BrainarrConstants.StylesCatalogRefreshHours);
            }
        }

        private static List<StyleEntry> ParseStyles(string json)
        {
            var list = new List<StyleEntry>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var name = el.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                        var slug = el.TryGetProperty("slug", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug)) continue;

                        var entry = new StyleEntry
                        {
                            Name = name,
                            Slug = slug,
                            Aliases = el.TryGetProperty("aliases", out var a) && a.ValueKind == JsonValueKind.Array
                                ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                                : new List<string>(),
                            Parents = el.TryGetProperty("parents", out var p) && p.ValueKind == JsonValueKind.Array
                                ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                                : new List<string>()
                        };
                        list.Add(entry);
                    }
                }
            }
            catch
            {
                // ignore; caller will fallback
            }
            return list;
        }

        private static List<StyleEntry> GetBuiltInFallback()
        {
            return new List<StyleEntry>
            {
                new StyleEntry { Name = "Rock", Slug = "rock", Aliases = new List<string>{"Classic Rock"}, Parents = new List<string>() },
                new StyleEntry { Name = "Progressive Rock", Slug = "progressive-rock", Aliases = new List<string>{"Prog","Prog Rock"}, Parents = new List<string>{"rock"} },
                new StyleEntry { Name = "Electronic", Slug = "electronic", Aliases = new List<string>{"Electronica"}, Parents = new List<string>() },
                new StyleEntry { Name = "Techno", Slug = "techno", Aliases = new List<string>(), Parents = new List<string>{"electronic"} },
                new StyleEntry { Name = "Jazz", Slug = "jazz", Aliases = new List<string>(), Parents = new List<string>() },
                new StyleEntry { Name = "Hip Hop", Slug = "hip-hop", Aliases = new List<string>{"Rap"}, Parents = new List<string>() },
            };
        }
    }

    public class StyleEntry
    {
        public string Name { get; set; }
        public List<string> Aliases { get; set; } = new List<string>();
        public string Slug { get; set; }
        public List<string> Parents { get; set; } = new List<string>();
    }
}
