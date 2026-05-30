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

        private static LibraryProfile ProfileWithGenres(params string[] genres)
        {
            var top = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in genres) top[g] = 10;
            return new LibraryProfile { TopGenres = top };
        }

        // ---- B3: IsStyleSeededDiscovery -------------------------------------------------------

        [Fact]
        public void IsStyleSeeded_LoFiOverRockLibrary_IsTrue()
        {
            // "lo-fi" resolves to lofi-hip-hop; a rock-only library doesn't cover it → genre-first.
            var settings = new BrainarrSettings { StyleFilters = new[] { "lo-fi" } };
            var profile = ProfileWithGenres("Alternative Rock", "Art Rock");
            RecommendationPipeline.IsStyleSeededDiscovery(Catalog(), settings, profile).Should().BeTrue();
        }

        [Fact]
        public void IsStyleSeeded_StyleAlreadyInLibrary_IsFalse()
        {
            // Library already has the style → it's a normal library-aligned filter, not discovery.
            var settings = new BrainarrSettings { StyleFilters = new[] { "lo-fi" } };
            var profile = ProfileWithGenres("Lo-Fi Hip Hop");
            RecommendationPipeline.IsStyleSeededDiscovery(Catalog(), settings, profile).Should().BeFalse();
        }

        [Fact]
        public void IsStyleSeeded_NoStyleFilters_IsFalse()
        {
            var settings = new BrainarrSettings { StyleFilters = Array.Empty<string>() };
            RecommendationPipeline.IsStyleSeededDiscovery(Catalog(), settings, ProfileWithGenres("Rock")).Should().BeFalse();
        }

        [Fact]
        public void IsStyleSeeded_EmptyLibrary_IsTrue()
        {
            // Brand-new/empty library with a style request is pure discovery.
            var settings = new BrainarrSettings { StyleFilters = new[] { "lo-fi" } };
            RecommendationPipeline.IsStyleSeededDiscovery(Catalog(), settings, ProfileWithGenres()).Should().BeTrue();
        }

        [Fact]
        public void IsStyleSeeded_NullCatalog_IsFalse()
        {
            var settings = new BrainarrSettings { StyleFilters = new[] { "lo-fi" } };
            RecommendationPipeline.IsStyleSeededDiscovery(null, settings, ProfileWithGenres("Rock")).Should().BeFalse();
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
