using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    // Regression coverage for two cache-key correctness bugs:
    //  1. The recommendation cache key omitted the output-gating settings
    //     (MinConfidence/RequireMbids/BackfillStrategy/CustomFilterPatterns/
    //     EnableStrictValidation/QueueBorderlineItems), so tightening a gate and
    //     re-syncing returned the OLD post-gate list for up to CacheDuration.
    //  2. The key used EffectiveModel (== ModelSelection) but the resolver prefers
    //     ManualModelId, so two configs differing only by ManualModelId collided.
    public class RecommendationCacheKeyGatingTests
    {
        private sealed class FakePlannerVersionProvider : IPlannerVersionProvider
        {
            public string GetConfigVersion() => "planner-v1";
        }

        private static RecommendationCacheKeyBuilder NewBuilder()
            => new RecommendationCacheKeyBuilder(new FakePlannerVersionProvider());

        private static BrainarrSettings BaseSettings()
        {
            return new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                ModelSelection = "qwen2.5:latest",
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 10,
                StyleFilters = new[] { "shoegaze", "dreampop" },
                RelaxStyleMatching = true,
                MaxSelectedStyles = 2
            };
        }

        private static LibraryProfile Profile()
        {
            return new LibraryProfile
            {
                TopGenres = new System.Collections.Generic.Dictionary<string, int> { { "rock", 10 } },
                TopArtists = new System.Collections.Generic.List<string> { "A", "B" }
            };
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_MinConfidence_Differs()
        {
            var a = BaseSettings(); a.MinConfidence = 0.7;
            var b = BaseSettings(); b.MinConfidence = 0.9;
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_RequireMbids_Differs()
        {
            var a = BaseSettings(); a.RequireMbids = true;
            var b = BaseSettings(); b.RequireMbids = false;
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_BackfillStrategy_Differs()
        {
            var a = BaseSettings(); a.BackfillStrategy = BackfillStrategy.Off;
            var b = BaseSettings(); b.BackfillStrategy = BackfillStrategy.Aggressive;
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_CustomFilterPatterns_Differ()
        {
            var a = BaseSettings(); a.CustomFilterPatterns = "bootleg";
            var b = BaseSettings(); b.CustomFilterPatterns = "karaoke";
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_EnableStrictValidation_Differs()
        {
            var a = BaseSettings(); a.EnableStrictValidation = false;
            var b = BaseSettings(); b.EnableStrictValidation = true;
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_QueueBorderlineItems_Differs()
        {
            var a = BaseSettings(); a.QueueBorderlineItems = true;
            var b = BaseSettings(); b.QueueBorderlineItems = false;
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_ReviewApproveKeys_Differ()
        {
            var a = BaseSettings(); a.ReviewApproveKeys = new[] { "AC|DC|Highway to Hell" };
            var b = BaseSettings(); b.ReviewApproveKeys = new[] { "Daft Punk|Discovery" };
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Same_When_ReviewApproveKeys_Differ_Only_By_Order_Or_Blanks()
        {
            var a = BaseSettings(); a.ReviewApproveKeys = new[] { "  Daft Punk|Discovery  ", "", "AC|DC|Highway to Hell" };
            var b = BaseSettings(); b.ReviewApproveKeys = new[] { "AC|DC|Highway to Hell", "Daft Punk|Discovery" };
            var kb = NewBuilder();
            Assert.Equal(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Same_When_ReviewApproveKeys_Differ_Only_By_Case()
        {
            // ReviewQueueService matches approval keys case-insensitively, so two case-only
            // variants gate identically and must share a cache entry (avoid a spurious miss).
            var a = BaseSettings(); a.ReviewApproveKeys = new[] { "AC|DC|Highway to Hell" };
            var b = BaseSettings(); b.ReviewApproveKeys = new[] { "ac|dc|highway to hell" };
            var kb = NewBuilder();
            Assert.Equal(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_MaxTopUpIterations_Differs()
        {
            var a = BaseSettings(); a.BackfillStrategy = BackfillStrategy.Standard; a.MaxTopUpIterations = 1;
            var b = BaseSettings(); b.BackfillStrategy = BackfillStrategy.Standard; b.MaxTopUpIterations = 4;
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_GuaranteeExactTarget_Differs()
        {
            var a = BaseSettings(); a.BackfillStrategy = BackfillStrategy.Standard; a.GuaranteeExactTarget = false;
            var b = BaseSettings(); b.BackfillStrategy = BackfillStrategy.Standard; b.GuaranteeExactTarget = true;
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_ThinkingMode_Differs()
        {
            var a = BaseSettings(); a.Provider = AIProvider.Anthropic; a.ThinkingMode = ThinkingMode.Off;
            var b = BaseSettings(); b.Provider = AIProvider.Anthropic; b.ThinkingMode = ThinkingMode.Auto;
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_ThinkingBudgetTokens_Differs()
        {
            var a = BaseSettings(); a.Provider = AIProvider.Anthropic; a.ThinkingMode = ThinkingMode.On; a.ThinkingBudgetTokens = 2000;
            var b = BaseSettings(); b.Provider = AIProvider.Anthropic; b.ThinkingMode = ThinkingMode.On; b.ThinkingBudgetTokens = 8000;
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Differs_When_ManualModelId_Differs_With_Same_ModelSelection()
        {
            var a = BaseSettings(); a.ManualModelId = "gpt-4o";
            var b = BaseSettings(); b.ManualModelId = "gpt-4o-mini";
            var kb = NewBuilder();
            Assert.NotEqual(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Same_When_ManualModelId_Null_vs_Whitespace()
        {
            var a = BaseSettings(); a.ManualModelId = null;
            var b = BaseSettings(); b.ManualModelId = "   ";
            var kb = NewBuilder();
            Assert.Equal(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Same_When_CustomFilterPatterns_Differ_Only_By_Case_Or_Order()
        {
            var a = BaseSettings(); a.CustomFilterPatterns = "Bootleg, Karaoke";
            var b = BaseSettings(); b.CustomFilterPatterns = "karaoke,bootleg";
            var kb = NewBuilder();
            Assert.Equal(kb.Build(a, Profile()), kb.Build(b, Profile()));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Key_Stable_For_Identical_Settings()
        {
            var kb = NewBuilder();
            Assert.Equal(kb.Build(BaseSettings(), Profile()), kb.Build(BaseSettings(), Profile()));
        }
    }
}
