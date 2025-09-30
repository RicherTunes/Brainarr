using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Time;
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
        public void Plan_FallsBackToSimilarMode_WhenNoStylesPresent()
        {
            var styleCatalog = new NoOpStyleCatalog();
            var planner = new LibraryPromptPlanner(Logger, styleCatalog);

            var profile = new LibraryProfile
            {
                TotalArtists = 3,
                TotalAlbums = 2,
                StyleContext = new LibraryStyleContext()
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Exploratory,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 4
            };

            var artists = new List<Artist>
    {
        new Artist { Id = 1, Name = "Alpha", Added = DateTime.UtcNow.AddDays(-2) },
        new Artist { Id = 2, Name = "Bravo", Added = default },
        new Artist { Id = 3, Name = "Charlie", Added = DateTime.UtcNow.AddDays(-30) }
    };

            var albums = new List<Album>
    {
        new Album { Id = 101, ArtistId = 1, Title = "Blue", Added = DateTime.UtcNow.AddDays(-1), ReleaseDate = DateTime.UtcNow.AddYears(-1) },
        new Album { Id = 102, ArtistId = 2, Title = "Green" }
    };

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                profile.StyleContext,
                recommendArtists: true,
                targetTokens: 3500,
                availableSamplingTokens: 2400,
                modelKey: "ollama",
                contextWindow: 32768);

            var plan = planner.Plan(profile, request, CancellationToken.None);

            Assert.Empty(plan.StyleContext.SelectedSlugs);
            Assert.False(plan.StyleCoverageSparse);
            Assert.False(plan.RelaxedStyleMatching);
            Assert.True(plan.Sample.ArtistCount > 0);
            Assert.Equal(plan.Sample.ArtistCount, plan.Sample.Artists.Select(a => a.ArtistId).Distinct().Count());
        }

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
        public void PlanCache_ExpiresEntriesAfterTtl()
        {
            var styleCatalog = new NoOpStyleCatalog();
            var clock = new ManualClock(DateTime.UtcNow);
            var cache = new PlanCache(capacity: 4, metrics: null, clock: clock);
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, cache, planCacheTtl: TimeSpan.FromMilliseconds(25));

            var profile = new LibraryProfile
            {
                TotalArtists = 1,
                TotalAlbums = 0,
                StyleContext = new LibraryStyleContext()
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 3
            };

            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "ArtistA", Added = DateTime.UtcNow.AddDays(-10) }
            };

            var albums = new List<Album>();

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                profile.StyleContext,
                recommendArtists: true,
                targetTokens: 2000,
                availableSamplingTokens: 1600,
                modelKey: "openai:gpt-4",
                contextWindow: 64000);

            var initialPlan = planner.Plan(profile, request, CancellationToken.None);
            Assert.False(initialPlan.FromCache);

            clock.Advance(TimeSpan.FromMilliseconds(30));

            Assert.False(cache.TryGet(initialPlan.PlanCacheKey, out _));

            var refreshedPlan = planner.Plan(profile, request, CancellationToken.None);

            var cachedPlan = planner.Plan(profile, request, CancellationToken.None);
            Assert.True(cachedPlan.FromCache);
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

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_OrdersAlbumsByDeterministicTieBreaks()
        {
            var styleCatalog = new StaticStyleCatalog(new StyleEntry { Name = "Alt", Slug = "alt" });
            var cache = new PlanCache(capacity: 4);
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, cache);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["alt"] = 6
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = Array.Empty<int>()
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 201, 202, 203, 204, 205, 206 }
                }));

            foreach (var albumId in new[] { 201, 202, 203, 204, 205, 206 })
            {
                styleContext.AlbumStyles[albumId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            }

            var profile = new LibraryProfile
            {
                TotalArtists = 2,
                TotalAlbums = 6,
                StyleContext = styleContext
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 6,
                StyleFilters = new[] { "alt" }
            };

            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "ArtistA" },
                new Artist { Id = 2, Name = "ArtistB" }
            };

            var now = DateTime.UtcNow;
            var albums = new List<Album>
            {
                new Album { Id = 201, ArtistId = 1, Title = "Zenith", Added = now.AddDays(-1), ReleaseDate = now.AddYears(-3) },
                new Album { Id = 202, ArtistId = 1, Title = "Aurora", Added = now.AddDays(-2), ReleaseDate = now.AddYears(-1) },
                new Album { Id = 203, ArtistId = 1, Title = "Blaze", Added = now.AddDays(-2), ReleaseDate = now.AddYears(-2) },
                new Album { Id = 204, ArtistId = 1, Title = "Cascade", Added = now.AddDays(-2), ReleaseDate = now.AddYears(-2) },
                new Album { Id = 205, ArtistId = 2, Title = "Echo", Added = now.AddDays(-2), ReleaseDate = now.AddYears(-2) },
                new Album { Id = 206, ArtistId = 2, Title = "Echo", Added = now.AddDays(-2), ReleaseDate = now.AddYears(-2) }
            };

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                styleContext,
                recommendArtists: false,
                targetTokens: 3200,
                availableSamplingTokens: 2400,
                modelKey: "openai:gpt-4",
                contextWindow: 64000);

            var plan = planner.Plan(profile, request, CancellationToken.None);
            var orderedAlbumIds = plan.Sample.Albums.Select(a => a.AlbumId).ToArray();

            Assert.Equal(new[] { 201, 202, 203 }, orderedAlbumIds);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_WithEquivalentStyleOrdering_IsDeterministic()
        {
            var styleCatalog = new StaticStyleCatalog(
                new StyleEntry { Name = "Alt", Slug = "alt" },
                new StyleEntry { Name = "Shoegaze", Slug = "shoegaze" });
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, planCache: null);

            var context = CreateStyleContext();
            var profile = new LibraryProfile
            {
                TotalArtists = 3,
                TotalAlbums = 4,
                StyleContext = context
            };

            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "ArtistA", Added = DateTime.UtcNow.AddDays(-10) },
                new Artist { Id = 2, Name = "ArtistB", Added = DateTime.UtcNow.AddDays(-5) },
                new Artist { Id = 3, Name = "ArtistC", Added = DateTime.UtcNow.AddDays(-2) }
            };

            var albums = new List<Album>
            {
                new Album { Id = 11, ArtistId = 1, Title = "Album1", Added = DateTime.UtcNow.AddDays(-20), ReleaseDate = DateTime.UtcNow.AddYears(-5) },
                new Album { Id = 21, ArtistId = 2, Title = "Album2", Added = DateTime.UtcNow.AddDays(-15), ReleaseDate = DateTime.UtcNow.AddYears(-4) },
                new Album { Id = 22, ArtistId = 2, Title = "Album3", Added = DateTime.UtcNow.AddDays(-12), ReleaseDate = DateTime.UtcNow.AddYears(-3) },
                new Album { Id = 31, ArtistId = 3, Title = "Album4", Added = DateTime.UtcNow.AddDays(-7), ReleaseDate = DateTime.UtcNow.AddYears(-2) }
            };

            var settingsA = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 5,
                StyleFilters = new[] { "shoegaze", "alt" },
                RelaxStyleMatching = true,
                MaxSelectedStyles = 5
            };

            var requestA = new RecommendationRequest(
                artists,
                albums,
                settingsA,
                context,
                recommendArtists: false,
                targetTokens: 3800,
                availableSamplingTokens: 2600,
                modelKey: "openai:gpt-4",
                contextWindow: 64000);

            var planA = planner.Plan(profile, requestA, CancellationToken.None);

            var settingsB = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 5,
                StyleFilters = new[] { "Alt", "Shoegaze" },
                RelaxStyleMatching = true,
                MaxSelectedStyles = 5
            };

            var requestB = new RecommendationRequest(
                artists,
                albums,
                settingsB,
                context,
                recommendArtists: false,
                targetTokens: 3800,
                availableSamplingTokens: 2600,
                modelKey: "openai:gpt-4",
                contextWindow: 64000);

            var planB = planner.Plan(profile, requestB, CancellationToken.None);

            Assert.Equal(planA.PlanCacheKey, planB.PlanCacheKey);
            Assert.Equal(planA.SampleFingerprint, planB.SampleFingerprint);
            Assert.Equal(planA.SampleSeed, planB.SampleSeed);
            Assert.Equal(planA.Sample.ArtistCount, planB.Sample.ArtistCount);
            Assert.Equal(planA.Sample.AlbumCount, planB.Sample.AlbumCount);
            Assert.Equal(
                planA.Sample.Artists.Select(a => a.ArtistId),
                planB.Sample.Artists.Select(a => a.ArtistId));
            Assert.Equal(
                planA.Sample.Artists.Select(a => string.Join(',', a.MatchedStyles.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))),
                planB.Sample.Artists.Select(a => string.Join(',', a.MatchedStyles.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))));
            Assert.Equal(planA.TrimmedStyles, planB.TrimmedStyles);
            Assert.Equal(
                planA.StyleContext.SelectedSlugs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase),
                planB.StyleContext.SelectedSlugs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_NormalizesStyleEntriesAlphabetically()
        {
            var styleCatalog = new StaticStyleCatalog(
                new StyleEntry { Name = "Alt Rock", Slug = "alt" },
                new StyleEntry { Name = "Dream Pop", Slug = "dreampop" },
                new StyleEntry { Name = "Shoegaze", Slug = "shoegaze" });
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, planCache: null);

            var context = new LibraryStyleContext();
            context.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["alt"] = 5,
                ["dreampop"] = 3,
                ["shoegaze"] = 4
            });

            context.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 1, 2 },
                    ["dreampop"] = new[] { 3 },
                    ["shoegaze"] = new[] { 2, 3 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 11, 21 },
                    ["dreampop"] = new[] { 32 },
                    ["shoegaze"] = new[] { 22, 31 }
                }));

            context.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.ArtistStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt", "shoegaze" };
            context.ArtistStyles[3] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze", "dreampop" };

            context.AlbumStyles[11] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.AlbumStyles[21] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.AlbumStyles[22] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze" };
            context.AlbumStyles[31] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze" };
            context.AlbumStyles[32] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dreampop" };

            var profile = new LibraryProfile
            {
                TotalArtists = 3,
                TotalAlbums = 5,
                StyleContext = context
            };

            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "ArtistA", Added = DateTime.UtcNow.AddDays(-10) },
                new Artist { Id = 2, Name = "ArtistB", Added = DateTime.UtcNow.AddDays(-6) },
                new Artist { Id = 3, Name = "ArtistC", Added = DateTime.UtcNow.AddDays(-3) }
            };

            var albums = new List<Album>
            {
                new Album { Id = 11, ArtistId = 1, Title = "Alt Album", Added = DateTime.UtcNow.AddDays(-20), ReleaseDate = DateTime.UtcNow.AddYears(-4) },
                new Album { Id = 21, ArtistId = 2, Title = "Alt Companion", Added = DateTime.UtcNow.AddDays(-18), ReleaseDate = DateTime.UtcNow.AddYears(-3) },
                new Album { Id = 22, ArtistId = 2, Title = "Shoegaze Entry", Added = DateTime.UtcNow.AddDays(-15), ReleaseDate = DateTime.UtcNow.AddYears(-2) },
                new Album { Id = 31, ArtistId = 3, Title = "Dream Sequence", Added = DateTime.UtcNow.AddDays(-12), ReleaseDate = DateTime.UtcNow.AddYears(-1) },
                new Album { Id = 32, ArtistId = 3, Title = "Night Bloom", Added = DateTime.UtcNow.AddDays(-11), ReleaseDate = DateTime.UtcNow.AddYears(-1) }
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 5,
                StyleFilters = new[] { "shoegaze", "Alt", "dreampop" },
                MaxSelectedStyles = 5
            };

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                context,
                recommendArtists: false,
                targetTokens: 3600,
                availableSamplingTokens: 2400,
                modelKey: "openai:gpt-4",
                contextWindow: 64000);

            var plan = planner.Plan(profile, request, CancellationToken.None);
            var names = plan.StyleContext.Entries.Select(e => e.Name).ToArray();

            Assert.Equal(new[] { "Alt Rock", "Dream Pop", "Shoegaze" }, names);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_AssignsSyntheticArtistNameWhenMetadataMissing()
        {
            var styleCatalog = new StaticStyleCatalog(new StyleEntry { Name = "Alt Rock", Slug = "alt" });
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, planCache: null);

            var context = new LibraryStyleContext();
            context.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["alt"] = 1
            });

            context.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 1 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 10 }
                }));

            context.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.AlbumStyles[10] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };

            var profile = new LibraryProfile
            {
                TotalArtists = 1,
                TotalAlbums = 1,
                StyleContext = context
            };

            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Primary Artist", Added = DateTime.UtcNow.AddDays(-10) }
            };

            var albums = new List<Album>
            {
                new Album
                {
                    Id = 10,
                    ArtistId = 1,
                    Title = "Album Title",
                    Added = DateTime.UtcNow.AddDays(-5),
                    ReleaseDate = DateTime.UtcNow.AddYears(-1),
                    ArtistMetadata = null
                }
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 3,
                StyleFilters = new[] { "alt" }
            };

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                context,
                recommendArtists: false,
                targetTokens: 2400,
                availableSamplingTokens: 1800,
                modelKey: "openai:gpt-4",
                contextWindow: 64000);

            var plan = planner.Plan(profile, request, CancellationToken.None);

            var sampledAlbum = Assert.Single(plan.Sample.Albums);
            Assert.Equal("Artist 1", sampledAlbum.ArtistName);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void PlanCache_InvalidateByFingerprint_EvictsEntry()
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

            cache.InvalidateByFingerprint(firstPlan.LibraryFingerprint);

            var thirdPlan = planner.Plan(profile, request, CancellationToken.None);
            Assert.False(thirdPlan.FromCache);
            Assert.Equal(firstPlan.SampleFingerprint, thirdPlan.SampleFingerprint);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_WhenCancelled_ThrowsAndLeavesCacheEmpty()
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

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => planner.Plan(profile, request, cts.Token));

            var plan = planner.Plan(profile, request, CancellationToken.None);
            Assert.False(plan.FromCache);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_WithTiedArtistScores_UsesDeterministicOrdering()
        {
            var styleCatalog = new NoOpStyleCatalog();
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, planCache: null);

            var profile = new LibraryProfile
            {
                TotalArtists = 6,
                TotalAlbums = 0,
                StyleContext = new LibraryStyleContext()
            };

            var baseline = DateTime.UtcNow.AddDays(-30);
            var artists = new List<Artist>
            {
                new Artist { Id = 5, Name = "Beta", Added = baseline },
                new Artist { Id = 1, Name = "Alpha", Added = baseline },
                new Artist { Id = 6, Name = "Zeta", Added = baseline },
                new Artist { Id = 4, Name = "Delta", Added = baseline },
                new Artist { Id = 3, Name = "Gamma", Added = baseline },
                new Artist { Id = 2, Name = "Alpha", Added = baseline }
            };

            var albums = new List<Album>();

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 12
            };

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                profile.StyleContext!,
                recommendArtists: true,
                targetTokens: 5200,
                availableSamplingTokens: 3600,
                modelKey: "lmstudio:stable",
                contextWindow: 32768);

            var plan = planner.Plan(profile, request, CancellationToken.None);
            var firstThree = plan.Sample.Artists.Take(3).Select(a => a.ArtistId).ToArray();

            Assert.Equal(new[] { 1, 2, 5 }, firstThree);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_WithTiedAlbumScores_OrdersByTitleAndId()
        {
            var styleCatalog = new NoOpStyleCatalog();
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, planCache: null);

            var profile = new LibraryProfile
            {
                TotalArtists = 4,
                TotalAlbums = 8,
                StyleContext = new LibraryStyleContext()
            };

            var baseline = DateTime.UtcNow.AddDays(-45);
            var artists = new List<Artist>
            {
                new Artist { Id = 10, Name = "Artist A", Added = baseline },
                new Artist { Id = 11, Name = "Artist B", Added = baseline },
                new Artist { Id = 12, Name = "Artist C", Added = baseline },
                new Artist { Id = 13, Name = "Artist D", Added = baseline }
            };

            var release = DateTime.UtcNow.AddYears(-5);
            var albums = new List<Album>
            {
                new Album { Id = 403, ArtistId = 10, Title = "Alpha", Added = baseline, ReleaseDate = release },
                new Album { Id = 404, ArtistId = 10, Title = "Alpha", Added = baseline, ReleaseDate = release },
                new Album { Id = 405, ArtistId = 11, Title = "Beta", Added = baseline, ReleaseDate = release },
                new Album { Id = 406, ArtistId = 11, Title = "Delta", Added = baseline, ReleaseDate = release },
                new Album { Id = 407, ArtistId = 12, Title = "Epsilon", Added = baseline, ReleaseDate = release },
                new Album { Id = 408, ArtistId = 12, Title = "Gamma", Added = baseline, ReleaseDate = release },
                new Album { Id = 409, ArtistId = 13, Title = "Omega", Added = baseline, ReleaseDate = release },
                new Album { Id = 410, ArtistId = 13, Title = "Zeta", Added = baseline, ReleaseDate = release }
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 12
            };

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                profile.StyleContext!,
                recommendArtists: false,
                targetTokens: 5400,
                availableSamplingTokens: 3600,
                modelKey: "lmstudio:stable",
                contextWindow: 32768);

            var plan = planner.Plan(profile, request, CancellationToken.None);
            var firstFour = plan.Sample.Albums.Take(4).Select(a => a.AlbumId).ToArray();

            Assert.Equal(new[] { 403, 404, 405, 406 }, firstFour);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_NormalizesNullAlbumAddedDates()
        {
            var styleCatalog = new NoOpStyleCatalog();
            var planner = new LibraryPromptPlanner(Logger, styleCatalog, planCache: null);

            var profile = new LibraryProfile
            {
                TotalArtists = 2,
                TotalAlbums = 2,
                StyleContext = new LibraryStyleContext()
            };

            var now = DateTime.UtcNow;
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Artist A", Added = now.AddDays(-30) },
                new Artist { Id = 2, Name = "Artist B", Added = now.AddDays(-10) }
            };

            var albums = new List<Album>
            {
                new Album { Id = 201, ArtistId = 1, Title = "Catalog Classic", Added = default, ReleaseDate = now.AddYears(-5) },
                new Album { Id = 202, ArtistId = 2, Title = "Fresh Press", Added = now.AddDays(-1), ReleaseDate = now.AddYears(-1) }
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 6
            };

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                profile.StyleContext!,
                recommendArtists: false,
                targetTokens: 4200,
                availableSamplingTokens: 2800,
                modelKey: "lmstudio:stable",
                contextWindow: 32768);

            var plan = planner.Plan(profile, request, CancellationToken.None);

            Assert.True(plan.Sample.Albums.Count >= 2);
            Assert.Equal(202, plan.Sample.Albums.First().AlbumId);
            Assert.Equal(201, plan.Sample.Albums.Last().AlbumId);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptPlanner")]
        public void Plan_IsDeterministic_WhenInputsPermuted()
        {
            var styleCatalog = new NoOpStyleCatalog();
            var planner = new LibraryPromptPlanner(Logger, styleCatalog);

            var profile = new LibraryProfile
            {
                TotalArtists = 6,
                TotalAlbums = 4,
                StyleContext = new LibraryStyleContext()
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 6
            };

            var artists = Enumerable.Range(1, 6)
                .Select(i => new Artist { Id = i, Name = $"Artist {i}", Added = DateTime.UtcNow.AddDays(-i) })
                .ToList();
            var albums = Enumerable.Range(1, 4)
                .Select(i => new Album { Id = 100 + i, ArtistId = (i % 3) + 1, Title = $"Album {i}", Added = DateTime.UtcNow.AddDays(-i) })
                .ToList();

            var baselineRequest = new RecommendationRequest(artists, albums, settings, profile.StyleContext, true, 3200, 2400, "openai:gpt-4o-mini", 64000);
            var baselinePlan = planner.Plan(profile, baselineRequest, CancellationToken.None);

            for (var iteration = 0; iteration < 5; iteration++)
            {
                var permutedArtists = artists.OrderBy(_ => Guid.NewGuid()).ToList();
                var permutedAlbums = albums.OrderBy(_ => Guid.NewGuid()).ToList();
                var request = new RecommendationRequest(permutedArtists, permutedAlbums, settings, profile.StyleContext, true, 3200, 2400, "openai:gpt-4o-mini", 64000);
                var plan = planner.Plan(profile, request, CancellationToken.None);

                Assert.Equal(baselinePlan.SampleFingerprint, plan.SampleFingerprint);
                Assert.Equal(baselinePlan.PlanCacheKey, plan.PlanCacheKey);
            }
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

        private sealed class ManualClock : IClock
        {
            private DateTime _utcNow;

            public ManualClock(DateTime start)
            {
                _utcNow = start;
            }

            public DateTime UtcNow => _utcNow;

            public void Advance(TimeSpan delta)
            {
                _utcNow = _utcNow.Add(delta);
            }
        }
        private static LibraryStyleContext CreateStyleContext()
        {
            var context = new LibraryStyleContext();
            context.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["alt"] = 3,
                ["shoegaze"] = 2
            });

            context.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 1, 2 },
                    ["shoegaze"] = new[] { 2, 3 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 11, 21 },
                    ["shoegaze"] = new[] { 22, 31 }
                }));

            context.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.ArtistStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt", "shoegaze" };
            context.ArtistStyles[3] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze" };

            context.AlbumStyles[11] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.AlbumStyles[21] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.AlbumStyles[22] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze" };
            context.AlbumStyles[31] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze" };

            return context;
        }
    }
}
