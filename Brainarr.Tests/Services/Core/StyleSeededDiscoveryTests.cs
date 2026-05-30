using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Phase B: style-seeded ("genre-first") discovery — recommend artists OF a requested style
    /// (e.g. "lo-fi") even when the library has no matching artists, plus freestyle passthrough for
    /// styles not in the catalog. Uses the real StyleCatalogService (loads music_styles.json).
    /// </summary>
    public class StyleSeededDiscoveryTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static StyleCatalogService Catalog() => new StyleCatalogService(Logger, httpClient: null);

        // Builds a profile whose StyleContext coverage is keyed by SLUG — the same signal the prompt
        // renderer sums, so the detector and the renderer stay in lockstep.
        private static LibraryProfile ProfileWithCoverage(params (string slug, int count)[] coverage)
        {
            var ctx = new LibraryStyleContext();
            if (coverage.Length > 0)
            {
                var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var (slug, count) in coverage) dict[slug] = count;
                ctx.SetCoverage(dict);
            }
            return new LibraryProfile { StyleContext = ctx };
        }

        // ---- B3: IsStyleSeededDiscovery -------------------------------------------------------

        [Fact]
        public void IsStyleSeeded_LoFiOverRockLibrary_IsTrue()
        {
            // "lo-fi" resolves to lofi-hip-hop; a rock library has zero lofi-hip-hop coverage → genre-first.
            var settings = new BrainarrSettings { StyleFilters = new[] { "lo-fi" } };
            var profile = ProfileWithCoverage(("alternative-rock", 12), ("art-rock", 8));
            RecommendationPipeline.IsStyleSeededDiscovery(Catalog(), settings, profile).Should().BeTrue();
        }

        [Fact]
        public void IsStyleSeeded_StyleAlreadyInLibrary_IsFalse()
        {
            // Library already covers the style (by slug) → normal library-aligned filter, not discovery.
            var settings = new BrainarrSettings { StyleFilters = new[] { "lo-fi" } };
            var profile = ProfileWithCoverage(("lofi-hip-hop", 9));
            RecommendationPipeline.IsStyleSeededDiscovery(Catalog(), settings, profile).Should().BeFalse();
        }

        [Fact]
        public void IsStyleSeeded_ParentSelected_ChildInLibrary_MatchesRenderer_IsTrue()
        {
            // Regression for the renderer/pipeline disagreement the adversarial review found: selecting
            // a parent ("rock") over a library that only has a child genre. The renderer sums
            // coverage["rock"]==0 → genre-first; the detector MUST agree (not parent-relax into a match).
            var settings = new BrainarrSettings { StyleFilters = new[] { "rock" } };
            var profile = ProfileWithCoverage(("art-rock", 10), ("post-rock", 6));
            RecommendationPipeline.IsStyleSeededDiscovery(Catalog(), settings, profile).Should().BeTrue();
        }

        [Fact]
        public void IsStyleSeeded_NoStyleFilters_IsFalse()
        {
            var settings = new BrainarrSettings { StyleFilters = Array.Empty<string>() };
            RecommendationPipeline.IsStyleSeededDiscovery(Catalog(), settings, ProfileWithCoverage(("rock", 10))).Should().BeFalse();
        }

        [Fact]
        public void IsStyleSeeded_EmptyLibrary_IsTrue()
        {
            // Brand-new/empty library with a style request is pure discovery.
            var settings = new BrainarrSettings { StyleFilters = new[] { "lo-fi" } };
            RecommendationPipeline.IsStyleSeededDiscovery(Catalog(), settings, ProfileWithCoverage()).Should().BeTrue();
        }

        [Fact]
        public void IsStyleSeeded_NullCatalog_IsFalse()
        {
            var settings = new BrainarrSettings { StyleFilters = new[] { "lo-fi" } };
            RecommendationPipeline.IsStyleSeededDiscovery(null, settings, ProfileWithCoverage(("rock", 10))).Should().BeFalse();
        }

        // ---- B2: freestyle passthrough --------------------------------------------------------

        [Fact]
        public void Freestyle_NonCatalogTerm_BecomesSeedAnchor()
        {
            var service = new DefaultStyleSelectionService(Logger, Catalog());
            var settings = new BrainarrSettings { StyleFilters = new[] { "vaporwave" }, MaxSelectedStyles = 10 };

            var selection = service.Build(new LibraryProfile(), settings, new LibraryStyleContext(),
                new DefaultCompressionPolicy(), CancellationToken.None);

            selection.HasStyles.Should().BeTrue("a typed freestyle style must still seed the prompt");
            selection.Entries.Should().Contain(e => e.Name == "vaporwave");
            selection.Sparse.Should().BeTrue("a freestyle style has no library coverage → genre-first");
        }

        [Fact]
        public void Freestyle_DoesNotInferLibraryStyles_WhenUserTypedFreeText()
        {
            // User typed a freestyle term in Similar mode; we must honor it, not silently replace it
            // with dominant library styles.
            var service = new DefaultStyleSelectionService(Logger, Catalog());
            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["rock"] = 50 });

            var settings = new BrainarrSettings
            {
                StyleFilters = new[] { "vaporwave" },
                DiscoveryMode = DiscoveryMode.Similar,
                MaxSelectedStyles = 10
            };

            var selection = service.Build(new LibraryProfile(), settings, styleContext,
                new DefaultCompressionPolicy(), CancellationToken.None);

            selection.Entries.Should().Contain(e => e.Name == "vaporwave");
            selection.Entries.Should().NotContain(e => e.Slug == "rock",
                "library inference must not override the user's explicit freestyle selection");
        }

        [Fact]
        public void Freestyle_NotStarvedByCatalogStyles_WhenBudgetTight()
        {
            // Adversarial-review finding: with MaxSelectedStyles filled by catalog picks, freestyle was
            // silently dropped. Freestyle gets its own allowance, so it survives a tight budget.
            var service = new DefaultStyleSelectionService(Logger, Catalog());
            var settings = new BrainarrSettings
            {
                StyleFilters = new[] { "rock", "vaporwave" },
                MaxSelectedStyles = 1
            };

            var selection = service.Build(new LibraryProfile(), settings, new LibraryStyleContext(),
                new DefaultCompressionPolicy(), CancellationToken.None);

            selection.Entries.Should().Contain(e => e.Name == "vaporwave",
                "an explicitly-typed freestyle term must not be starved by catalog selections");
        }

        [Fact]
        public void Freestyle_CatalogAlias_ResolvesNormally_NotTreatedAsFreestyle()
        {
            // "Lo-fi" is an alias of lofi-hip-hop — it should resolve to the real slug, not a
            // "freestyle:" pseudo-slug.
            var service = new DefaultStyleSelectionService(Logger, Catalog());
            var settings = new BrainarrSettings { StyleFilters = new[] { "Lo-fi" }, MaxSelectedStyles = 10 };

            var selection = service.Build(new LibraryProfile(), settings, new LibraryStyleContext(),
                new DefaultCompressionPolicy(), CancellationToken.None);

            selection.SelectedSlugs.Should().Contain("lofi-hip-hop");
            selection.SelectedSlugs.Should().NotContain(s => s.StartsWith("freestyle:"));
        }
    }
}
