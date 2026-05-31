using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for StyleCatalogService.
    /// Tests all public methods and edge cases including:
    /// - GetAll, Search, Normalize, IsMatch
    /// - ResolveSlug, GetBySlug, GetSimilarSlugs
    /// - RefreshAsync, constructor validation
    /// </summary>
    public class StyleCatalogServiceCovTests
    {
        private readonly Logger _logger;

        public StyleCatalogServiceCovTests()
        {
            _logger = TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => new StyleCatalogService(null, Mock.Of<IHttpClient>()));
            exception.ParamName.Should().Be("logger");
        }

        [Fact]
        public void Constructor_WithNullHttpClient_WorksCorrectly()
        {
            // Arrange & Act
            var service = new StyleCatalogService(_logger, null);

            // Assert - service should work without HTTP client
            var all = service.GetAll();
            all.Should().NotBeNull();
            all.Should().HaveCountGreaterThanOrEqualTo(6, "Should load fallback catalog with at least 6 styles");
        }

        [Fact]
        public void GetAll_ReturnsNonEmptyCatalog()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetAll();

            // Assert
            result.Should().NotBeEmpty();
            result.Should().HaveCountGreaterThanOrEqualTo(6, "Fallback catalog has 6 built-in styles");
            result.First().Name.Should().NotBeNullOrEmpty();
            result.First().Slug.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void GetAll_ReturnsImmutableSnapshot()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result1 = service.GetAll();
            var result2 = service.GetAll();

            // Assert - Should return different array instances (snapshot)
            result1.Should().NotBeSameAs(result2, "GetAll should return a new array snapshot");
            result1.Should().BeEquivalentTo(result2, "But content should be equivalent");
        }

        [Fact]
        public void Search_WithEmptyQuery_ReturnsOrderedStyles()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.Search("");

            // Assert
            result.Should().NotBeEmpty();
            // Verify results are alphabetically sorted
            result.Select(s => s.Name).Should().BeInAscendingOrder(StringComparer.InvariantCultureIgnoreCase);
        }

        [Fact]
        public void Search_WithNullQuery_ReturnsOrderedStyles()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.Search(null);

            // Assert
            result.Should().NotBeEmpty();
            // Verify results are alphabetically sorted
            result.Select(s => s.Name).Should().BeInAscendingOrder(StringComparer.InvariantCultureIgnoreCase);
        }

        [Fact]
        public void Search_WithPartialMatch_ReturnsMatchingStyles()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.Search("rock");

            // Assert
            result.Should().NotBeEmpty();
            result.All(s => s.Name.ToLowerInvariant().Contains("rock") ||
                           s.Aliases.Any(a => a?.ToLowerInvariant().Contains("rock") == true))
                  .Should().BeTrue("All results should match 'rock'");
        }

        [Fact]
        public void Search_WithExactStartMatch_HasHighestScore()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.Search("rock");

            // Assert - "Rock" should be first since it starts with the query
            result.First().Slug.Should().Be("rock");
        }

        [Fact]
        public void Search_WithAliasMatch_ReturnsStyle()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act - Search for alias "Prog"
            var result = service.Search("prog");

            // Assert
            result.Should().Contain(s => s.Slug == "progressive-rock");
        }

        [Fact]
        public void Search_WithReorderedTokens_ReturnsStyle()
        {
            // F4 (real-usage): typing "Rock Alternative" in the Music Styles TagSelect returned NO
            // options because Score only did whole-string StartsWith/Contains — "rock alternative" is
            // not a substring of "alternative rock". Multi-word queries must match order-independently
            // (every token present), so the user can actually find/select the style.
            var service = new StyleCatalogService(_logger, null);

            var result = service.Search("Rock Alternative");

            result.Should().Contain(s => s.Slug == "alternative-rock",
                "a reordered multi-word query must still find Alternative Rock");
        }

        [Fact]
        public void Search_WithReorderedTokens_DoesNotOverMatchMissingToken()
        {
            // Token-AND, not token-OR: an entry missing one of the query tokens must NOT match. Plain
            // "Rock" lacks "alternative", so the query "Rock Alternative" must not return it.
            var service = new StyleCatalogService(_logger, null);

            var result = service.Search("Rock Alternative");

            result.Should().NotContain(s => s.Slug == "rock",
                "plain Rock is missing the 'alternative' token and must be excluded");
        }

        [Fact]
        public void Search_WithLimit_ReturnsLimitedResults()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.Search("", limit: 2);

            // Assert
            result.Should().HaveCount(2);
        }

        [Fact]
        public void Normalize_WithNullInput_ReturnsEmptySet()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.Normalize(null);

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void Normalize_WithValidStyleNames_ReturnsSlugs()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act - "Electronic" is an alias of "electronica" in the embedded resource
            var result = service.Normalize(new[] { "Rock", "Electronic", "techno" });

            // Assert
            result.Should().HaveCount(3);
            result.Should().Contain("rock");
            result.Should().Contain("electronica");
            result.Should().Contain("techno");
        }

        [Fact]
        public void Normalize_WithAlias_ReturnsCanonicalSlug()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act - "Prog" is an alias for "progressive-rock"
            var result = service.Normalize(new[] { "Prog" });

            // Assert
            result.Should().HaveCount(1);
            result.Should().Contain("progressive-rock");
        }

        [Fact]
        public void Normalize_IsCaseInsensitive()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.Normalize(new[] { "ROCK", "Rock", "rock" });

            // Assert - All should resolve to same slug
            result.Should().HaveCount(1, "All case variations should normalize to same slug");
            result.Should().Contain("rock");
        }

        [Fact]
        public void Normalize_WithUnknownValues_ReturnsEmptySet()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.Normalize(new[] { "UnknownStyleXYZ", "NonExistent" });

            // Assert
            result.Should().BeEmpty("Unknown styles should not be in the result");
        }

        [Fact]
        public void IsMatch_WithEmptyLibraryGenres_ReturnsFalse()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };

            // Act
            var result = service.IsMatch(Array.Empty<string>(), selected);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsMatch_WithEmptySelectedStyles_ReturnsFalse()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new[] { "rock" };

            // Act
            var result = service.IsMatch(libraryGenres, new HashSet<string>());

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsMatch_WithDirectMatch_ReturnsTrue()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new[] { "rock" };
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };

            // Act
            var result = service.IsMatch(libraryGenres, selected);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsMatch_WithAliasMatch_ReturnsTrue()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new[] { "Prog Rock" }; // Alias for progressive-rock
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "progressive-rock" };

            // Act
            var result = service.IsMatch(libraryGenres, selected);

            // Assert
            result.Should().BeTrue("Alias should match canonical slug");
        }

        [Fact]
        public void IsMatch_WithNoMatch_ReturnsFalse()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new[] { "jazz" };
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };

            // Act
            var result = service.IsMatch(libraryGenres, selected);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsMatch_WithRelaxParentMatchAndParentSelected_ReturnsTrue()
        {
            // Arrange - progressive-rock has parent "rock"
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new[] { "progressive-rock" };
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };

            // Act
            var result = service.IsMatch(libraryGenres, selected, relaxParentMatch: true);

            // Assert
            result.Should().BeTrue("Child style should match when parent is selected with relaxParentMatch");
        }

        [Fact]
        public void IsMatch_WithoutRelaxParentMatch_DoesNotMatchParent()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new[] { "progressive-rock" };
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };

            // Act
            var result = service.IsMatch(libraryGenres, selected, relaxParentMatch: false);

            // Assert
            result.Should().BeFalse("Without relaxParentMatch, parent should not match child");
        }

        [Fact]
        public void IsMatch_WithMultipleLibraryGenres_MatchesAny()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new[] { "jazz", "progressive-rock", "techno" };
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };

            // Act - progressive-rock has parent "rock", so relaxParentMatch should find the match
            var result = service.IsMatch(libraryGenres, selected, relaxParentMatch: true);

            // Assert
            result.Should().BeTrue("progressive-rock's parent 'rock' is in selected styles");
        }

        [Fact]
        public void ResolveSlug_WithValidSlug_ReturnsSameSlug()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.ResolveSlug("rock");

            // Assert
            result.Should().Be("rock");
        }

        [Fact]
        public void ResolveSlug_WithName_ReturnsSlug()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.ResolveSlug("Rock");

            // Assert
            result.Should().Be("rock");
        }

        [Fact]
        public void ResolveSlug_WithAlias_ReturnsCanonicalSlug()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act - "Prog" is an alias
            var result = service.ResolveSlug("Prog");

            // Assert
            result.Should().Be("progressive-rock");
        }

        [Fact]
        public void ResolveSlug_WithUnknownValue_ReturnsNull()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.ResolveSlug("unknown-style-xyz");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void ResolveSlug_WithNullInput_ReturnsNull()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.ResolveSlug(null);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void ResolveSlug_WithWhitespaceInput_ReturnsNull()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.ResolveSlug("   ");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void ResolveSlug_IsCaseInsensitive()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result1 = service.ResolveSlug("ROCK");
            var result2 = service.ResolveSlug("Rock");
            var result3 = service.ResolveSlug("rock");

            // Assert
            result1.Should().Be("rock");
            result2.Should().Be("rock");
            result3.Should().Be("rock");
        }

        [Fact]
        public void GetBySlug_WithValidSlug_ReturnsEntry()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetBySlug("rock");

            // Assert
            result.Should().NotBeNull();
            result.Slug.Should().Be("rock");
            result.Name.Should().Be("Rock");
        }

        [Fact]
        public void GetBySlug_WithUnknownSlug_ReturnsNull()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetBySlug("unknown");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetBySlug_WithNullInput_ReturnsNull()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetBySlug(null);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetBySlug_WithWhitespaceInput_ReturnsNull()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetBySlug("   ");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetBySlug_IsCaseInsensitive()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result1 = service.GetBySlug("ROCK");
            var result2 = service.GetBySlug("Rock");

            // Assert
            result1.Should().NotBeNull();
            result2.Should().NotBeNull();
            result1.Slug.Should().Be("rock");
            result2.Slug.Should().Be("rock");
        }

        [Fact]
        public void GetSimilarSlugs_WithValidSlug_ReturnsSimilarityList()
        {
            // Arrange - progressive-rock has parent "rock"
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetSimilarSlugs("progressive-rock");

            // Assert
            result.Should().NotBeEmpty();
            result.Should().Contain(s => s.Slug == "progressive-rock" && s.Relationship == "self");
        }

        [Fact]
        public void GetSimilarSlugs_IncludesParentWithCorrectScore()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetSimilarSlugs("progressive-rock").ToList();

            // Assert
            result.Should().Contain(s => s.Slug == "rock" && s.Relationship == "parent" && s.Score == 0.85);
        }

        [Fact]
        public void GetSimilarSlugs_IncludesSiblings()
        {
            // Arrange - progressive-rock is child of rock, if there are siblings they should appear
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetSimilarSlugs("progressive-rock").ToList();

            // Assert - At minimum should have self and parent
            result.Should().Contain(s => s.Relationship == "self");
            result.Should().Contain(s => s.Relationship == "parent");
        }

        [Fact]
        public void GetSimilarSlugs_WithParentSlug_IncludesChildren()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act - rock has child "progressive-rock"
            var result = service.GetSimilarSlugs("rock").ToList();

            // Assert
            result.Should().Contain(s => s.Slug == "progressive-rock" && s.Relationship == "child");
        }

        [Fact]
        public void GetSimilarSlugs_WithUnknownSlug_ReturnsEmptyList()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetSimilarSlugs("unknown-style");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetSimilarSlugs_WithNullInput_ReturnsEmptyList()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetSimilarSlugs(null);

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetSimilarSlugs_WithWhitespaceInput_ReturnsEmptyList()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetSimilarSlugs("   ");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetSimilarSlugs_NoDuplicatesInResults()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetSimilarSlugs("rock");

            // Assert - No duplicate slugs
            var slugs = result.Select(s => s.Slug).ToList();
            slugs.Should().OnlyHaveUniqueItems("Similarity results should not contain duplicate slugs");
        }

        [Fact]
        public void GetSimilarSlugs_AreOrderedByScore()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetSimilarSlugs("progressive-rock").ToList();

            // Assert - Self should be first with score 1.0
            if (result.Count > 0)
            {
                result.First().Score.Should().Be(1.0);
                result.First().Relationship.Should().Be("self");
            }
        }

        [Fact]
        public async Task RefreshAsync_ResetsRefreshTimer()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            await service.RefreshAsync();

            // Assert - Should not throw and catalog should still be accessible
            var all = service.GetAll();
            all.Should().NotBeEmpty();
        }

        [Fact]
        public async Task RefreshAsync_WithCancellationToken_ThrowsWhenCancelled()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await service.RefreshAsync(cts.Token));
        }

        [Fact]
        public void MultipleServiceInstances_HaveIndependentCaches()
        {
            // Arrange
            var service1 = new StyleCatalogService(_logger, null);
            var service2 = new StyleCatalogService(_logger, null);

            // Act
            var all1 = service1.GetAll();
            var all2 = service2.GetAll();

            // Assert - Both should work independently
            all1.Should().NotBeEmpty();
            all2.Should().NotBeEmpty();
            all1.Should().HaveCount(all2.Count());
        }

        [Fact]
        public void Search_WithNoMatches_ReturnsEmpty()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act - Search for something that won't match
            var result = service.Search("xyz123nonexistent");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void IsMatch_WithNullLibraryGenres_ReturnsFalse()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };

            // Act
            var result = service.IsMatch(null, selected);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsMatch_WithNullSelectedStyles_ReturnsFalse()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);
            var libraryGenres = new[] { "rock" };

            // Act
            var result = service.IsMatch(libraryGenres, null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void GetBySlug_EntryContainsExpectedProperties()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.GetBySlug("progressive-rock");

            // Assert
            result.Should().NotBeNull();
            result.Name.Should().Be("Progressive Rock");
            result.Slug.Should().Be("progressive-rock");
            result.Aliases.Should().NotBeEmpty();
            result.Aliases.Should().Contain("Prog");
            result.Parents.Should().NotBeEmpty();
            result.Parents.Should().Contain("rock");
        }

        [Fact]
        public void Search_WithSpecialCharacters_ReturnsMatches()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act - Hip Hop has a space
            var result = service.Search("hip");

            // Assert
            result.Should().Contain(s => s.Slug == "hip-hop");
        }

        [Fact]
        public void Normalize_WithMixedValidAndInvalid_ReturnsOnlyValid()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act
            var result = service.Normalize(new[] { "rock", "unknown", "jazz", "also-unknown" });

            // Assert
            result.Should().HaveCount(2);
            result.Should().Contain("rock");
            result.Should().Contain("jazz");
            result.Should().NotContain("unknown");
            result.Should().NotContain("also-unknown");
        }

        [Fact]
        public async Task RefreshAsync_MultipleCalls_DoNotCauseIssues()
        {
            // Arrange
            var service = new StyleCatalogService(_logger, null);

            // Act - Multiple refreshes
            await service.RefreshAsync();
            await service.RefreshAsync();
            await service.RefreshAsync();

            // Assert - Catalog should still work
            var all = service.GetAll();
            all.Should().NotBeEmpty();
        }
    }
}
