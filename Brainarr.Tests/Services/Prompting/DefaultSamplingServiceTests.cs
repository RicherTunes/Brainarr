using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    public class DefaultSamplingServiceTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void RelaxedExpansion_IsCappedByAbsoluteLimit()
        {
            var catalog = new StubStyleCatalog();
            var contextPolicy = new StubContextPolicy(artistCount: 2000, albumCount: 0);
            var service = new DefaultSamplingService(Logger, catalog, contextPolicy);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["shoegaze"] = 500,
                ["adjacent"] = 1500
            });
            styleContext.SetDominantStyles(new[] { "shoegaze" });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["shoegaze"] = Enumerable.Range(1, 500).ToList(),
                    ["adjacent"] = Enumerable.Range(501, 1000).ToList()
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = new StylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze" },
                expanded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze", "adjacent" },
                entries: new List<StyleEntry> { new StyleEntry { Name = "Shoegaze", Slug = "shoegaze" } },
                adjacent: new List<StyleEntry> { new StyleEntry { Name = "Adjacent", Slug = "adjacent" } },
                coverage: styleContext.StyleCoverage.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
                relaxed: true,
                threshold: 0.75,
                trimmed: new List<string>(),
                inferred: new List<string>());

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 1500,
                RelaxStyleMatching = true
            };

            var artists = Enumerable.Range(1, 1500)
                .Select(id => new Artist { Id = id, Name = $"Artist {id}", Added = DateTime.UtcNow.AddDays(-id) })
                .ToList();

            var sample = service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                settings,
                tokenBudget: 4000,
                seed: 42,
                token: CancellationToken.None);

            Assert.True(sample.ArtistCount <= 1200);
        }

        // ---------------------------------------------------------------------
        // Improvement B: style-seeded zero-coverage dedup fallback.
        // When the user selects styles their library has ZERO exact coverage of
        // (genre-first discovery), the strict + relaxed style match lists are empty,
        // so the library/dedup sample comes back empty and the prompt prints
        // "0 groups" — the model gets no "avoid these duplicates" signal. The
        // fallback surfaces the closest library artists/albums (adjacent styles,
        // then dominant artists) so the dedup audit is non-empty.
        // ---------------------------------------------------------------------

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_StyleSeededZeroCoverage_AdjacentStylesExist_PopulatesDedupArtists()
        {
            // Seed style "lo-fi-hip-hop" has ZERO library coverage; the library only
            // contains the adjacent style "hip-hop" (a sibling the catalog returns from
            // GetSimilarSlugs). Today the artist match list is empty → dedup list empty.
            var catalog = new SimilarityStubStyleCatalog(
                ("lo-fi-hip-hop", new[] { new StyleSimilarity("hip-hop", 0.75, "sibling") }));
            var contextPolicy = new StubContextPolicy(artistCount: 40, albumCount: 0);
            var service = new DefaultSamplingService(Logger, catalog, contextPolicy);

            var styleContext = new LibraryStyleContext();
            // The library does NOT cover the seed style at all (genre-first gate fires).
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["hip-hop"] = 3
            });
            styleContext.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hip-hop" };
            styleContext.ArtistStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hip-hop" };
            styleContext.ArtistStyles[3] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hip-hop" };
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["hip-hop"] = new List<int> { 1, 2, 3 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = new StylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lo-fi-hip-hop" },
                expanded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lo-fi-hip-hop" },
                entries: new List<StyleEntry> { new StyleEntry { Name = "Lo-Fi Hip Hop", Slug = "lo-fi-hip-hop" } },
                adjacent: new List<StyleEntry>(),
                // Renderer's genre-first signal: sum of selected-slug coverage == 0.
                coverage: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["lo-fi-hip-hop"] = 0 },
                relaxed: false,
                threshold: 1.0,
                trimmed: new List<string>(),
                inferred: new List<string>());

            var artists = Enumerable.Range(1, 3)
                .Select(id => CreateArtist(id, $"Hip Hop Artist {id}"))
                .ToList();

            var sample = service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 4000,
                seed: 42,
                token: CancellationToken.None);

            sample.ArtistCount.Should().BeGreaterThan(0,
                "the dedup audit must surface adjacent-style library artists so the prompt is not '0 groups'");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_StyleSeededZeroCoverage_NoAdjacentStyles_FallsBackToDominantArtists()
        {
            // Seed style "lo-fi-hip-hop" has ZERO coverage AND there is no adjacent style
            // in the library (GetSimilarSlugs returns nothing useful). The fallback must
            // still surface the library's dominant artists for the dedup audit.
            var catalog = new SimilarityStubStyleCatalog(); // returns empty similars
            var contextPolicy = new StubContextPolicy(artistCount: 40, albumCount: 0);
            var service = new DefaultSamplingService(Logger, catalog, contextPolicy);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 5
            });
            styleContext.SetDominantStyles(new[] { "rock" });
            styleContext.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.ArtistStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 1, 2 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = new StylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lo-fi-hip-hop" },
                expanded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lo-fi-hip-hop" },
                entries: new List<StyleEntry> { new StyleEntry { Name = "Lo-Fi Hip Hop", Slug = "lo-fi-hip-hop" } },
                adjacent: new List<StyleEntry>(),
                coverage: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["lo-fi-hip-hop"] = 0 },
                relaxed: false,
                threshold: 1.0,
                trimmed: new List<string>(),
                inferred: new List<string>());

            var artists = Enumerable.Range(1, 2)
                .Select(id => CreateArtist(id, $"Rock Artist {id}"))
                .ToList();

            var sample = service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 4000,
                seed: 42,
                token: CancellationToken.None);

            sample.ArtistCount.Should().BeGreaterThan(0,
                "with no adjacent style coverage the dedup audit must fall back to the library's dominant artists");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_StyleSeededZeroCoverage_DoesNotFlipSelectedCoverageToNonZero()
        {
            // CRITICAL INVARIANT: the genre-first gate is sum(Coverage[selectedSlug]) == 0,
            // computed identically by the renderer AND RecommendationPipeline. Enriching the
            // dedup sample must NOT change that value — the fallback artists are a dedup audit
            // ONLY; they must not register as matches against the seed slugs.
            var catalog = new SimilarityStubStyleCatalog(
                ("lo-fi-hip-hop", new[] { new StyleSimilarity("hip-hop", 0.75, "sibling") }));
            var contextPolicy = new StubContextPolicy(artistCount: 40, albumCount: 0);
            var service = new DefaultSamplingService(Logger, catalog, contextPolicy);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["hip-hop"] = 3
            });
            styleContext.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hip-hop" };
            styleContext.ArtistStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hip-hop" };
            styleContext.ArtistStyles[3] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hip-hop" };
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["hip-hop"] = new List<int> { 1, 2, 3 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = new StylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lo-fi-hip-hop" },
                expanded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lo-fi-hip-hop" },
                entries: new List<StyleEntry> { new StyleEntry { Name = "Lo-Fi Hip Hop", Slug = "lo-fi-hip-hop" } },
                adjacent: new List<StyleEntry>(),
                coverage: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["lo-fi-hip-hop"] = 0 },
                relaxed: false,
                threshold: 1.0,
                trimmed: new List<string>(),
                inferred: new List<string>());

            var artists = Enumerable.Range(1, 3)
                .Select(id => CreateArtist(id, $"Hip Hop Artist {id}"))
                .ToList();

            // The renderer computes styleSeeded from selection.Coverage; capture it the same way.
            int SelectedCoverage() =>
                selection.SelectedSlugs.Sum(s => selection.Coverage.TryGetValue(s, out var c) ? c : 0);

            SelectedCoverage().Should().Be(0, "precondition: genre-first gate must be active before sampling");

            service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 4000,
                seed: 42,
                token: CancellationToken.None);

            SelectedCoverage().Should().Be(0,
                "the dedup-fallback must NOT flip the selected-style coverage to non-zero (genre-first gate preserved)");
            selection.MatchedCounts.Should().NotContainKey("lo-fi-hip-hop",
                "fallback dedup artists must not register as seed-style matches");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_StyleSeededZeroCoverage_AdjacentAlbumsExist_PopulatesDedupAlbums()
        {
            // Album-side parity: zero seed coverage but adjacent-style albums exist.
            var catalog = new SimilarityStubStyleCatalog(
                ("lo-fi-hip-hop", new[] { new StyleSimilarity("hip-hop", 0.75, "sibling") }));
            var contextPolicy = new StubContextPolicy(artistCount: 0, albumCount: 40);
            var service = new DefaultSamplingService(Logger, catalog, contextPolicy);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["hip-hop"] = 3
            });
            styleContext.AlbumStyles[10] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hip-hop" };
            styleContext.AlbumStyles[11] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hip-hop" };
            styleContext.AlbumStyles[12] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hip-hop" };
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["hip-hop"] = new List<int> { 10, 11, 12 }
                }));

            var selection = new StylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lo-fi-hip-hop" },
                expanded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lo-fi-hip-hop" },
                entries: new List<StyleEntry> { new StyleEntry { Name = "Lo-Fi Hip Hop", Slug = "lo-fi-hip-hop" } },
                adjacent: new List<StyleEntry>(),
                coverage: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["lo-fi-hip-hop"] = 0 },
                relaxed: false,
                threshold: 1.0,
                trimmed: new List<string>(),
                inferred: new List<string>());

            var albums = new[] { 10, 11, 12 }
                .Select(id => CreateAlbum(id, $"Hip Hop Album {id}", artistId: id))
                .ToList();

            var sample = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 4000,
                seed: 42,
                token: CancellationToken.None);

            sample.AlbumCount.Should().BeGreaterThan(0,
                "the dedup audit must surface adjacent-style library albums for the genre-first path");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_LibraryAligned_DoesNotEngageDedupFallback()
        {
            // Guard against over-reach: when the library DOES cover the selected style,
            // matching proceeds normally and the fallback must not fire (no behavior change).
            var catalog = new SimilarityStubStyleCatalog();
            var contextPolicy = new StubContextPolicy(artistCount: 40, albumCount: 0);
            var service = new DefaultSamplingService(Logger, catalog, contextPolicy);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 2
            });
            styleContext.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.ArtistStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 1, 2 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = new StylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                expanded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                entries: new List<StyleEntry> { new StyleEntry { Name = "Rock", Slug = "rock" } },
                adjacent: new List<StyleEntry>(),
                coverage: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["rock"] = 2 },
                relaxed: false,
                threshold: 1.0,
                trimmed: new List<string>(),
                inferred: new List<string>());

            var artists = Enumerable.Range(1, 2)
                .Select(id => CreateArtist(id, $"Rock Artist {id}"))
                .ToList();

            var sample = service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 4000,
                seed: 42,
                token: CancellationToken.None);

            // Normal style match (rock matches rock) → both artists present with the matched style.
            sample.ArtistCount.Should().Be(2);
            sample.Artists.Should().OnlyContain(a => a.MatchedStyles.Contains("rock"));
        }

        private static Artist CreateArtist(int id, string name)
        {
            var artist = new Artist
            {
                Id = id,
                Added = DateTime.UtcNow.AddDays(-id)
            };
            artist.Metadata.Value.Name = name;
            return artist;
        }

        private static Album CreateAlbum(int id, string title, int artistId)
        {
            return new Album
            {
                Id = id,
                Title = title,
                ArtistId = artistId,
                Added = DateTime.UtcNow.AddDays(-id),
                ReleaseDate = DateTime.UtcNow.AddYears(-1)
            };
        }

        // Stub catalog that returns configured similar-slugs per seed slug (for the
        // adjacent-style dedup fallback) and otherwise behaves like an empty catalog.
        private sealed class SimilarityStubStyleCatalog : IStyleCatalogService
        {
            private readonly Dictionary<string, IReadOnlyList<StyleSimilarity>> _similars;

            public SimilarityStubStyleCatalog(params (string Slug, StyleSimilarity[] Similars)[] entries)
            {
                _similars = entries.ToDictionary(
                    e => e.Slug,
                    e => (IReadOnlyList<StyleSimilarity>)e.Similars,
                    StringComparer.OrdinalIgnoreCase);
            }

            public IReadOnlyList<StyleEntry> GetAll() => Array.Empty<StyleEntry>();
            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => Array.Empty<StyleEntry>();
            public ISet<string> Normalize(IEnumerable<string> selected) => new HashSet<string>(selected ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs, bool relaxParentMatch = false) => false;
            public string? ResolveSlug(string value) => value;
            public StyleEntry? GetBySlug(string slug) => null;
            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug)
                => _similars.TryGetValue(slug ?? string.Empty, out var s) ? s : Array.Empty<StyleSimilarity>();
            public Task RefreshAsync(CancellationToken token = default) => Task.CompletedTask;
        }

        private sealed class StubStyleCatalog : IStyleCatalogService
        {
            public IReadOnlyList<StyleEntry> GetAll() => Array.Empty<StyleEntry>();
            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => Array.Empty<StyleEntry>();
            public ISet<string> Normalize(IEnumerable<string> selected) => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs, bool relaxParentMatch = false) => false;
            public string? ResolveSlug(string value) => value;
            public StyleEntry? GetBySlug(string slug) => null;
            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug) => Array.Empty<StyleSimilarity>();
            public Task RefreshAsync(CancellationToken token = default) => Task.CompletedTask;
        }

        private sealed class StubContextPolicy : IContextPolicy
        {
            private readonly int _artistCount;
            private readonly int _albumCount;

            public StubContextPolicy(int artistCount, int albumCount)
            {
                _artistCount = artistCount;
                _albumCount = albumCount;
            }

            public int DetermineTargetArtistCount(int totalArtists, int tokenBudget) => _artistCount;

            public int DetermineTargetAlbumCount(int totalAlbums, int tokenBudget) => _albumCount;
        }
    }
}
