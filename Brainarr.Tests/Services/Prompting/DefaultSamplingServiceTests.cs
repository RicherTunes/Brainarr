using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
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

        private sealed class StubStyleCatalog : IStyleCatalogService
        {
            public IReadOnlyList<StyleEntry> GetAll() => Array.Empty<StyleEntry>();
            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => Array.Empty<StyleEntry>();
            public ISet<string> Normalize(IEnumerable<string> selected) => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs) => false;
            public string? ResolveSlug(string value) => value;
            public StyleEntry? GetBySlug(string slug) => null;
            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug) => Array.Empty<StyleSimilarity>();
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
