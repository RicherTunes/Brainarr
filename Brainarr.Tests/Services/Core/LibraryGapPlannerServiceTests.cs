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

        [Fact]
        public void BuildPlan_ShouldIncludeEvidenceAndWhyNow()
        {
            var planner = new LibraryGapPlannerService();
            var profile = new LibraryProfile
            {
                Metadata = new Dictionary<string, object>
                {
                    ["GenreDistribution"] = new Dictionary<string, double>
                    {
                        ["Ambient"] = 2.0,
                        ["Rock"] = 65.0
                    },
                    ["PreferredEras"] = new List<string> { "Modern", "Contemporary" },
                    ["NewReleaseRatio"] = 0.6
                }
            };

            var plan = planner.BuildPlan(profile, maxItems: 5);

            plan.Should().OnlyContain(item => item.Evidence != null && item.Evidence.Count > 0);
            plan.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.WhyNow));
            plan.Should().Contain(item => item.ExpectedLift > 0);
        }

        [Fact]
        public void BuildPlan_StylePriority_ShouldBeMonotonicWithGapSize()
        {
            var planner = new LibraryGapPlannerService();
            var widerGapProfile = new LibraryProfile
            {
                Metadata = new Dictionary<string, object>
                {
                    ["GenreDistribution"] = new Dictionary<string, double>
                    {
                        ["Ambient"] = 1.0,
                        ["Rock"] = 70.0
                    }
                }
            };

            var narrowerGapProfile = new LibraryProfile
            {
                Metadata = new Dictionary<string, object>
                {
                    ["GenreDistribution"] = new Dictionary<string, double>
                    {
                        ["Ambient"] = 6.5,
                        ["Rock"] = 70.0
                    }
                }
            };

            var widerGap = planner.BuildPlan(widerGapProfile, maxItems: 5).Single(item => item.Target == "Ambient");
            var narrowerGap = planner.BuildPlan(narrowerGapProfile, maxItems: 5).Single(item => item.Target == "Ambient");

            widerGap.Priority.Should().BeGreaterOrEqualTo(narrowerGap.Priority);
            widerGap.ExpectedLift.Should().BeGreaterThan(narrowerGap.ExpectedLift);
        }
    }
}
