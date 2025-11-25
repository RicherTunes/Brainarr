using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        string? ResolveSlug(string value);
        StyleEntry? GetBySlug(string slug);
        IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug);
    }

    public class StyleCatalogService : IStyleCatalogService
    {
        private readonly Logger _logger;
        private readonly IHttpClient _httpClient;
        private List<StyleEntry> _cache = new List<StyleEntry>();
        private readonly Dictionary<string, StyleEntry> _entriesBySlug = new Dictionary<string, StyleEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _valueToSlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _childrenByParent = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private DateTime _nextRefreshUtc = DateTime.MinValue;
        private string _etag;
        private readonly object _syncRoot = new();
        private static readonly string EmbeddedResourceName = "NzbDrone.Core.ImportLists.Brainarr.Resources.music_styles.json";

        public StyleCatalogService(Logger logger, IHttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient; // optional; may be null in tests
        }

        public IReadOnlyList<StyleEntry> GetAll()
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                return _cache.ToArray();
            }
        }

        public IEnumerable<StyleEntry> Search(string query, int limit = 50)
        {
            EnsureLoaded();
            var q = (query ?? string.Empty).Trim();
            lock (_syncRoot)
            {
                if (string.IsNullOrEmpty(q))
                {
                    return _cache
                        .OrderBy(s => s.Name, StringComparer.InvariantCultureIgnoreCase)
                        .Take(Math.Max(1, limit))
                        .ToList();
                }

                var lower = q.ToLowerInvariant();
                return _cache
                    .Select(s => new { s, score = Score(s, lower) })
                    .Where(x => x.score > 0)
                    .OrderByDescending(x => x.score)
                    .ThenBy(x => x.s.Name, StringComparer.InvariantCultureIgnoreCase)
                    .Take(Math.Max(1, limit))
                    .Select(x => x.s)
                    .ToList();
            }
        }

        public ISet<string> Normalize(IEnumerable<string> selected)
        {
            EnsureLoaded();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selected == null) return set;

            lock (_syncRoot)
            {
                foreach (var raw in selected)
                {
                    var slug = ResolveSlugInternal(raw);
                    if (!string.IsNullOrEmpty(slug))
                    {
                        set.Add(slug);
                    }
                }
            }

            return set;
        }

        public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs)
        {
            if (libraryGenres == null || libraryGenres.Count == 0 || selectedStyleSlugs == null || selectedStyleSlugs.Count == 0)
                return false;

            EnsureLoaded();
            lock (_syncRoot)
            {
                foreach (var g in libraryGenres)
                {
                    var slug = ResolveSlugInternal(g);
                    if (!string.IsNullOrEmpty(slug) && selectedStyleSlugs.Contains(slug))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public string? ResolveSlug(string value)
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                return ResolveSlugInternal(value);
            }
        }

        public StyleEntry? GetBySlug(string slug)
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                if (string.IsNullOrWhiteSpace(slug)) return null;
                return _entriesBySlug.TryGetValue(slug, out var entry) ? entry : null;
            }
        }

        public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug)
        {
            EnsureLoaded();
            var results = new List<StyleSimilarity>();
            lock (_syncRoot)
            {
                var canonical = ResolveSlugInternal(slug) ?? slug;
                if (string.IsNullOrWhiteSpace(canonical)) return results;

                if (!_entriesBySlug.TryGetValue(canonical, out var entry)) return results;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (seen.Add(entry.Slug))
                {
                    results.Add(new StyleSimilarity(entry.Slug, 1.0, "self"));
                }

                if (entry.Parents != null)
                {
                    foreach (var parent in entry.Parents)
                    {
                        var parentSlug = ResolveSlugInternal(parent) ?? parent;
                        if (string.IsNullOrWhiteSpace(parentSlug)) continue;

                        if (seen.Add(parentSlug))
                        {
                            results.Add(new StyleSimilarity(parentSlug, 0.85, "parent"));
                        }

                        if (_childrenByParent.TryGetValue(parentSlug, out var siblings))
                        {
                            foreach (var sibling in siblings)
                            {
                                if (!string.Equals(sibling, entry.Slug, StringComparison.OrdinalIgnoreCase) && seen.Add(sibling))
                                {
                                    results.Add(new StyleSimilarity(sibling, 0.75, "sibling"));
                                }
                            }
                        }
                    }
                }

                if (_childrenByParent.TryGetValue(entry.Slug, out var children))
                {
                    foreach (var child in children)
                    {
                        if (seen.Add(child))
                        {
                            results.Add(new StyleSimilarity(child, 0.85, "child"));
                        }

                        if (_childrenByParent.TryGetValue(child, out var grandChildren))
                        {
                            foreach (var grandChild in grandChildren)
                            {
                                if (seen.Add(grandChild))
                                {
                                    results.Add(new StyleSimilarity(grandChild, 0.7, "grandchild"));
                                }
                            }
                        }
                    }
                }
            }

            return results;
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

        private void RebuildIndexes()
        {
            lock (_syncRoot)
            {
                _entriesBySlug.Clear();
                _valueToSlug.Clear();
                _childrenByParent.Clear();

                foreach (var entry in _cache)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Slug))
                    {
                        continue;
                    }

                    _entriesBySlug[entry.Slug] = entry;
                    _valueToSlug[entry.Slug] = entry.Slug;

                    if (!string.IsNullOrWhiteSpace(entry.Name))
                    {
                        _valueToSlug[entry.Name] = entry.Slug;
                    }

                    if (entry.Aliases != null)
                    {
                        foreach (var alias in entry.Aliases)
                        {
                            var trimmed = (alias ?? string.Empty).Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                _valueToSlug[trimmed] = entry.Slug;
                            }
                        }
                    }

                    if (entry.Parents != null)
                    {
                        foreach (var parent in entry.Parents)
                        {
                            var trimmed = (parent ?? string.Empty).Trim();
                            if (string.IsNullOrEmpty(trimmed))
                            {
                                continue;
                            }

                            if (!_childrenByParent.TryGetValue(trimmed, out var children))
                            {
                                children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                _childrenByParent[trimmed] = children;
                            }

                            children.Add(entry.Slug);
                        }
                    }
                }
            }
        }

        private void EnsureLoaded()
        {
            lock (_syncRoot)
            {
                try
                {
                    if (_cache.Count == 0)
                    {
                        var loaded = LoadEmbeddedCatalog();
                        if (!loaded)
                        {
                            _cache = GetBuiltInFallback();
                            RebuildIndexes();
                        }
                        TryRefreshFromRemote();
                    }
                    else if (DateTime.UtcNow >= _nextRefreshUtc)
                    {
                        TryRefreshFromRemote();
                    }
                }
                catch
                {
                    if (_cache.Count == 0 && !LoadEmbeddedCatalog())
                    {
                        _cache = GetBuiltInFallback();
                        RebuildIndexes();
                    }
                }
            }
        }

        private void TryRefreshFromRemote()
        {
            // Allow operators to disable remote fetch entirely (air-gapped or deterministic setups)
            var disableRemote = string.Equals(Environment.GetEnvironmentVariable("BRAINARR_DISABLE_STYLES_REMOTE"), "true", StringComparison.OrdinalIgnoreCase);
            if (disableRemote)
            {
                _logger.Debug("Styles remote fetch disabled via BRAINARR_DISABLE_STYLES_REMOTE");
                _nextRefreshUtc = DateTime.UtcNow.AddHours(BrainarrConstants.StylesCatalogRefreshHours);
                return;
            }

            // Allow overriding the URL or pinning to a tag/ref for determinism
            var url = Environment.GetEnvironmentVariable("BRAINARR_STYLES_CATALOG_URL");
            if (string.IsNullOrWhiteSpace(url)) url = BrainarrConstants.StylesCatalogUrl;

            var refOverride = Environment.GetEnvironmentVariable("BRAINARR_STYLES_CATALOG_REF");
            if (!string.IsNullOrWhiteSpace(refOverride) && !string.IsNullOrWhiteSpace(url))
            {
                // If the URL points at raw.githubusercontent.com and contains '/main/', allow replacing it with the provided ref/tag.
                // This is a best-effort transform for the canonical URL form.
                var needle = "/main/";
                if (url.Contains(needle, StringComparison.Ordinal))
                {
                    url = url.Replace(needle, "/" + refOverride + "/", StringComparison.Ordinal);
                }
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                _nextRefreshUtc = DateTime.UtcNow.AddHours(BrainarrConstants.StylesCatalogRefreshHours);
                return;
            }

            // no HTTP client available (tests), skip
            if (_httpClient == null) { _nextRefreshUtc = DateTime.UtcNow.AddHours(BrainarrConstants.StylesCatalogRefreshHours); return; }

            try
            {
                var req = new HttpRequestBuilder(url).Build();
                req.RequestTimeout = TimeSpan.FromMilliseconds(BrainarrConstants.StylesCatalogTimeoutMs);
                var currentEtag = _etag;
                if (!string.IsNullOrWhiteSpace(currentEtag))
                {
                    req.Headers["If-None-Match"] = currentEtag;
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
                        RebuildIndexes();
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

        private bool LoadEmbeddedCatalog()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
                if (stream == null)
                {
                    _logger.Debug($"Embedded styles resource '{EmbeddedResourceName}' not found");
                    return false;
                }

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var list = ParseStyles(json);
                if (list.Count > 0)
                {
                    _cache = list;
                    RebuildIndexes();
                    _logger.Debug($"Loaded {list.Count} styles from embedded catalog");
                    _nextRefreshUtc = DateTime.UtcNow.AddHours(BrainarrConstants.StylesCatalogRefreshHours);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Embedded styles load failed: {ex.Message}");
            }

            return false;
        }

        private string? ResolveSlugInternal(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            return _valueToSlug.TryGetValue(trimmed, out var slug) ? slug : null;
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

    public readonly record struct StyleSimilarity(string Slug, double Score, string Relationship);
}
