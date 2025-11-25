using System.Linq;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Styles
{
    [Trait("Category", "Unit")]
    public class StyleCatalogDataTests
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [Fact]
        public void Catalog_Should_Have_Unique_Slugs()
        {
            var svc = new StyleCatalogService(_logger, new System.Net.Http.HttpClient());
            var slugs = svc.GetAll().Select(s => s.Slug).ToList();
            slugs.Count.Should().Be(slugs.Distinct().Count(), "style slugs must be unique");
        }

        [Fact]
        public void Catalog_Aliases_Should_Not_Equal_Own_Slug()
        {
            var svc = new StyleCatalogService(_logger, new System.Net.Http.HttpClient());
            foreach (var style in svc.GetAll())
            {
                style.Aliases.Should().OnlyContain(a => !string.Equals(a, style.Slug, System.StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
