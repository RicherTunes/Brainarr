using Lidarr.Plugin.Common.Services.Registration;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;

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

    protected override void ConfigureServices(IServiceCollection services)
    {
        BrainarrOrchestratorFactory.ConfigureServices(services);
    }
}
