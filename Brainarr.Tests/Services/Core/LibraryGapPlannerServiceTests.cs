using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class LibraryGapPlannerServiceTests
    {
        [Fact]
        public void BuildPlan_ShouldPrioritizeUnderrepresentedStylesAndEraBalance()
        {
            var planner = new LibraryGapPlannerService();
            var profile = new LibraryProfile
            {
                Metadata = new Dictionary<string, object>
                {
                    ["GenreDistribution"] = new Dictionary<string, double>
                    {
                        ["Rock"] = 70.0,
                        ["Ambient"] = 2.5,
                        ["Jazz"] = 6.0,
                        ["dominant_genre_percentage"] = 70.0
                    },
                    ["PreferredEras"] = new List<string> { "Modern", "Contemporary" },
                    ["NewReleaseRatio"] = 0.58
                }
            };

            var plan = planner.BuildPlan(profile, maxItems: 5);

            plan.Should().NotBeEmpty();
            plan.Any(p => p.Target == "Ambient").Should().BeTrue();
            plan.Any(p => p.Target == "Catalog Backfill").Should().BeTrue();
        }

        [Fact]
        public void BuildPlan_ShouldRespectMaxItems()
        {
            var planner = new LibraryGapPlannerService();
            var profile = new LibraryProfile
            {
                Metadata = new Dictionary<string, object>
                {
                    ["GenreDistribution"] = new Dictionary<string, double>
                    {
                        ["Rock"] = 55.0,
                        ["Ambient"] = 1.5,
                        ["Fusion"] = 4.0
                    },
                    ["PreferredEras"] = new List<string> { "Modern" },
                    ["NewReleaseRatio"] = 0.05
                }
            };

            var plan = planner.BuildPlan(profile, maxItems: 2);

            plan.Count.Should().Be(2);
        }
    }
}
