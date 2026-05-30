using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    /// <summary>
    /// Covers the overall-fetch timeout budget (A1) and the output-token budget (A2).
    /// These make a user's raised "AI Request Timeout" actually take effect and let a full
    /// recommendation list complete in one request instead of being truncated/guillotined.
    /// </summary>
    public class BrainarrSettingsBudgetTests
    {
        // ---- A1: GetOverallFetchTimeoutMs ------------------------------------------------

        [Fact]
        public void OverallFetchTimeout_NeverBelowLegacyFloor()
        {
            // Fast CLOUD config (30s, backfill off) would compute < 120s — must floor at the legacy
            // default so behavior is never SHORTER than before. (Local providers elevate — see below.)
            var s = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                AIRequestTimeoutSeconds = 30,
                BackfillStrategy = BackfillStrategy.Off
            };
            s.GetOverallFetchTimeoutMs().Should().Be(BrainarrConstants.DefaultAsyncTimeoutMs);
        }

        [Fact]
        public void OverallFetchTimeout_ScalesWithPerRequestTimeout()
        {
            var lo = new BrainarrSettings { AIRequestTimeoutSeconds = 60, BackfillStrategy = BackfillStrategy.Standard };
            var hi = new BrainarrSettings { AIRequestTimeoutSeconds = 360, BackfillStrategy = BackfillStrategy.Standard };
            hi.GetOverallFetchTimeoutMs().Should().BeGreaterThan(lo.GetOverallFetchTimeoutMs(),
                "raising the per-request timeout must lengthen the overall budget (the A1 fix)");
        }

        [Fact]
        public void OverallFetchTimeout_RespectsConfiguredTimeout_NotHardcoded120s()
        {
            // The regression this guards: a 360s setting used to be silently capped at 120s.
            var s = new BrainarrSettings { AIRequestTimeoutSeconds = 360, BackfillStrategy = BackfillStrategy.Standard };
            // 360s × (1 initial + >=1 top-up) + overhead is well past 120s.
            s.GetOverallFetchTimeoutMs().Should().BeGreaterThan(BrainarrConstants.DefaultAsyncTimeoutMs);
        }

        [Fact]
        public void OverallFetchTimeout_BackfillOff_IsSingleCallPlusOverhead()
        {
            var s = new BrainarrSettings { AIRequestTimeoutSeconds = 200, BackfillStrategy = BackfillStrategy.Off };
            var expected = (200 + BrainarrConstants.FetchOverheadSeconds) * 1000;
            s.GetOverallFetchTimeoutMs().Should().Be(expected);
        }

        [Fact]
        public void OverallFetchTimeout_LocalProvider_ElevatesShortTimeout()
        {
            // Ollama/LM Studio at the default 30s actually run up to LocalProviderDefaultTimeout per
            // request; the overall budget must mirror that or it would guillotine a single local call.
            var s = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                AIRequestTimeoutSeconds = 30,
                BackfillStrategy = BackfillStrategy.Off
            };
            var expected = (BrainarrConstants.LocalProviderDefaultTimeout + BrainarrConstants.FetchOverheadSeconds) * 1000;
            s.GetOverallFetchTimeoutMs().Should().Be(expected);
            s.GetOverallFetchTimeoutMs().Should().BeGreaterThan(BrainarrConstants.DefaultAsyncTimeoutMs);
        }

        [Fact]
        public void OverallFetchTimeout_NeverShorterThanASingleLocalRequest()
        {
            // Regression for the adversarial-review finding: budget must not be shorter than one
            // (elevated) local request, or SafeAsyncHelper kills the run mid-request.
            var s = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                AIRequestTimeoutSeconds = 10,
                BackfillStrategy = BackfillStrategy.Off
            };
            s.GetOverallFetchTimeoutMs().Should()
                .BeGreaterThanOrEqualTo(BrainarrConstants.LocalProviderDefaultTimeout * 1000);
        }

        [Fact]
        public void OverallFetchTimeout_CappedAtCeiling()
        {
            var s = new BrainarrSettings
            {
                AIRequestTimeoutSeconds = BrainarrConstants.MaxAITimeout, // 600s
                BackfillStrategy = BackfillStrategy.Aggressive,
                MaxTopUpIterations = 50 // force a huge product
            };
            s.GetOverallFetchTimeoutMs().Should().Be(BrainarrConstants.MaxOverallFetchTimeoutMs);
        }

        // ---- A2: GetOutputTokenBudget ----------------------------------------------------

        [Fact]
        public void OutputTokenBudget_ScalesWithTarget_WhenTimeAllows()
        {
            // Scaling is only visible when the per-request timeout grants enough generation time.
            var small = new BrainarrSettings { MaxRecommendations = 10, AIRequestTimeoutSeconds = 600 };
            var large = new BrainarrSettings { MaxRecommendations = 50, AIRequestTimeoutSeconds = 600 };
            large.GetOutputTokenBudget().Should().BeGreaterThan(small.GetOutputTokenBudget());
        }

        [Fact]
        public void OutputTokenBudget_NeverBelowLegacyDefault()
        {
            var s = new BrainarrSettings { MaxRecommendations = 1, AIRequestTimeoutSeconds = 600 };
            s.GetOutputTokenBudget().Should().BeGreaterThanOrEqualTo(BrainarrConstants.DefaultMaxTokens);
        }

        [Fact]
        public void OutputTokenBudget_ShortTimeout_FloorsToSafeDefault()
        {
            // At the default 30s timeout a slow model can't finish a big list, so the budget must NOT
            // balloon (overshoot → cancelled mid-stream → 0 salvageable). It floors to the safe 2000.
            var s = new BrainarrSettings { MaxRecommendations = 50, AIRequestTimeoutSeconds = 30 };
            s.GetOutputTokenBudget().Should().Be(BrainarrConstants.DefaultMaxTokens);
        }

        [Fact]
        public void OutputTokenBudget_BoundedByTimeout()
        {
            // A longer timeout permits a larger budget than a shorter one for the same big target.
            var shortT = new BrainarrSettings { MaxRecommendations = 100, AIRequestTimeoutSeconds = 90 };
            var longT = new BrainarrSettings { MaxRecommendations = 100, AIRequestTimeoutSeconds = 300 };
            longT.GetOutputTokenBudget().Should().BeGreaterThan(shortT.GetOutputTokenBudget());
        }

        [Fact]
        public void OutputTokenBudget_CappedAtCeiling()
        {
            var s = new BrainarrSettings { MaxRecommendations = 100000, AIRequestTimeoutSeconds = 600 };
            s.GetOutputTokenBudget().Should().Be(BrainarrConstants.MaxOutputTokensCeiling);
        }

        [Fact]
        public void OutputTokenBudget_TargetOf50_WithGenerousTimeout_FitsAFullCompactList()
        {
            // 50 × 160 + 600 = 8600 tokens — enough to finish a 50-item compact array, and 360s @ ~50
            // tok/s (=18000 ceiling) leaves ample headroom, so the desired budget is what's used.
            var s = new BrainarrSettings { MaxRecommendations = 50, AIRequestTimeoutSeconds = 360 };
            s.GetOutputTokenBudget().Should().BeGreaterThan(8000);
        }
    }
}
