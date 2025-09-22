using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    public class LibraryPromptPlannerTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_UsesCacheOnSubsequentCalls()
        {
            var styleCatalog = new NoOpStyleCatalog();
            var cache = new PlanCache(capacity: 8);
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, cache);

            var profile = new LibraryProfile
            {
                TotalArtists = 2,
                TotalAlbums = 0,
                StyleContext = new LibraryStyleContext()
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 5
            };

            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "ArtistA", Added = DateTime.UtcNow.AddDays(-10) },
                new Artist { Id = 2, Name = "ArtistB", Added = DateTime.UtcNow.AddDays(-5) }
            };
            var albums = new List<Album>();

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                profile.StyleContext,
                recommendArtists: true,
                targetTokens: 4000,
                availableSamplingTokens: 2800,
                modelKey: "openai:gpt-4",
                contextWindow: 64000);

            var firstPlan = planner.Plan(profile, request, CancellationToken.None);
            Assert.False(firstPlan.FromCache);

            var secondPlan = planner.Plan(profile, request, CancellationToken.None);

            Assert.True(secondPlan.FromCache);
            Assert.Equal(firstPlan.PlanCacheKey, secondPlan.PlanCacheKey);
            Assert.Equal(firstPlan.SampleFingerprint, secondPlan.SampleFingerprint);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_OrdersArtistsByRecencyThenIdForTies()
        {
            var styleCatalog = new StaticStyleCatalog(new StyleEntry { Name = "Alt", Slug = "alt" });
            var cache = new PlanCache(capacity: 4);
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, cache);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["alt"] = 2
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 1, 2 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));
            styleContext.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            styleContext.ArtistStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };

            var profile = new LibraryProfile
            {
                TotalArtists = 2,
                TotalAlbums = 0,
                StyleContext = styleContext
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 5,
                StyleFilters = new[] { "alt" }
            };

            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "ArtistA", Added = DateTime.UtcNow.AddDays(-10) },
                new Artist { Id = 2, Name = "ArtistB", Added = DateTime.UtcNow.AddDays(-1) }
            };
            var albums = new List<Album>();

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                styleContext,
                recommendArtists: true,
                targetTokens: 4000,
                availableSamplingTokens: 2800,
                modelKey: "openai:gpt-4",
                contextWindow: 64000);

            var plan = planner.Plan(profile, request, CancellationToken.None);

            var orderedIds = plan.Sample.Artists.Select(a => a.ArtistId).ToArray();
            Assert.Equal(new[] { 2, 1 }, orderedIds);
        }

        private sealed class NoOpStyleCatalog : IStyleCatalogService
        {
            public IReadOnlyList<StyleEntry> GetAll() => Array.Empty<StyleEntry>();
            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => Array.Empty<StyleEntry>();
            public ISet<string> Normalize(IEnumerable<string> selected) => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs) => false;
            public string? ResolveSlug(string value) => value;
            public StyleEntry? GetBySlug(string slug) => null;
            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug) => Array.Empty<StyleSimilarity>();
        }

        private sealed class StaticStyleCatalog : IStyleCatalogService
        {
            private readonly Dictionary<string, StyleEntry> _entries;

            public StaticStyleCatalog(params StyleEntry[] entries)
            {
                _entries = entries.ToDictionary(e => e.Slug, StringComparer.OrdinalIgnoreCase);
            }

            public IReadOnlyList<StyleEntry> GetAll() => _entries.Values.ToList();

            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => _entries.Values;

            public ISet<string> Normalize(IEnumerable<string> selected)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (selected == null)
                {
                    return set;
                }

                foreach (var value in selected)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var key = value.Trim();
                    if (_entries.ContainsKey(key))
                    {
                        set.Add(_entries[key].Slug);
                    }
                    else
                    {
                        set.Add(key);
                    }
                }

                return set;
            }

            public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs) => false;

            public string? ResolveSlug(string value) => value;

            public StyleEntry? GetBySlug(string slug) => _entries.TryGetValue(slug, out var entry) ? entry : null;

            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug) => Array.Empty<StyleSimilarity>();
        }
    }
}
