using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Phase 15 tests for Gap Planner v2: budget constraints, simulation,
    /// monotonicity, and golden fixture stability.
    /// </summary>
    public class GapPlannerV2Tests
    {
        private readonly LibraryGapPlannerService _planner = new();

        private static LibraryProfile MakeProfile(
            Dictionary<string, double> genres = null,
            List<string> preferredEras = null,
            double newReleaseRatio = 0.25)
        {
            var metadata = new Dictionary<string, object>();
            if (genres != null) metadata["GenreDistribution"] = genres;
            if (preferredEras != null) metadata["PreferredEras"] = preferredEras;
            metadata["NewReleaseRatio"] = newReleaseRatio;
            return new LibraryProfile { Metadata = metadata };
        }

        // ── Budget Constraint Tests ──────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_WithBudget_CapsItemCount()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 1.0, ["Jazz"] = 3.0, ["Rock"] = 60.0
                },
                preferredEras: new List<string> { "Modern" },
                newReleaseRatio: 0.55);

            // Without budget: should return up to maxItems
            var unbounded = _planner.BuildPlan(profile, maxItems: 10);
            unbounded.Count.Should().BeGreaterThan(2, "base profile should produce multiple items");

            // With budget of 2: caps output
            var bounded = _planner.BuildPlan(profile, maxItems: 10, budget: 2);
            bounded.Count.Should().BeLessOrEqualTo(2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_BudgetLowerThanMaxItems_UsesSmaller()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.0, ["Jazz"] = 5.0, ["Rock"] = 65.0
                },
                preferredEras: new List<string> { "Modern", "Contemporary" },
                newReleaseRatio: 0.60);

            var plan = _planner.BuildPlan(profile, maxItems: 10, budget: 1);
            plan.Count.Should().Be(1);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_BudgetHigherThanMaxItems_UsesMaxItems()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 1.0, ["Rock"] = 70.0
                },
                preferredEras: new List<string> { "Modern" },
                newReleaseRatio: 0.50);

            var plan = _planner.BuildPlan(profile, maxItems: 2, budget: 100);
            plan.Count.Should().BeLessOrEqualTo(2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_ZeroBudget_ReturnsOneItem()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.0, ["Rock"] = 60.0
                });

            // Budget of 0 → clamped to 1
            var plan = _planner.BuildPlan(profile, maxItems: 5, budget: 0);
            plan.Count.Should().BeLessOrEqualTo(1);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_MinConfidence_FiltersLowConfidenceItems()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 1.0, ["Jazz"] = 5.0, ["Rock"] = 60.0
                },
                preferredEras: new List<string> { "Modern" },
                newReleaseRatio: 0.55);

            // All items have confidence <= 0.88
            var allItems = _planner.BuildPlan(profile, maxItems: 10);
            allItems.Should().NotBeEmpty();

            // Filter at 0.90 should eliminate most/all items
            var filtered = _planner.BuildPlan(profile, maxItems: 10, minConfidence: 0.90);
            filtered.Count.Should().BeLessThan(allItems.Count);
        }

        // ── Simulate Tests ───────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void Simulate_ReturnsDryRunResult()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.0, ["Jazz"] = 5.0, ["Rock"] = 65.0
                },
                preferredEras: new List<string> { "Modern", "Contemporary" },
                newReleaseRatio: 0.55);

            var result = _planner.Simulate(profile, maxItems: 5);

            result.DryRun.Should().BeTrue();
            result.TotalItems.Should().Be(result.Items.Count);
            result.BudgetApplied.Should().BeFalse();
            result.BudgetRemaining.Should().BeNull();
            result.Items.Should().NotBeEmpty();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Simulate_WithBudget_TracksBudgetRemaining()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 1.0, ["Jazz"] = 3.0, ["Rock"] = 60.0
                },
                preferredEras: new List<string> { "Modern" },
                newReleaseRatio: 0.55);

            var result = _planner.Simulate(profile, maxItems: 10, budget: 5);

            result.BudgetApplied.Should().BeTrue();
            result.BudgetRemaining.Should().NotBeNull();
            result.BudgetRemaining.Value.Should().Be(5 - result.TotalItems);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Simulate_AverageConfidence_IsCorrect()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.0, ["Rock"] = 65.0
                },
                preferredEras: new List<string> { "Modern", "Contemporary" },
                newReleaseRatio: 0.55);

            var result = _planner.Simulate(profile, maxItems: 5);

            if (result.Items.Count > 0)
            {
                var expectedAvg = result.Items.Average(p => p.Confidence);
                result.AverageConfidence.Should().BeApproximately(expectedAvg, 0.001);
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Simulate_TotalExpectedLift_IsSumOfItems()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.0, ["Rock"] = 65.0
                },
                preferredEras: new List<string> { "Modern", "Contemporary" },
                newReleaseRatio: 0.55);

            var result = _planner.Simulate(profile, maxItems: 5);
            var expectedLift = result.Items.Sum(p => p.ExpectedLift);
            result.TotalExpectedLift.Should().BeApproximately(expectedLift, 0.001);
        }

        // ── Monotonicity Tests ───────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_SameInput_SameOutput_Deterministic()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.0, ["Jazz"] = 5.5, ["Rock"] = 60.0
                },
                preferredEras: new List<string> { "Modern" },
                newReleaseRatio: 0.48);

            var run1 = _planner.BuildPlan(profile, maxItems: 5, budget: 3);
            var run2 = _planner.BuildPlan(profile, maxItems: 5, budget: 3);

            run1.Count.Should().Be(run2.Count);
            for (int i = 0; i < run1.Count; i++)
            {
                run1[i].Category.Should().Be(run2[i].Category);
                run1[i].Target.Should().Be(run2[i].Target);
                run1[i].Priority.Should().Be(run2[i].Priority);
                run1[i].Confidence.Should().Be(run2[i].Confidence);
                run1[i].ExpectedLift.Should().Be(run2[i].ExpectedLift);
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Simulate_SameInput_SameOutput_Deterministic()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.0, ["Rock"] = 65.0
                },
                preferredEras: new List<string> { "Modern", "Contemporary" },
                newReleaseRatio: 0.55);

            var r1 = _planner.Simulate(profile, maxItems: 5, budget: 3);
            var r2 = _planner.Simulate(profile, maxItems: 5, budget: 3);

            r1.TotalItems.Should().Be(r2.TotalItems);
            r1.AverageConfidence.Should().Be(r2.AverageConfidence);
            r1.TotalExpectedLift.Should().Be(r2.TotalExpectedLift);
            r1.BudgetRemaining.Should().Be(r2.BudgetRemaining);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_WiderGap_HigherOrEqualPriority()
        {
            // Wider gap (1.0%) should produce >= priority than narrower gap (6.0%)
            var widerGap = MakeProfile(
                genres: new Dictionary<string, double> { ["Ambient"] = 1.0, ["Rock"] = 70.0 });
            var narrowerGap = MakeProfile(
                genres: new Dictionary<string, double> { ["Ambient"] = 6.0, ["Rock"] = 70.0 });

            var wider = _planner.BuildPlan(widerGap, maxItems: 5)
                .FirstOrDefault(p => p.Target == "Ambient");
            var narrower = _planner.BuildPlan(narrowerGap, maxItems: 5)
                .FirstOrDefault(p => p.Target == "Ambient");

            wider.Should().NotBeNull();
            narrower.Should().NotBeNull();
            wider!.Priority.Should().BeGreaterOrEqualTo(narrower!.Priority);
            wider.Confidence.Should().BeGreaterOrEqualTo(narrower.Confidence);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_WiderGap_HigherExpectedLift()
        {
            var widerGap = MakeProfile(
                genres: new Dictionary<string, double> { ["Ambient"] = 1.0, ["Rock"] = 70.0 });
            var narrowerGap = MakeProfile(
                genres: new Dictionary<string, double> { ["Ambient"] = 6.0, ["Rock"] = 70.0 });

            var wider = _planner.BuildPlan(widerGap, maxItems: 5)
                .First(p => p.Target == "Ambient");
            var narrower = _planner.BuildPlan(narrowerGap, maxItems: 5)
                .First(p => p.Target == "Ambient");

            wider.ExpectedLift.Should().BeGreaterThan(narrower.ExpectedLift);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_HigherRecencyBias_ProducesCatalogBackfill()
        {
            var highRecency = MakeProfile(
                genres: new Dictionary<string, double> { ["Rock"] = 70.0 },
                newReleaseRatio: 0.60);
            var lowRecency = MakeProfile(
                genres: new Dictionary<string, double> { ["Rock"] = 70.0 },
                newReleaseRatio: 0.25);

            var highPlan = _planner.BuildPlan(highRecency, maxItems: 5);
            var lowPlan = _planner.BuildPlan(lowRecency, maxItems: 5);

            highPlan.Should().Contain(p => p.Target == "Catalog Backfill");
            lowPlan.Should().NotContain(p => p.Target == "Catalog Backfill");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_LowRecencyRatio_ProducesCurrentReleases()
        {
            var lowRecency = MakeProfile(
                genres: new Dictionary<string, double> { ["Rock"] = 70.0 },
                newReleaseRatio: 0.05);
            var normalRecency = MakeProfile(
                genres: new Dictionary<string, double> { ["Rock"] = 70.0 },
                newReleaseRatio: 0.25);

            var lowPlan = _planner.BuildPlan(lowRecency, maxItems: 5);
            var normalPlan = _planner.BuildPlan(normalRecency, maxItems: 5);

            lowPlan.Should().Contain(p => p.Target == "Current Releases");
            normalPlan.Should().NotContain(p => p.Target == "Current Releases");
        }

        // ── Golden Fixtures ──────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void GoldenFixture_GenreGapProfile_StableOutput()
        {
            // Fixed input → deterministic snapshot
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.5,
                    ["Jazz"] = 6.0,
                    ["Rock"] = 70.0
                },
                preferredEras: new List<string> { "Modern", "Contemporary" },
                newReleaseRatio: 0.58);

            var plan = _planner.BuildPlan(profile, maxItems: 5);

            // Ambient gap: 2.5% < 3.0% → priority 90, confidence 0.88
            var ambient = plan.First(p => p.Target == "Ambient");
            ambient.Category.Should().Be("style");
            ambient.Priority.Should().Be(90);
            ambient.Confidence.Should().Be(0.88);
            ambient.ExpectedLift.Should().BeApproximately(0.055, 0.001); // (8.0 - 2.5) / 100

            // Jazz gap: 6.0% is between 3.0% and 8.0% → priority 70, confidence 0.72
            var jazz = plan.First(p => p.Target == "Jazz");
            jazz.Category.Should().Be("style");
            jazz.Priority.Should().Be(70);
            jazz.Confidence.Should().Be(0.72);
            jazz.ExpectedLift.Should().BeApproximately(0.02, 0.001); // (8.0 - 6.0) / 100

            // Catalog Backfill: 0.58 > 0.45 → present
            var backfill = plan.First(p => p.Target == "Catalog Backfill");
            backfill.Category.Should().Be("era-balance");
            backfill.Priority.Should().Be(75);
            backfill.Confidence.Should().Be(0.74);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void GoldenFixture_EraGapProfile_StableOutput()
        {
            // Preferred eras exclude "Classic" → era gap should surface it
            var profile = MakeProfile(
                genres: new Dictionary<string, double> { ["Rock"] = 70.0 },
                preferredEras: new List<string> { "Modern", "Contemporary" },
                newReleaseRatio: 0.25);

            var plan = _planner.BuildPlan(profile, maxItems: 5);

            var eraItem = plan.First(p => p.Category == "era");
            eraItem.Target.Should().Be("Classic");
            eraItem.Priority.Should().Be(80);
            eraItem.Confidence.Should().Be(0.79);
            eraItem.ExpectedLift.Should().Be(0.20);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void GoldenFixture_SimulateWithBudget_StableResult()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.5,
                    ["Jazz"] = 6.0,
                    ["Rock"] = 70.0
                },
                preferredEras: new List<string> { "Modern", "Contemporary" },
                newReleaseRatio: 0.58);

            var result = _planner.Simulate(profile, maxItems: 5, budget: 2);

            result.DryRun.Should().BeTrue();
            result.TotalItems.Should().Be(2);
            result.BudgetApplied.Should().BeTrue();
            result.BudgetRemaining.Should().Be(0); // 2 budget - 2 items = 0

            // Top 2 by priority: Ambient (90) then era Classic (80)
            result.Items[0].Target.Should().Be("Ambient");
            result.Items[1].Target.Should().Be("Classic");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void GoldenFixture_EmptyProfile_ReturnsDefaultEraPlan()
        {
            var profile = MakeProfile();

            var plan = _planner.BuildPlan(profile, maxItems: 5);

            // No genre distribution → no style items
            plan.Should().NotContain(p => p.Category == "style");

            // Default eras (Modern, Contemporary) → missing Classic
            var eraItem = plan.FirstOrDefault(p => p.Category == "era");
            eraItem.Should().NotBeNull();
            eraItem!.Target.Should().Be("Classic");
        }

        // ── Backwards Compatibility ──────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_OriginalOverload_StillWorks()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.0, ["Rock"] = 65.0
                });

            // Original 2-param overload
            var plan = _planner.BuildPlan(profile, 3);
            plan.Should().NotBeEmpty();
            plan.Count.Should().BeLessOrEqualTo(3);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPlan_NullBudget_BehavesLikeOriginal()
        {
            var profile = MakeProfile(
                genres: new Dictionary<string, double>
                {
                    ["Ambient"] = 2.0, ["Rock"] = 65.0
                });

            var original = _planner.BuildPlan(profile, 5);
            var withNullBudget = _planner.BuildPlan(profile, 5, budget: null);

            original.Count.Should().Be(withNullBudget.Count);
            for (int i = 0; i < original.Count; i++)
            {
                original[i].Target.Should().Be(withNullBudget[i].Target);
                original[i].Priority.Should().Be(withNullBudget[i].Priority);
            }
        }
    }
}
