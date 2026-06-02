using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Performance;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Deterministic over-drop guard for the live, model-agnostic style-enforcement filter
    /// (iter 7). Uses the REAL StyleCatalogService (embedded music_styles.json) and drives the
    /// whole RecommendationPipeline.ProcessAsync style gate with a fixed input derived from a real
    /// GLM run ("Run B": 15 on-style + 12 off-style for styles "edm techno trance").
    ///
    /// THE CONTRACT: the filter must DROP items confidently off EVERY selected style and KEEP
    /// everything else — on-style items (including umbrella descendants like house / big-room /
    /// drum-and-bass under "edm"), and any item it cannot confidently classify (unknown/empty/
    /// unmappable genre → keep-when-ambiguous). It must NEVER over-drop a legitimate recommendation.
    /// </summary>
    public class StyleEnforcementFilterTests
    {
        private static readonly Logger Logger = Helpers.TestLogger.CreateNullLogger();

        private static StyleCatalogService RealCatalog() => new StyleCatalogService(Logger, httpClient: null);

        // A profile whose StyleContext covers the selected styles by slug → IsStyleSeededDiscovery
        // is false, so the library-aligned hard-drop path runs (this is the path under test).
        private static LibraryProfile ProfileCovering(params string[] slugs)
        {
            var ctx = new LibraryStyleContext();
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in slugs) dict[s] = 10;
            ctx.SetCoverage(dict);
            return new LibraryProfile { StyleContext = ctx };
        }

        // ---- Run B fixed set (real GLM output, styles "edm techno trance") -------------------

        // 15 ON-STYLE items: electronic-dance artists. Genres are the kind of free-text label GLM
        // emits. Includes umbrella descendants (house / big-room / DnB / progressive-house) that are
        // NOT literally techno/trance but ARE under the electronic-dance umbrella.
        private static List<Recommendation> OnStyle() => new()
        {
            new Recommendation { Artist = "Daft Punk",      Genre = "house" },
            new Recommendation { Artist = "Eric Prydz",     Genre = "progressive house" },
            new Recommendation { Artist = "Carl Cox",       Genre = "techno" },
            new Recommendation { Artist = "Charlotte de Witte", Genre = "techno" },
            new Recommendation { Artist = "Adam Beyer",     Genre = "minimal techno" },
            new Recommendation { Artist = "Above & Beyond", Genre = "trance" },
            new Recommendation { Artist = "Armin van Buuren", Genre = "trance" },
            new Recommendation { Artist = "Paul van Dyk",   Genre = "progressive trance" },
            new Recommendation { Artist = "Deadmau5",       Genre = "progressive house" },
            new Recommendation { Artist = "Swedish House Mafia", Genre = "big room house" },
            new Recommendation { Artist = "Pendulum",       Genre = "drum and bass" },
            new Recommendation { Artist = "Skrillex",       Genre = "dubstep" },
            new Recommendation { Artist = "Disclosure",     Genre = "deep house" },
            new Recommendation { Artist = "Boris Brejcha",  Genre = "tech house" },
            new Recommendation { Artist = "Aphex Twin",     Genre = "electronic" },
        };

        // 10 CLEARLY-OFF items from the real iter-7 Run B slippage. Every genre here resolves to a
        // catalog slug that is NOT on the ancestor/descendant chain of edm/techno/trance, so each is
        // confidently classified as off EVERY selected style → must be dropped.
        private static List<Recommendation> ClearlyOff() => new()
        {
            new Recommendation { Artist = "John Coltrane",        Genre = "jazz" },
            new Recommendation { Artist = "Thelonious Monk",      Genre = "bebop" },
            new Recommendation { Artist = "Miles Davis",          Genre = "jazz" },
            new Recommendation { Artist = "Glenn Gould",          Genre = "classical" },
            new Recommendation { Artist = "Philip Glass",         Genre = "contemporary classical" },
            new Recommendation { Artist = "Ludwig van Beethoven", Genre = "classical" },
            new Recommendation { Artist = "Radiohead",            Genre = "alternative rock" },
            new Recommendation { Artist = "Pink Floyd",           Genre = "progressive rock" },
            new Recommendation { Artist = "Nina Simone",          Genre = "soul" },
            new Recommendation { Artist = "Johnny Cash",          Genre = "country" },
        };

        // Two real Run B items that the OVER-DROP-SAFE rules intentionally KEEP, not drop:
        //   - The Cure / "post-punk": no catalog slug → unclassifiable → keep-when-ambiguous.
        //   - Stars of the Lid / "ambient": "ambient" is genuinely a child of "electronica" in the
        //     catalog, so under the "edm"→electronica umbrella it is on-style by the hierarchy. A
        //     false-keep is the SAFE direction; we never over-drop a legitimately-electronic item.
        private static List<Recommendation> SafeKeeps() => new()
        {
            new Recommendation { Artist = "The Cure",           Genre = "post-punk" },
            new Recommendation { Artist = "Stars of the Lid",   Genre = "ambient" },
        };

        [Fact]
        public async Task RunB_FixedSet_KeepsAllOnStyle_DropsClearlyOff_AndNeverOverDrops()
        {
            var recs = OnStyle().Concat(ClearlyOff()).Concat(SafeKeeps()).ToList();
            var settings = new BrainarrSettings
            {
                MaxRecommendations = 30,
                RecommendationMode = RecommendationMode.SpecificAlbums,
                StyleFilters = new[] { "edm", "techno", "trance" },
                RelaxStyleMatching = false, // user's stored default; enforcement is over-drop-safe regardless
            };

            var items = await RunStyleFilterAsync(recs, settings,
                // Library covers techno+trance (and electronica via edm) → filter runs, not genre-first.
                ProfileCovering("techno", "trance", "electronica"));

            var kept = items.Select(i => i.Artist).ToHashSet();

            // KEEP all 15 on-style (incl. umbrella descendants house/big-room/DnB/deep-house).
            foreach (var r in OnStyle())
            {
                kept.Should().Contain(r.Artist, $"on-style item '{r.Artist}' ({r.Genre}) must be kept");
            }

            // DROP every clearly-off, resolvable item (jazz/classical/rock/soul/country).
            foreach (var r in ClearlyOff())
            {
                kept.Should().NotContain(r.Artist, $"clearly-off item '{r.Artist}' ({r.Genre}) must be dropped");
            }

            // NEVER over-drop: unclassifiable / electronic-umbrella items are kept.
            foreach (var r in SafeKeeps())
            {
                kept.Should().Contain(r.Artist, $"over-drop-safe item '{r.Artist}' ({r.Genre}) must be kept");
            }

            items.Should().HaveCount(OnStyle().Count + SafeKeeps().Count,
                "the 15 on-style + 2 over-drop-safe items survive; only the 10 clearly-off are dropped");
        }

        [Fact]
        public async Task Umbrella_Edm_KeepsHouseAndBigRoom_AndDropsJazz()
        {
            // "edm" alone (no explicit techno/trance) must still resolve to the electronic umbrella
            // and keep house / big-room descendants while dropping clearly off-style jazz.
            var recs = new List<Recommendation>
            {
                new Recommendation { Artist = "Daft Punk",            Genre = "house" },
                new Recommendation { Artist = "Swedish House Mafia",  Genre = "big room house" },
                new Recommendation { Artist = "Calvin Harris",        Genre = "electro house" },
                new Recommendation { Artist = "John Coltrane",        Genre = "jazz" },
            };
            var settings = new BrainarrSettings
            {
                MaxRecommendations = 30,
                RecommendationMode = RecommendationMode.SpecificAlbums,
                StyleFilters = new[] { "edm" },
                RelaxStyleMatching = false,
            };

            var items = await RunStyleFilterAsync(recs, settings, ProfileCovering("electronica"));
            var kept = items.Select(i => i.Artist).ToHashSet();

            kept.Should().Contain("Daft Punk");
            kept.Should().Contain("Swedish House Mafia");
            kept.Should().Contain("Calvin Harris");
            kept.Should().NotContain("John Coltrane", "jazz is clearly off the EDM umbrella");
        }

        [Fact]
        public async Task KeepWhenAmbiguous_UnknownOrEmptyOrUnmappableGenre_IsKept()
        {
            // Items we cannot confidently classify must be KEPT (never over-drop a legit rec).
            var recs = new List<Recommendation>
            {
                new Recommendation { Artist = "No Genre Artist",     Genre = null },
                new Recommendation { Artist = "Empty Genre Artist",  Genre = "   " },
                new Recommendation { Artist = "Unmappable Artist",   Genre = "zzz-totally-made-up-genre" },
                new Recommendation { Artist = "Off Style Jazz",      Genre = "jazz" },
            };
            var settings = new BrainarrSettings
            {
                MaxRecommendations = 30,
                RecommendationMode = RecommendationMode.SpecificAlbums,
                StyleFilters = new[] { "edm", "techno", "trance" },
                RelaxStyleMatching = false,
            };

            var items = await RunStyleFilterAsync(recs, settings,
                ProfileCovering("techno", "trance", "electronica"));
            var kept = items.Select(i => i.Artist).ToHashSet();

            kept.Should().Contain("No Genre Artist", "null genre is unclassifiable → keep");
            kept.Should().Contain("Empty Genre Artist", "blank genre is unclassifiable → keep");
            kept.Should().Contain("Unmappable Artist", "a genre that maps to no catalog slug is unclassifiable → keep");
            kept.Should().NotContain("Off Style Jazz", "a confidently off-style item is still dropped");
        }

        // ---- harness: drive ONLY the style filter, isolating it from enrichment/dedup/top-up -----

        private static async Task<List<ImportListItemInfo>> RunStyleFilterAsync(
            List<Recommendation> recs, BrainarrSettings settings, LibraryProfile profile)
        {
            var (pipeline, validator, tmp) = CreatePipeline(RealCatalog());
            try
            {
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                    .Returns(new ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });

                return await pipeline.ProcessAsync(
                    settings,
                    recs,
                    profile,
                    new ReviewQueueService(Logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        private static (RecommendationPipeline pipeline, Mock<IRecommendationValidator> validator, string tmp)
            CreatePipeline(IStyleCatalogService styleCatalog)
        {
            var dupFilter = new Mock<IDuplicateFilterService>();
            dupFilter.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
                .Returns((List<ImportListItemInfo> items) => items);
            dupFilter.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                .Returns((List<Recommendation> r, bool _) => r);

            var validator = new Mock<IRecommendationValidator>();

            var gates = new Mock<ISafetyGateService>();
            gates.Setup(g => g.ApplySafetyGates(
                    It.IsAny<List<Recommendation>>(), It.IsAny<BrainarrSettings>(), It.IsAny<ReviewQueueService>(),
                    It.IsAny<RecommendationHistory>(), It.IsAny<Logger>(), It.IsAny<IPerformanceMetrics>(),
                    It.IsAny<CancellationToken>()))
                .Returns<List<Recommendation>, BrainarrSettings, ReviewQueueService, RecommendationHistory, Logger, IPerformanceMetrics, CancellationToken>(
                    (enriched, _, __, ___, ____, _____, ______) => enriched);

            var topUp = new Mock<ITopUpPlanner>();
            // No top-up: keep the count assertions about the FILTER, not refill.
            topUp.Setup(t => t.TopUpAsync(
                    It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<IDuplicationPrevention>(), It.IsAny<LibraryProfile>(),
                    It.IsAny<int>(), It.IsAny<ValidationResult>(), It.IsAny<CancellationToken>(),
                    It.IsAny<List<ImportListItemInfo>>(), It.IsAny<IArtistMbidResolver>(), It.IsAny<IMusicBrainzResolver>()))
                .ReturnsAsync(new List<ImportListItemInfo>());

            var mbids = new Mock<IMusicBrainzResolver>();
            mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                .Returns<List<Recommendation>, CancellationToken>((r, _) => Task.FromResult(r));
            var artists = new Mock<IArtistMbidResolver>();
            artists.Setup(a => a.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                .Returns<List<Recommendation>, CancellationToken>((r, _) => Task.FromResult(r));

            var dedup = new Mock<IDuplicationPrevention>();
            dedup.Setup(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()))
                .Returns((List<ImportListItemInfo> items) => items);
            dedup.Setup(d => d.FilterPreviouslyRecommended(It.IsAny<List<ImportListItemInfo>>(), It.IsAny<ISet<string>>()))
                .Returns((List<ImportListItemInfo> items, ISet<string> _) => items);

            var metrics = new PerformanceMetrics(Logger);
            var tmp = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var history = new RecommendationHistory(Logger, tmp);

            var pipeline = new RecommendationPipeline(
                Logger,
                new Mock<ILibraryAnalyzer>().Object,
                dupFilter.Object,
                validator.Object,
                gates.Object,
                topUp.Object,
                mbids.Object,
                artists.Object,
                dedup.Object,
                metrics,
                history,
                styleCatalog);

            return (pipeline, validator, tmp);
        }
    }
}
