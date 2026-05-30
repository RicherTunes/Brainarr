using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Collection definition for tests that exercise <c>LimiterRegistry</c>'s process-wide STATIC
    /// dictionaries (<c>_throttleUntil</c>, <c>_overrides</c>, …). Three test classes already shared
    /// the <c>"LimiterRegistryBounded"</c> collection NAME, but without this definition xUnit had no
    /// <c>[CollectionDefinition]</c> to attach <see cref="CollectionDefinitionAttribute.DisableParallelization"/>
    /// to — so the collection ran in parallel with every other collection. Other parallel collections
    /// mutate the same statics (e.g. <c>LimiterRegistry.ConfigureFromSettings</c> via the recommendation
    /// generator, or <c>ResetForTesting</c> via the orchestrator fixture), which raced exact-state
    /// assertions here and flaked <c>Insert_AtCapacity_BoundsAllDicts</c> / the maintenance tests under
    /// full-suite load (they passed in isolation).
    ///
    /// <para>DisableParallelization runs this collection serially with respect to all others — the
    /// same mechanism <c>OrchestratorIntegration</c> already uses for the same shared singletons — so
    /// no foreign code mutates <c>LimiterRegistry</c> mid-test. Keep this collection tight: only add
    /// classes that touch LimiterRegistry static state.</para>
    /// </summary>
    [CollectionDefinition("LimiterRegistryBounded", DisableParallelization = true)]
    public class LimiterRegistryBoundedCollection
    {
    }
}
