using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Time;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    public class LibraryPromptPlannerCacheTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void ReturnsCachedPlan_WhenInputsIdentical()
        {
            var planner = new LibraryPromptPlanner(Logger, new NoOpStyleCatalog(), new PlanCache(capacity: 8));

            var profile = new LibraryProfile { TotalArtists = 2, TotalAlbums = 2, StyleContext = new LibraryStyleContext() };
            var settings = new BrainarrSettings { DiscoveryMode = DiscoveryMode.Similar, SamplingStrategy = SamplingStrategy.Balanced, MaxRecommendations = 5 };
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "A", Added = DateTime.UtcNow.AddDays(-2) },
                new Artist { Id = 2, Name = "B", Added = DateTime.UtcNow.AddDays(-1) }
            };
            var albums = new List<Album>();
            var request = new RecommendationRequest(artists, albums, settings, profile.StyleContext, true, 3200, 2400, "openai:gpt-4o-mini", 64000);

            var first = planner.Plan(profile, request, CancellationToken.None);
            var second = planner.Plan(profile, request, CancellationToken.None);

            Assert.False(first.FromCache);
            Assert.True(second.FromCache);
            Assert.Equal(first.PlanCacheKey, second.PlanCacheKey);
            Assert.Equal(first.SampleFingerprint, second.SampleFingerprint);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void CacheExpires_ByTtl()
        {
            var clock = new ManualClock(DateTime.UtcNow);
            var cache = new PlanCache(capacity: 4, metrics: null, clock: clock);
            var planner = new LibraryPromptPlanner(Logger, new NoOpStyleCatalog(), cache, TimeSpan.FromMilliseconds(10));

            var profile = new LibraryProfile { TotalArtists = 1, TotalAlbums = 0, StyleContext = new LibraryStyleContext() };
            var settings = new BrainarrSettings { DiscoveryMode = DiscoveryMode.Similar, SamplingStrategy = SamplingStrategy.Balanced, MaxRecommendations = 3 };
            var artists = new List<Artist> { new Artist { Id = 1, Name = "Solo", Added = DateTime.UtcNow.AddDays(-3) } };
            var request = new RecommendationRequest(artists, new List<Album>(), settings, profile.StyleContext, true, 2000, 1500, "openai:gpt-4o-mini", 64000);

            var initial = planner.Plan(profile, request, CancellationToken.None);
            Assert.False(initial.FromCache);

            clock.Advance(TimeSpan.FromMilliseconds(25));
            Assert.False(cache.TryGet(initial.PlanCacheKey, out _));

            var refreshed = planner.Plan(profile, request, CancellationToken.None);
            Assert.False(refreshed.FromCache);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void InvalidateByFingerprint_RemovesPlan()
        {
            var cache = new PlanCache(capacity: 8);
            var planner = new LibraryPromptPlanner(Logger, new NoOpStyleCatalog(), cache);

            var profile = new LibraryProfile { TotalArtists = 2, TotalAlbums = 1, StyleContext = new LibraryStyleContext() };
            var settings = new BrainarrSettings { DiscoveryMode = DiscoveryMode.Similar, SamplingStrategy = SamplingStrategy.Balanced, MaxRecommendations = 4 };
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Alpha", Added = DateTime.UtcNow.AddDays(-2) },
                new Artist { Id = 2, Name = "Beta", Added = DateTime.UtcNow.AddDays(-1) }
            };
            var request = new RecommendationRequest(artists, new List<Album>(), settings, profile.StyleContext, true, 2800, 2000, "openai:gpt-4o-mini", 64000);

            var plan = planner.Plan(profile, request, CancellationToken.None);
            cache.InvalidateByFingerprint(plan.LibraryFingerprint);
            Assert.False(cache.TryGet(plan.PlanCacheKey, out _));
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void CacheKey_Stable_WhenStylesReordered()
        {
            var catalog = new NormalizingStyleCatalog();
            var planner = new LibraryPromptPlanner(Logger, catalog, new PlanCache(capacity: 8));

            var coverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["shoegaze"] = 4,
                ["dreampop"] = 3,
                ["ambient"] = 2
            };
            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(coverage);
            styleContext.SetDominantStyles(new[] { "shoegaze", "dreampop", "ambient" });
            styleContext.SetStyleIndex(LibraryStyleIndex.Empty);

            var profile = new LibraryProfile { TotalArtists = 5, TotalAlbums = 2, StyleContext = styleContext };
            var settingsA = new BrainarrSettings { DiscoveryMode = DiscoveryMode.Similar, SamplingStrategy = SamplingStrategy.Balanced, MaxRecommendations = 5, StyleFilters = new[] { "shoegaze", "dreampop", "ambient" } };
            var settingsB = new BrainarrSettings { DiscoveryMode = DiscoveryMode.Similar, SamplingStrategy = SamplingStrategy.Balanced, MaxRecommendations = 5, StyleFilters = new[] { "ambient", "shoegaze", "dreampop" } };

            var artists = Enumerable.Range(1, 5).Select(i => new Artist { Id = i, Name = $"Artist {i}", Added = DateTime.UtcNow.AddDays(-i) }).ToList();
            var albums = new List<Album>();

            var requestA = new RecommendationRequest(artists, albums, settingsA, profile.StyleContext, true, 3200, 2400, "openai:gpt-4o-mini", 64000);
            var planA = planner.Plan(profile, requestA, CancellationToken.None);

            var requestB = new RecommendationRequest(artists, albums, settingsB, profile.StyleContext, true, 3200, 2400, "openai:gpt-4o-mini", 64000);
            var planB = planner.Plan(profile, requestB, CancellationToken.None);

            Assert.Equal(planA.PlanCacheKey, planB.PlanCacheKey);
            Assert.Equal(planA.SampleFingerprint, planB.SampleFingerprint);
        }

        private sealed class NoOpStyleCatalog : IStyleCatalogService
        {
            public IReadOnlyList<StyleEntry> GetAll() => Array.Empty<StyleEntry>();
            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => Array.Empty<StyleEntry>();
            public ISet<string> Normalize(IEnumerable<string> slugs) => new HashSet<string>(slugs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            public bool IsMatch(ICollection<string> groupSlugs, ISet<string> selected) => false;
            public string? ResolveSlug(string value) => value;
            public StyleEntry? GetBySlug(string slug) => new StyleEntry { Name = slug, Slug = slug };
            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug) => Array.Empty<StyleSimilarity>();
        }

        private sealed class NormalizingStyleCatalog : IStyleCatalogService
        {
            public IReadOnlyList<StyleEntry> GetAll() => Array.Empty<StyleEntry>();
            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => Array.Empty<StyleEntry>();
            public ISet<string> Normalize(IEnumerable<string> slugs) => new HashSet<string>(slugs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            public bool IsMatch(ICollection<string> groupSlugs, ISet<string> selected) => false;
            public string? ResolveSlug(string value) => value;
            public StyleEntry? GetBySlug(string slug) => new StyleEntry { Name = slug, Slug = slug };
            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug) => Array.Empty<StyleSimilarity>();
        }

        private sealed class ManualClock : IClock
        {
            private DateTime _utcNow;
            public ManualClock(DateTime start) => _utcNow = start;
            public DateTime UtcNow => _utcNow;
            public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
        }
    }
}
