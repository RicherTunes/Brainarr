using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Cost;
using Brainarr.Tests.Helpers;
using Xunit;

namespace Brainarr.Tests.Services.Cost
{
    // Isolated in a non-parallel collection: this test drives TokenCostEstimator's static UsageHistory
    // past its 10k cap, which is inherently O(n^2) (StoreUsageReport does a full-list RemoveAll on every
    // add). Run in parallel, that CPU burst starved a wall-clock timing test
    // (ConcurrencyTests.SyncAsyncBridge_WithTimeout_CancelsCorrectly) into a flake, and the static
    // UsageHistory would also pollute parallel cost tests. DisableParallelization keeps it from
    // overlapping any other collection (same pattern as LimiterRegistryBoundedCollection).
    [CollectionDefinition("TokenCostEstimatorStaticHistory", DisableParallelization = true)]
    public sealed class TokenCostEstimatorStaticHistoryCollection { }

    [Collection("TokenCostEstimatorStaticHistory")]
    [Trait("Category", "Unit")]
    public class TokenCostEstimatorUsageHistoryCapTests
    {
        [Fact]
        public void StoreUsageReport_CapsUsageHistory_AtMaxEntries()
        {
            // #57: UsageHistory is a process-wide static list with a 10k cap; driving it past the cap
            // must evict oldest so it can't grow unbounded over the plugin's weeks-long lifetime.
            // Reset before (clean baseline) AND after (don't leak ~10k entries into other tests).
            var estimator = new TokenCostEstimator(TestLogger.CreateNullLogger());
            TokenCostEstimator.ResetUsageHistoryForTesting();
            try
            {
                for (int i = 0; i < TokenCostEstimator.MaxUsageHistoryEntries + 25; i++)
                {
                    estimator.TrackUsage(AIProvider.OpenAI, "gpt-4o-mini", "p", "r", TimeSpan.FromMilliseconds(1));
                }

                TokenCostEstimator.UsageHistoryCountForTesting.Should().Be(
                    TokenCostEstimator.MaxUsageHistoryEntries,
                    "adding more than the cap must trim history to exactly the cap (oldest evicted)");
            }
            finally
            {
                TokenCostEstimator.ResetUsageHistoryForTesting();
            }
        }

        [Fact]
        public void ResetUsageHistoryForTesting_ClearsAll()
        {
            var estimator = new TokenCostEstimator(TestLogger.CreateNullLogger());
            TokenCostEstimator.ResetUsageHistoryForTesting();
            try
            {
                estimator.TrackUsage(AIProvider.OpenAI, "gpt-4o-mini", "p", "r", TimeSpan.FromMilliseconds(1));
                TokenCostEstimator.UsageHistoryCountForTesting.Should().BeGreaterThan(0);

                TokenCostEstimator.ResetUsageHistoryForTesting();
                TokenCostEstimator.UsageHistoryCountForTesting.Should().Be(0);
            }
            finally
            {
                TokenCostEstimator.ResetUsageHistoryForTesting();
            }
        }
    }
}
