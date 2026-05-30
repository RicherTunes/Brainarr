using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// The per-run gate-metrics scope must isolate concurrent runs (the reason it replaced a shared
    /// cumulative counter+delta, which could leak one run's confidence-floor drops into another's
    /// under-target hint).
    /// </summary>
    public class GateMetricsContextTests
    {
        [Fact]
        public void OutsideScope_AddIsNoOp_AndReadsZero()
        {
            GateMetricsContext.AddConfidenceFloorDrops(5); // no active scope → ignored
            GateMetricsContext.ConfidenceFloorDrops.Should().Be(0);
        }

        [Fact]
        public void Scope_AccumulatesAndResetsOnDispose()
        {
            using (GateMetricsContext.BeginScope())
            {
                GateMetricsContext.AddConfidenceFloorDrops(2);
                GateMetricsContext.AddConfidenceFloorDrops(3);
                GateMetricsContext.ConfidenceFloorDrops.Should().Be(5);
            }
            GateMetricsContext.ConfidenceFloorDrops.Should().Be(0, "the scope is restored on dispose");
        }

        [Fact]
        public async Task ConcurrentScopes_DoNotCrossContaminate()
        {
            // Two runs accumulating concurrently in their own async contexts must not see each other's
            // drops — this is the structural fix for the cumulative-counter race.
            async Task<int> RunAsync(int drops)
            {
                using (GateMetricsContext.BeginScope())
                {
                    GateMetricsContext.AddConfidenceFloorDrops(drops);
                    await Task.Yield();
                    await Task.Delay(10);
                    return GateMetricsContext.ConfidenceFloorDrops;
                }
            }

            var results = await Task.WhenAll(RunAsync(1), RunAsync(7), RunAsync(13));

            results.Should().BeEquivalentTo(new[] { 1, 7, 13 },
                "each concurrent run sees only its own confidence-floor drops");
        }

        [Fact]
        public async Task ChildAsyncWork_WritesVisibleToParentScope()
        {
            // The gate runs inside the awaited pipeline; its writes (mutating the holder the AsyncLocal
            // reference points at) must be visible to the parent (orchestrator) after the await.
            using (GateMetricsContext.BeginScope())
            {
                async Task ChildGateAsync()
                {
                    await Task.Yield();
                    GateMetricsContext.AddConfidenceFloorDrops(4);
                }

                await ChildGateAsync();

                GateMetricsContext.ConfidenceFloorDrops.Should().Be(4,
                    "a child's holder mutation is visible to the parent scope after awaiting");
            }
        }
    }
}
