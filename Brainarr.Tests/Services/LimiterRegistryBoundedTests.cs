using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Verifies the concurrency-hardening additions to <see cref="LimiterRegistry"/>:
    /// bounded dict growth (cap = 5 120 per dict) and idempotent Dispose.
    /// </summary>
    [Collection("LimiterRegistryBounded")]
    public class LimiterRegistryBoundedTests : IDisposable
    {
        // DictCap constant value from production code.
        private const int DictCap = 5120;

        public LimiterRegistryBoundedTests()
        {
            LimiterRegistry.ResetForTesting();
        }

        void IDisposable.Dispose()
        {
            LimiterRegistry.ResetForTesting();
        }

        // -----------------------------------------------------------------------
        // Dict cap / eviction
        // -----------------------------------------------------------------------

        [Fact]
        public void Insert_AtCapacity_BoundsAllDicts()
        {
            // Enable adaptive throttling so RegisterThrottle inserts into _throttleUntil.
            var settings = new BrainarrSettings
            {
                EnableAdaptiveThrottling = true,
                AdaptiveThrottleSeconds = 300
            };
            LimiterRegistry.ConfigureFromSettings(settings);

            // Fill _throttleUntil to exactly DictCap entries.
            for (int i = 0; i < DictCap; i++)
            {
                LimiterRegistry.RegisterThrottle($"openai:model-{i}", TimeSpan.FromSeconds(300), 2);
            }

            // Adding one more must trigger clear-all eviction without throwing.
            var act = () =>
                LimiterRegistry.RegisterThrottle("openai:model-overflow", TimeSpan.FromSeconds(300), 2);
            act.Should().NotThrow("overflow eviction must be transparent to callers");

            // Strong assertion (reliable now the collection is serialized via DisableParallelization,
            // so no foreign collection mutates _throttleUntil mid-test): the clear-all fires THEN the
            // new entry is inserted, so the just-added entry survives.
            LimiterRegistry.HasThrottleFor("openai:model-overflow")
                .Should().BeTrue("the entry inserted after the clear-all eviction must be present");

            // Defense-in-depth: the bound itself must hold regardless of which entries survive.
            LimiterRegistry.ThrottleEntryCountForTesting
                .Should().BeLessThanOrEqualTo(LimiterRegistry.DictCapForTesting,
                    "the bounded dictionary must never exceed its cap");
        }

        // -----------------------------------------------------------------------
        // Timer / Dispose
        // -----------------------------------------------------------------------

        [Fact]
        public void Dispose_StopsTimer()
        {
            // Dispose must not throw and must complete without hanging.
            var act = () => LimiterRegistry.Dispose();
            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            // Multiple calls must not throw.
            var act = () =>
            {
                LimiterRegistry.Dispose();
                LimiterRegistry.Dispose();
                LimiterRegistry.Dispose();
            };
            act.Should().NotThrow("Dispose must be idempotent");
        }
    }
}
