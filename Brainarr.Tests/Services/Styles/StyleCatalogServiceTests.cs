using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Styles
{
    [Trait("Category", "Unit")]
    public class StyleCatalogServiceTests
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [Fact]
        public void Normalize_Should_Map_Aliases_And_Slugs()
        {
            var svc = new StyleCatalogService(_logger, new System.Net.Http.HttpClient());
            var normalized = svc.Normalize(new[] { "Prog Rock", "progressive-rock", "Art Rock", "Unknown-Thing" });

            normalized.Should().Contain("progressive-rock");
            normalized.Should().Contain("art-rock");
            normalized.Should().NotContain("Unknown-Thing");
        }

        [Fact]
        public void Search_Should_Return_Relevant_Styles()
        {
            var svc = new StyleCatalogService(_logger, new System.Net.Http.HttpClient());
            var res = svc.Search("prog", 10).Select(s => s.Slug).ToList();
            res.Should().Contain("progressive-rock");
        }

        [Fact]
        public void IsMatch_Strict_Should_Require_Explicit_Intersection()
        {
            var svc = new StyleCatalogService(_logger, new System.Net.Http.HttpClient());
            var selected = svc.Normalize(new[] { "progressive-rock" });

            // Direct genre
            svc.IsMatch(new List<string> { "Progressive Rock" }, selected).Should().BeTrue();
            // Alias
            svc.IsMatch(new List<string> { "Prog Rock" }, selected).Should().BeTrue();
            // Non-matching
            svc.IsMatch(new List<string> { "Jazz" }, selected).Should().BeFalse();
        }

        [Fact]
        public void IsMatch_Relax_Should_Allow_Parent_To_Selected_Child()
        {
            var svc = new StyleCatalogService(_logger, new System.Net.Http.HttpClient());
            var selected = svc.Normalize(new[] { "fusion" }); // child under Jazz and Rock

            // If album genre is parent (e.g., Jazz), relax permits match because child is selected
            svc.IsMatch(new List<string> { "Jazz" }, selected, relaxParentMatch: true).Should().BeTrue();
            // Strict remains false
            svc.IsMatch(new List<string> { "Jazz" }, selected, relaxParentMatch: false).Should().BeFalse();
        }
    }
}

