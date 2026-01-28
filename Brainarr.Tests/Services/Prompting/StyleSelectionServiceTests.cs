using NzbDrone.Core.ImportLists.Brainarr;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    public class StyleSelectionServiceTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [Fact]
        [Trait("Area", "PromptPlanner")]
        public void RelaxedExpansion_IsBounded_ByFactor_AndAbsoluteCap()
        {
            var catalog = new TestStyleCatalog();
            var service = new DefaultStyleSelectionService(Logger, catalog);
            var compressionPolicy = new TestCompressionPolicy(maxRelaxedInflation: 2.0, absoluteCap: 6);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["shoegaze"] = 10
            });
            styleContext.SetStyleIndex(catalog.CreateIndex());

            var settings = new BrainarrSettings
            {
                RelaxStyleMatching = true,
                MaxSelectedStyles = 1,
                StyleFilters = new[] { "shoegaze" }
            };

            var selection = service.Build(new LibraryProfile(), settings, styleContext, compressionPolicy, CancellationToken.None);

            var strictCount = selection.SelectedSlugs.Count;
            var extraCount = selection.ExpandedSlugs.Count(s => !selection.SelectedSlugs.Contains(s));

            Assert.Equal(1, strictCount);
            Assert.True(extraCount <= compressionPolicy.AbsoluteRelaxedCap - strictCount);
            Assert.True(selection.ExpandedSlugs.Count <= compressionPolicy.AbsoluteRelaxedCap);
        }

        private sealed class TestCompressionPolicy : ICompressionPolicy
        {
            public TestCompressionPolicy(double maxRelaxedInflation, int absoluteCap)
            {
                MaxRelaxedInflation = maxRelaxedInflation;
                AbsoluteRelaxedCap = absoluteCap;
            }

            public int MinAlbumsPerGroup => 3;
            public double MaxRelaxedInflation { get; }
            public int AbsoluteRelaxedCap { get; }
        }

        private sealed class TestStyleCatalog : IStyleCatalogService
        {
            private readonly StyleEntry _baseEntry = new StyleEntry { Name = "Shoegaze", Slug = "shoegaze" };

            public IReadOnlyList<StyleEntry> GetAll() => new[] { _baseEntry };
            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => Array.Empty<StyleEntry>();
            public ISet<string> Normalize(IEnumerable<string> slugs) => new HashSet<string>(slugs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            public bool IsMatch(ICollection<string> groupSlugs, ISet<string> selected, bool relaxParentMatch = false) => false;
            public string? ResolveSlug(string value) => value;
            public StyleEntry? GetBySlug(string slug) => new StyleEntry { Name = slug, Slug = slug };

            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug)
            {
                if (!slug.Equals("shoegaze", StringComparison.OrdinalIgnoreCase))
                {
                    return Array.Empty<StyleSimilarity>();
                }

                return Enumerable.Range(1, 20)
                    .Select(i => new StyleSimilarity($"similar-{i}", 0.85, "adjacent"));
            }

            public Task RefreshAsync(CancellationToken token = default) => Task.CompletedTask;

            public LibraryStyleIndex CreateIndex()
            {
                var artistsByStyle = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["shoegaze"] = new[] { 1, 2 },
                    ["similar-1"] = Enumerable.Range(100, 40).ToArray(),
                    ["similar-2"] = Enumerable.Range(200, 40).ToArray(),
                    ["similar-3"] = Enumerable.Range(300, 40).ToArray()
                };
                return new LibraryStyleIndex(artistsByStyle, artistsByStyle);
            }
        }
    }
}
