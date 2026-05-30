using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// The synchronous Fetch() entry point bridges to async via SafeAsyncHelper.RunSafeSync.
    /// Its timeout must never be shorter than the inner per-request budget, otherwise a
    /// slow-but-valid run (e.g. a local LLM legitimately using the 360s LocalProviderDefaultTimeout)
    /// is aborted by the bridge before the provider's own timeout elapses.
    /// </summary>
    [Trait("Category", "Unit")]
    public class BrainarrOrchestratorSyncTimeoutTests
    {
        [Fact]
        public void Resolves_at_least_the_local_provider_budget_for_default_settings()
        {
            // Default AIRequestTimeoutSeconds (30s) is auto-bumped to LocalProviderDefaultTimeout
            // (360s) for local providers inside the pipeline; the sync bridge must outlast that.
            var settings = new BrainarrSettings { AIRequestTimeoutSeconds = BrainarrConstants.DefaultAITimeout };

            var timeoutMs = BrainarrOrchestrator.ResolveSyncFetchTimeoutMs(settings);

            timeoutMs.Should().BeGreaterThanOrEqualTo(BrainarrConstants.LocalProviderDefaultTimeout * 1000);
            timeoutMs.Should().BeGreaterThan(BrainarrConstants.DefaultAsyncTimeoutMs,
                "the old fixed 120s cap aborted valid local-provider runs");
        }

        [Fact]
        public void Resolves_at_least_a_raised_request_timeout()
        {
            var settings = new BrainarrSettings { AIRequestTimeoutSeconds = BrainarrConstants.MaxAITimeout };

            var timeoutMs = BrainarrOrchestrator.ResolveSyncFetchTimeoutMs(settings);

            timeoutMs.Should().BeGreaterThanOrEqualTo(BrainarrConstants.MaxAITimeout * 1000);
        }

        [Fact]
        public void Never_drops_below_the_legacy_default_floor()
        {
            var settings = new BrainarrSettings { AIRequestTimeoutSeconds = BrainarrConstants.MinAITimeout };

            var timeoutMs = BrainarrOrchestrator.ResolveSyncFetchTimeoutMs(settings);

            timeoutMs.Should().BeGreaterThanOrEqualTo(BrainarrConstants.DefaultAsyncTimeoutMs);
        }

        [Fact]
        public void Handles_null_settings_without_throwing()
        {
            var timeoutMs = BrainarrOrchestrator.ResolveSyncFetchTimeoutMs(null);

            timeoutMs.Should().BeGreaterThanOrEqualTo(BrainarrConstants.LocalProviderDefaultTimeout * 1000);
        }
    }
}
