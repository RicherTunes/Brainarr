using System.Threading;
using Lidarr.Plugin.Common.Extensions;
using Lidarr.Plugin.Common.Hosting;
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
    // Guard so that PluginLifecycle.RegisterShutdown is called exactly once per plugin
    // AssemblyLoadContext lifetime, even if multiple BrainarrModule instances are created
    // (e.g., during unit tests). 0 = not registered, 1 = registered.
    private static int _hooksRegistered = 0;

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
    /// Registers static plugin-level teardown hooks with Common's <see cref="PluginLifecycle"/>
    /// registry. Called once per plugin load via <see cref="StreamingPluginModule.RegisterServices"/>.
    /// </summary>
    protected override void RegisterCustomServices()
    {
        base.RegisterCustomServices();

        // CAS ensures we register exactly once, even if RegisterServices() is called multiple
        // times (e.g., in tests that construct multiple BrainarrModule instances).
        if (Interlocked.CompareExchange(ref _hooksRegistered, 1, 0) != 0)
            return;

        // LIFO order: LimiterRegistry tears down before MetricsCollector.
        PluginLifecycle.RegisterShutdown("MetricsCollector", MetricsCollector.Dispose);
        PluginLifecycle.RegisterShutdown("LimiterRegistry", LimiterRegistry.Dispose);
    }

    /// <summary>
    /// Disposes DI singletons (via base), then invokes all registered
    /// <see cref="PluginLifecycle"/> shutdown hooks in LIFO order.
    /// Static teardown hooks (MetricsCollector, LimiterRegistry, …) are registered
    /// in <see cref="RegisterCustomServices"/> — add new ones there, not here.
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        PluginLifecycle.Shutdown();
        // Reset the hook-registration guard so the next plugin reload re-registers cleanly.
        Interlocked.Exchange(ref _hooksRegistered, 0);
    }
}
