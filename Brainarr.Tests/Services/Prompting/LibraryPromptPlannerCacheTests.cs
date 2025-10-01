using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
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

        public void CacheKey_IncludesPlannerVersion()
        {
            var planner = new LibraryPromptPlanner(Logger, new NoOpStyleCatalog(), new PlanCache(capacity: 4));
            var profile = new LibraryProfile { TotalArtists = 1, TotalAlbums = 1, StyleContext = new LibraryStyleContext() };
            var settings = new BrainarrSettings { DiscoveryMode = DiscoveryMode.Similar, SamplingStrategy = SamplingStrategy.Balanced, MaxRecommendations = 3 };
            var artists = new List<Artist> { new Artist { Id = 1, Name = "Solo", Added = DateTime.UtcNow.AddDays(-7) } };
            var request = new RecommendationRequest(artists, new List<Album>(), settings, profile.StyleContext, true, 3000, 2200, "openai:gpt-4o-mini", 64000);

            var plan = planner.Plan(profile, request, CancellationToken.None);

            Assert.StartsWith($"{PlannerBuild.ConfigVersion}#", plan.PlanCacheKey);
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

        public void Plan_SetsGeneratedAtTimestamp()
        {
            var planner = new LibraryPromptPlanner(Logger, new NoOpStyleCatalog(), new PlanCache(capacity: 4));
            var profile = new LibraryProfile { TotalArtists = 1, TotalAlbums = 0, StyleContext = new LibraryStyleContext() };
            var settings = new BrainarrSettings { DiscoveryMode = DiscoveryMode.Similar, SamplingStrategy = SamplingStrategy.Balanced, MaxRecommendations = 3 };
            var artists = new List<Artist> { new Artist { Id = 1, Name = "Solo", Added = DateTime.UtcNow.AddDays(-1) } };
            var request = new RecommendationRequest(artists, new List<Album>(), settings, profile.StyleContext, true, 2000, 1500, "openai:gpt-4o-mini", 64000);

            var first = planner.Plan(profile, request, CancellationToken.None);
            Assert.True(first.GeneratedAt > DateTime.MinValue);

            var second = planner.Plan(profile, request, CancellationToken.None);
            Assert.True(second.GeneratedAt > DateTime.MinValue);
            Assert.Equal(first.GeneratedAt, second.GeneratedAt);
        }

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

        [Fact]

        [Trait("Category", "Unit")]

        [Trait("Category", "PromptPlanner")]

        public void PlanCacheKey_Differs_When_MaxSelectedStyles_Changes()

        {

            var catalog = new NormalizingStyleCatalog();

            var planner = new LibraryPromptPlanner(Logger, catalog);



            var artists = CreateArtists();

            var albums = new List<Album>();



            var profileA = new LibraryProfile { TotalArtists = artists.Count, TotalAlbums = albums.Count, StyleContext = CreateStyleContext() };

            var settingsA = new BrainarrSettings

            {

                DiscoveryMode = DiscoveryMode.Similar,

                SamplingStrategy = SamplingStrategy.Balanced,

                MaxRecommendations = 5,

                MaxSelectedStyles = 1,

                StyleFilters = new[] { "shoegaze", "dreampop", "ambient" }

            };



            var requestA = new RecommendationRequest(

                artists,

                albums,

                settingsA,

                profileA.StyleContext,

                recommendArtists: true,

                targetTokens: 3200,

                availableSamplingTokens: 2400,

                modelKey: "openai:gpt-4o-mini",

                contextWindow: 64000);



            var planA = planner.Plan(profileA, requestA, CancellationToken.None);



            var profileB = new LibraryProfile { TotalArtists = artists.Count, TotalAlbums = albums.Count, StyleContext = CreateStyleContext() };

            var settingsB = new BrainarrSettings

            {

                DiscoveryMode = DiscoveryMode.Similar,

                SamplingStrategy = SamplingStrategy.Balanced,

                MaxRecommendations = 5,

                MaxSelectedStyles = 3,

                StyleFilters = new[] { "shoegaze", "dreampop", "ambient" }

            };



            var requestB = new RecommendationRequest(

                artists,

                albums,

                settingsB,

                profileB.StyleContext,

                recommendArtists: true,

                targetTokens: 3200,

                availableSamplingTokens: 2400,

                modelKey: "openai:gpt-4o-mini",

                contextWindow: 64000);



            var planB = planner.Plan(profileB, requestB, CancellationToken.None);



            Assert.NotEqual(planA.PlanCacheKey, planB.PlanCacheKey);

        }



        [Fact]

        [Trait("Category", "Unit")]

        [Trait("Category", "PromptPlanner")]

        public void PlanCacheKey_Differs_When_RelaxStyleMatching_Changes()

        {

            var catalog = new NormalizingStyleCatalog();

            var planner = new LibraryPromptPlanner(Logger, catalog);



            var artists = CreateArtists();

            var albums = new List<Album>();



            var strictProfile = new LibraryProfile { TotalArtists = artists.Count, TotalAlbums = albums.Count, StyleContext = CreateStyleContext() };

            var strictSettings = new BrainarrSettings

            {

                DiscoveryMode = DiscoveryMode.Similar,

                SamplingStrategy = SamplingStrategy.Balanced,

                MaxRecommendations = 5,

                MaxSelectedStyles = 2,

                RelaxStyleMatching = false,

                StyleFilters = new[] { "shoegaze", "dreampop", "ambient" }

            };



            var strictRequest = new RecommendationRequest(

                artists,

                albums,

                strictSettings,

                strictProfile.StyleContext,

                recommendArtists: true,

                targetTokens: 3200,

                availableSamplingTokens: 2400,

                modelKey: "openai:gpt-4o-mini",

                contextWindow: 64000);



            var strictPlan = planner.Plan(strictProfile, strictRequest, CancellationToken.None);



            var relaxedProfile = new LibraryProfile { TotalArtists = artists.Count, TotalAlbums = albums.Count, StyleContext = CreateStyleContext() };

            var relaxedSettings = new BrainarrSettings

            {

                DiscoveryMode = DiscoveryMode.Similar,

                SamplingStrategy = SamplingStrategy.Balanced,

                MaxRecommendations = 5,

                MaxSelectedStyles = 2,

                RelaxStyleMatching = true,

                StyleFilters = new[] { "shoegaze", "dreampop", "ambient" }

            };



            var relaxedRequest = new RecommendationRequest(

                artists,

                albums,

                relaxedSettings,

                relaxedProfile.StyleContext,

                recommendArtists: true,

                targetTokens: 3200,

                availableSamplingTokens: 2400,

                modelKey: "openai:gpt-4o-mini",

                contextWindow: 64000);



            var relaxedPlan = planner.Plan(relaxedProfile, relaxedRequest, CancellationToken.None);



            Assert.NotEqual(strictPlan.PlanCacheKey, relaxedPlan.PlanCacheKey);

        }



        [Fact]

        [Trait("Category", "Unit")]

        [Trait("Category", "PromptPlanner")]

        public void PlanCacheKey_Differs_When_CompressionPolicy_Changes()

        {

            var catalog = new NormalizingStyleCatalog();

            var compressionA = new TestCompressionPolicy(minAlbumsPerGroup: 3, maxRelaxedInflation: 2.0, absoluteCap: 600);

            var compressionB = new TestCompressionPolicy(minAlbumsPerGroup: 4, maxRelaxedInflation: 2.5, absoluteCap: 400);



            var plannerA = new LibraryPromptPlanner(Logger, catalog, compressionPolicy: compressionA);

            var plannerB = new LibraryPromptPlanner(Logger, catalog, compressionPolicy: compressionB);



            var artists = CreateArtists();

            var albums = new List<Album>();



            var profileA = new LibraryProfile { TotalArtists = artists.Count, TotalAlbums = albums.Count, StyleContext = CreateStyleContext() };

            var profileB = new LibraryProfile { TotalArtists = artists.Count, TotalAlbums = albums.Count, StyleContext = CreateStyleContext() };



            var settings = new BrainarrSettings

            {

                DiscoveryMode = DiscoveryMode.Similar,

                SamplingStrategy = SamplingStrategy.Balanced,

                MaxRecommendations = 5,

                MaxSelectedStyles = 2,

                StyleFilters = new[] { "shoegaze", "dreampop", "ambient" }

            };



            var requestA = new RecommendationRequest(

                artists,

                albums,

                settings,

                profileA.StyleContext,

                recommendArtists: true,

                targetTokens: 3200,

                availableSamplingTokens: 2400,

                modelKey: "openai:gpt-4o-mini",

                contextWindow: 64000);



            var requestB = new RecommendationRequest(

                artists,

                albums,

                settings,

                profileB.StyleContext,

                recommendArtists: true,

                targetTokens: 3200,

                availableSamplingTokens: 2400,

                modelKey: "openai:gpt-4o-mini",

                contextWindow: 64000);



            var planA = plannerA.Plan(profileA, requestA, CancellationToken.None);

            var planB = plannerB.Plan(profileB, requestB, CancellationToken.None);



            Assert.NotEqual(planA.PlanCacheKey, planB.PlanCacheKey);

        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void PlanCacheKey_Changes_When_MaxSelectedStyles_Differ()
        {
            var styleCatalog = new NormalizingStyleCatalog();
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, planCache: null);

            var profileA = new LibraryProfile
            {
                TotalArtists = 4,
                TotalAlbums = 0,
                StyleContext = new LibraryStyleContext()
            };
            profileA.StyleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["shoegaze"] = 5,
                ["dreampop"] = 3,
                ["ambient"] = 2
            });
            profileA.StyleContext.SetDominantStyles(new[] { "shoegaze", "dreampop", "ambient" });
            profileA.StyleContext.SetStyleIndex(LibraryStyleIndex.Empty);

            var profileB = new LibraryProfile
            {
                TotalArtists = 4,
                TotalAlbums = 0,
                StyleContext = new LibraryStyleContext()
            };
            profileB.StyleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["shoegaze"] = 5,
                ["dreampop"] = 3,
                ["ambient"] = 2
            });
            profileB.StyleContext.SetDominantStyles(new[] { "shoegaze", "dreampop", "ambient" });
            profileB.StyleContext.SetStyleIndex(LibraryStyleIndex.Empty);

            var artists = Enumerable.Range(1, 4)
                .Select(i => new Artist { Id = i, Name = $"Artist {i}", Added = new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i) })
                .ToList();
            var albums = new List<Album>();

            var settingsA = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 6,
                StyleFilters = new[] { "shoegaze", "dreampop", "ambient" },
                MaxSelectedStyles = 2
            };

            var settingsB = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 6,
                StyleFilters = new[] { "shoegaze", "dreampop", "ambient" },
                MaxSelectedStyles = 3
            };

            var planA = planner.Plan(
                profileA,
                new RecommendationRequest(artists, albums, settingsA, profileA.StyleContext, recommendArtists: true, targetTokens: 3200, availableSamplingTokens: 2400, modelKey: "ollama:mixtral", contextWindow: 32768),
                CancellationToken.None);

            var planB = planner.Plan(
                profileB,
                new RecommendationRequest(artists, albums, settingsB, profileB.StyleContext, recommendArtists: true, targetTokens: 3200, availableSamplingTokens: 2400, modelKey: "ollama:mixtral", contextWindow: 32768),
                CancellationToken.None);

            Assert.NotEqual(planA.PlanCacheKey, planB.PlanCacheKey);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void PlanCacheKey_Changes_When_SamplingShape_Differs()
        {
            var styleCatalog = new NormalizingStyleCatalog();
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, planCache: null);

            var profileBase = new LibraryProfile
            {
                TotalArtists = 4,
                TotalAlbums = 0,
                StyleContext = CreateStyleContext()
            };

            var profileTuned = new LibraryProfile
            {
                TotalArtists = 4,
                TotalAlbums = 0,
                StyleContext = CreateStyleContext()
            };

            var artists = CreateArtists(4);
            var albums = new List<Album>();

            var baseSettings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 6
            };

            var tunedSettings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 6,
                SamplingShape = new SamplingShape
                {
                    Artist = new SamplingShape.ModeShape
                    {
                        Similar = new SamplingShape.ModeDistribution(70, 20),
                        Adjacent = new SamplingShape.ModeDistribution(55, 30),
                        Exploratory = new SamplingShape.ModeDistribution(45, 40)
                    },
                    Album = new SamplingShape.ModeShape
                    {
                        Similar = new SamplingShape.ModeDistribution(60, 25),
                        Adjacent = new SamplingShape.ModeDistribution(50, 30),
                        Exploratory = new SamplingShape.ModeDistribution(35, 45)
                    },
                    MaxAlbumsPerGroupFloor = 4,
                    MaxRelaxedInflation = 2.5
                }
            };

            var basePlan = planner.Plan(
                profileBase,
                new RecommendationRequest(artists, albums, baseSettings, profileBase.StyleContext, recommendArtists: true, targetTokens: 3200, availableSamplingTokens: 2400, modelKey: "openai:gpt-4o-mini", contextWindow: 64000),
                CancellationToken.None);

            var tunedPlan = planner.Plan(
                profileTuned,
                new RecommendationRequest(artists, albums, tunedSettings, profileTuned.StyleContext, recommendArtists: true, targetTokens: 3200, availableSamplingTokens: 2400, modelKey: "openai:gpt-4o-mini", contextWindow: 64000),
                CancellationToken.None);

            Assert.NotEqual(basePlan.PlanCacheKey, tunedPlan.PlanCacheKey);
        }
        private static LibraryStyleContext CreateStyleContext()
        {
            var context = new LibraryStyleContext();
            context.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["shoegaze"] = 5,
                ["dreampop"] = 3,
                ["ambient"] = 2
            });
            context.SetDominantStyles(new[] { "shoegaze", "dreampop", "ambient" });
            context.SetStyleIndex(LibraryStyleIndex.Empty);
            return context;
        }

        private static List<Artist> CreateArtists(int count = 6)
        {
            return Enumerable.Range(1, count)
                .Select(i => new Artist
                {
                    Id = i,
                    Name = $"Artist {i}",
                    Added = DateTime.UtcNow.AddDays(-i)
                })
                .ToList();
        }

        private sealed class TestCompressionPolicy : ICompressionPolicy
        {
            public TestCompressionPolicy(int minAlbumsPerGroup, double maxRelaxedInflation, int absoluteCap)
            {
                MinAlbumsPerGroup = minAlbumsPerGroup;
                MaxRelaxedInflation = maxRelaxedInflation;
                AbsoluteRelaxedCap = absoluteCap;
            }

            public int MinAlbumsPerGroup { get; }
            public double MaxRelaxedInflation { get; }
            public int AbsoluteRelaxedCap { get; }
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
