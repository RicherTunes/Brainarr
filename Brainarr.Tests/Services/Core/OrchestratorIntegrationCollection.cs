using System;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Shared fixture that resets all static singletons touched by orchestrator tests
    /// (LimiterRegistry, ResiliencePolicy, ModelRegistryLoader).
    /// <para>
    /// xUnit creates this once per collection run. Constructor fires before the first
    /// test class; Dispose fires after the last.  Individual test classes that need a
    /// per-test reset can still call the methods directly, but this fixture guarantees
    /// a clean baseline even if a test forgets.
    /// </para>
    /// </summary>
    public sealed class OrchestratorStaticStateFixture : IDisposable
    {
        public OrchestratorStaticStateFixture()
        {
            ResetAll();
        }

        public void Dispose()
        {
            ResetAll();
        }

        public static void ResetAll()
        {
            NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.ResetForTesting();
            ModelRegistryLoader.InvalidateSharedCache();
            LimiterRegistry.ResetForTesting();
        }
    }

    /// <summary>
    /// Collection definition that serializes orchestrator tests using shared static state.
    /// <para>
    /// Keep this collection tight — only add test classes that truly need serialized
    /// access to LimiterRegistry / ResiliencePolicy / ModelRegistryLoader.  Tests that
    /// only mock the orchestrator (no real static state) should NOT be in this collection.
    /// </para>
    /// </summary>
    [CollectionDefinition("OrchestratorIntegration", DisableParallelization = true)]
    public class OrchestratorIntegrationCollection : ICollectionFixture<OrchestratorStaticStateFixture>
    {
    }
}
