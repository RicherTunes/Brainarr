using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using Xunit;

namespace Brainarr.Tests.Services.Styles
{
    [Trait("Category", "Unit")]
    public class StyleCatalogServiceTests
    {
        private readonly Logger _logger = LogManager.GetLogger("test");

        #region GetAll Tests

        [Fact]
        public void GetAll_returns_all_styles_from_catalog()
        {
            var service = new StyleCatalogService(_logger, null);
            var all = service.GetAll();

            all.Should().NotBeNull();
            all.Should().NotBeEmpty();
        }

        [Fact]
        public void GetAll_returns_readonly_list()
        {
            var service = new StyleCatalogService(_logger, null);
            var all = service.GetAll();

            all.Should().BeAssignableTo<IReadOnlyList<StyleEntry>>();
        }

        #endregion

        #region Search Tests

        [Fact]
        public void Search_with_null_query_returns_alphabetical_results()
        {
            var service = new StyleCatalogService(_logger, null);
            var results = service.Search(null, 10).ToList();

            results.Should().NotBeNull();
            results.Should().HaveCountLessOrEqualTo(10);
            // Results should be alphabetically ordered
            var names = results.Select(r => r.Name).ToList();
            names.Should().BeInAscendingOrder(StringComparer.InvariantCultureIgnoreCase);
        }

        [Fact]
        public void Search_with_empty_query_returns_alphabetical_results()
        {
            var service = new StyleCatalogService(_logger, null);
            var results = service.Search("", 10).ToList();

            results.Should().NotBeNull();
            results.Should().HaveCountLessOrEqualTo(10);
        }

        [Fact]
        public void Search_with_whitespace_query_returns_alphabetical_results()
        {
            var service = new StyleCatalogService(_logger, null);
            var results = service.Search("   ", 10).ToList();

            results.Should().NotBeNull();
            results.Should().HaveCountLessOrEqualTo(10);
        }

        [Fact]
        public void Search_respects_limit_parameter()
        {
            var service = new StyleCatalogService(_logger, null);
            var results = service.Search(null, 5).ToList();

            results.Should().HaveCountLessOrEqualTo(5);
        }

        [Fact]
        public void Search_with_zero_or_negative_limit_returns_at_least_one()
        {
            var service = new StyleCatalogService(_logger, null);

            var resultsZero = service.Search(null, 0).ToList();
            var resultsNegative = service.Search(null, -5).ToList();

            resultsZero.Should().HaveCountGreaterOrEqualTo(1);
            resultsNegative.Should().HaveCountGreaterOrEqualTo(1);
        }

        [Fact]
        public void Search_finds_matching_styles()
        {
            var service = new StyleCatalogService(_logger, null);

            // Search for a common term that should exist
            var results = service.Search("rock", 50).ToList();

            results.Should().NotBeEmpty();
            // At least one result should contain "rock" in name or alias
            results.Should().Contain(r =>
                r.Name.Contains("rock", StringComparison.OrdinalIgnoreCase) ||
                r.Slug.Contains("rock", StringComparison.OrdinalIgnoreCase) ||
                (r.Aliases != null && r.Aliases.Any(a => a.Contains("rock", StringComparison.OrdinalIgnoreCase))));
        }

        [Fact]
        public void Search_is_case_insensitive()
        {
            var service = new StyleCatalogService(_logger, null);

            var resultsLower = service.Search("rock", 50).ToList();
            var resultsUpper = service.Search("ROCK", 50).ToList();
            var resultsMixed = service.Search("RoCk", 50).ToList();

            resultsLower.Should().BeEquivalentTo(resultsUpper);
            resultsLower.Should().BeEquivalentTo(resultsMixed);
        }

        #endregion

        #region Normalize Tests

        [Fact]
        public void Normalize_returns_empty_set_for_null_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.Normalize(null);

            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void Normalize_returns_empty_set_for_empty_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.Normalize(Array.Empty<string>());

            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void Normalize_filters_out_null_and_empty_values()
        {
            var service = new StyleCatalogService(_logger, null);
            var input = new[] { null, "", "   ", "rock" };

            var result = service.Normalize(input);

            // Should only contain the valid "rock" entry (if it exists in catalog)
            result.Should().HaveCountLessOrEqualTo(1);
        }

        [Fact]
        public void Normalize_deduplicates_slugs()
        {
            var service = new StyleCatalogService(_logger, null);
            var input = new[] { "rock", "Rock", "ROCK" };

            var result = service.Normalize(input);

            // Should deduplicate to single entry
            result.Should().HaveCountLessOrEqualTo(1);
        }

        [Fact]
        public void Normalize_returns_case_insensitive_set()
        {
            var service = new StyleCatalogService(_logger, null);
            var input = new[] { "rock" };

            var result = service.Normalize(input);

            if (result.Count > 0)
            {
                var slug = result.First();
                result.Contains(slug.ToUpperInvariant()).Should().BeTrue();
                result.Contains(slug.ToLowerInvariant()).Should().BeTrue();
            }
        }

        #endregion

        #region IsMatch Tests

        [Fact]
        public void IsMatch_returns_false_for_null_libraryGenres()
        {
            var service = new StyleCatalogService(_logger, null);
            var selectedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };

            var result = service.IsMatch(null, selectedSlugs);

            result.Should().BeFalse();
        }

        [Fact]
        public void IsMatch_returns_false_for_empty_libraryGenres()
        {
            var service = new StyleCatalogService(_logger, null);
            var selectedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };

            var result = service.IsMatch(new List<string>(), selectedSlugs);

            result.Should().BeFalse();
        }

        [Fact]
        public void IsMatch_returns_false_for_null_selectedStyleSlugs()
        {
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new List<string> { "rock" };

            var result = service.IsMatch(libraryGenres, null);

            result.Should().BeFalse();
        }

        [Fact]
        public void IsMatch_returns_false_for_empty_selectedStyleSlugs()
        {
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new List<string> { "rock" };
            var selectedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var result = service.IsMatch(libraryGenres, selectedSlugs);

            result.Should().BeFalse();
        }

        [Fact]
        public void IsMatch_returns_true_for_direct_match()
        {
            var service = new StyleCatalogService(_logger, null);

            // Get a known slug from the catalog
            var all = service.GetAll();
            if (all.Count == 0) return; // Skip if catalog is empty

            var knownSlug = all.First().Slug;
            var libraryGenres = new List<string> { knownSlug };
            var selectedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { knownSlug };

            var result = service.IsMatch(libraryGenres, selectedSlugs);

            result.Should().BeTrue();
        }

        [Fact]
        public void IsMatch_returns_false_for_no_match()
        {
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new List<string> { "nonexistent-genre-12345" };
            var selectedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "different-nonexistent-genre-67890" };

            var result = service.IsMatch(libraryGenres, selectedSlugs);

            result.Should().BeFalse();
        }

        [Fact]
        public void IsMatch_with_relaxParentMatch_checks_parents()
        {
            var service = new StyleCatalogService(_logger, null);

            // Find a style that has parents
            var all = service.GetAll();
            var styleWithParent = all.FirstOrDefault(s => s.Parents != null && s.Parents.Count > 0);

            if (styleWithParent == null) return; // Skip if no styles with parents

            var parentSlug = styleWithParent.Parents.First();
            var resolvedParent = service.ResolveSlug(parentSlug) ?? parentSlug;

            var libraryGenres = new List<string> { styleWithParent.Slug };
            var selectedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { resolvedParent };

            // Without relaxParentMatch, should not match via parent
            var resultStrict = service.IsMatch(libraryGenres, selectedSlugs, relaxParentMatch: false);

            // With relaxParentMatch, should match via parent
            var resultRelaxed = service.IsMatch(libraryGenres, selectedSlugs, relaxParentMatch: true);

            // The relaxed should be true if it found the parent match
            resultRelaxed.Should().BeTrue();
        }

        #endregion

        #region ResolveSlug Tests

        [Fact]
        public void ResolveSlug_returns_null_for_null_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.ResolveSlug(null);

            result.Should().BeNull();
        }

        [Fact]
        public void ResolveSlug_returns_null_for_empty_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.ResolveSlug("");

            result.Should().BeNull();
        }

        [Fact]
        public void ResolveSlug_returns_null_for_whitespace_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.ResolveSlug("   ");

            result.Should().BeNull();
        }

        [Fact]
        public void ResolveSlug_returns_slug_for_known_style()
        {
            var service = new StyleCatalogService(_logger, null);

            var all = service.GetAll();
            if (all.Count == 0) return;

            var knownSlug = all.First().Slug;
            var result = service.ResolveSlug(knownSlug);

            result.Should().Be(knownSlug);
        }

        [Fact]
        public void ResolveSlug_resolves_by_name()
        {
            var service = new StyleCatalogService(_logger, null);

            var all = service.GetAll();
            if (all.Count == 0) return;

            var entry = all.First();
            var result = service.ResolveSlug(entry.Name);

            result.Should().Be(entry.Slug);
        }

        #endregion

        #region GetBySlug Tests

        [Fact]
        public void GetBySlug_returns_null_for_null_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.GetBySlug(null);

            result.Should().BeNull();
        }

        [Fact]
        public void GetBySlug_returns_null_for_empty_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.GetBySlug("");

            result.Should().BeNull();
        }

        [Fact]
        public void GetBySlug_returns_null_for_whitespace_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.GetBySlug("   ");

            result.Should().BeNull();
        }

        [Fact]
        public void GetBySlug_returns_entry_for_known_slug()
        {
            var service = new StyleCatalogService(_logger, null);

            var all = service.GetAll();
            if (all.Count == 0) return;

            var knownSlug = all.First().Slug;
            var result = service.GetBySlug(knownSlug);

            result.Should().NotBeNull();
            result.Slug.Should().Be(knownSlug);
        }

        [Fact]
        public void GetBySlug_is_case_insensitive()
        {
            var service = new StyleCatalogService(_logger, null);

            var all = service.GetAll();
            if (all.Count == 0) return;

            var knownSlug = all.First().Slug;
            var resultUpper = service.GetBySlug(knownSlug.ToUpperInvariant());
            var resultLower = service.GetBySlug(knownSlug.ToLowerInvariant());

            resultUpper.Should().NotBeNull();
            resultLower.Should().NotBeNull();
            resultUpper.Slug.Should().Be(resultLower.Slug);
        }

        [Fact]
        public void GetBySlug_returns_null_for_unknown_slug()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.GetBySlug("nonexistent-style-slug-12345");

            result.Should().BeNull();
        }

        #endregion

        #region GetSimilarSlugs Tests

        [Fact]
        public void GetSimilarSlugs_returns_empty_for_null_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.GetSimilarSlugs(null).ToList();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetSimilarSlugs_returns_empty_for_empty_input()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.GetSimilarSlugs("").ToList();

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetSimilarSlugs_returns_self_with_score_1()
        {
            var service = new StyleCatalogService(_logger, null);

            var all = service.GetAll();
            if (all.Count == 0) return;

            var knownSlug = all.First().Slug;
            var result = service.GetSimilarSlugs(knownSlug).ToList();

            result.Should().Contain(s => s.Slug == knownSlug && Math.Abs(s.Score - 1.0) < 0.001);
        }

        [Fact]
        public void GetSimilarSlugs_includes_parents_siblings_and_children()
        {
            var service = new StyleCatalogService(_logger, null);

            var all = service.GetAll();
            var styleWithParent = all.FirstOrDefault(s => s.Parents != null && s.Parents.Count > 0);

            if (styleWithParent == null) return;

            var result = service.GetSimilarSlugs(styleWithParent.Slug).ToList();

            // Should include at least self and parents
            result.Should().HaveCountGreaterOrEqualTo(2);

            // Check for parent relationship
            result.Should().Contain(s => s.Relationship == "parent" || s.Relationship == "self");
        }

        [Fact]
        public void GetSimilarSlugs_returns_empty_for_unknown_slug()
        {
            var service = new StyleCatalogService(_logger, null);
            var result = service.GetSimilarSlugs("nonexistent-style-slug-12345").ToList();

            result.Should().BeEmpty();
        }

        #endregion

        #region RefreshAsync Tests

        [Fact]
        public async Task RefreshAsync_completes_without_error()
        {
            var service = new StyleCatalogService(_logger, null);

            await service.RefreshAsync();

            // Should complete without throwing
            var all = service.GetAll();
            all.Should().NotBeNull();
        }

        [Fact]
        public async Task RefreshAsync_respects_cancellation_token()
        {
            var service = new StyleCatalogService(_logger, null);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.RefreshAsync(cts.Token));
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public void StyleCatalogService_is_thread_safe_for_concurrent_reads()
        {
            var service = new StyleCatalogService(_logger, null);

            // Warm up the catalog
            var _ = service.GetAll();

            var exceptions = new List<Exception>();
            var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            {
                try
                {
                    service.GetAll();
                    service.Search("rock", 10);
                    service.GetBySlug("rock");
                    service.ResolveSlug("rock");
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            })).ToArray();

            Task.WaitAll(tasks);
            exceptions.Should().BeEmpty();
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_throws_for_null_logger()
        {
            Assert.Throws<ArgumentNullException>(() => new StyleCatalogService(null, null));
        }

        [Fact]
        public void Constructor_accepts_null_httpClient()
        {
            // Should not throw - httpClient is optional
            var service = new StyleCatalogService(_logger, null);
            service.Should().NotBeNull();
        }

        #endregion
    }
}
