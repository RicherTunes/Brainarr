using Lidarr.Plugin.Common.Extensions;
using Lidarr.Plugin.Common.Services.Registration;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Hosting;

/// <summary>
/// Wiring module used to build dependency injection container instances for Brainarr.
/// This allows both the legacy import list surface and the new plugin host bridge to
/// resolve <see cref="IBrainarrOrchestrator"/> (and related services) in a consistent way.
/// </summary>
public sealed class BrainarrModule : StreamingPluginModule
{
    public override string ServiceName => "Brainarr";
    public override string Description => "AI-powered music discovery import list";
    public override string Author => "RicherTunes";

    /// <summary>
    /// Brainarr is an import list only — it does not provide an indexer.
    /// </summary>
    protected override bool HasIndexer() => false;

    /// <summary>
    /// Brainarr is an import list only — it does not provide a download client.
    /// </summary>
    protected override bool HasDownloadClient() => false;

    protected override void ConfigureServices(IServiceCollection services)
    {
        BrainarrOrchestratorFactory.ConfigureServices(services);

        // Register common's default no-op bridge handlers (auth-failure, indexer/download
        // status, rate-limit). Brainarr is an import-list-only plugin and does not exercise
        // these bridge contracts in its primary code paths, but registering the defaults
        // satisfies the cross-plugin ecosystem parity contract (Check_RegistersBridgeDefaults
        // in EcosystemParityTestBase) and prevents NREs from any common-library helper that
        // optionally resolves a bridge handler. Uses TryAddSingleton so any custom handler
        // already registered above takes precedence.
        services.AddBridgeDefaults();
    }

    /// <summary>
    /// Disposes DI singletons (base), then disposes static plugin-level resources
    /// whose lifetimes are tied to the plugin's AssemblyLoadContext:
    /// <list type="bullet">
    ///   <item>MetricsCollector.Dispose — stops the hourly cleanup timer.</item>
    ///   <item>LimiterRegistry.Dispose — stops the 10-minute maintenance timer.</item>
    /// </list>
    /// Both calls are idempotent. Wave 8A additions should append further static Dispose()
    /// calls after the two below.
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        MetricsCollector.Dispose();
        LimiterRegistry.Dispose();
    }
}
