using NzbDrone.Core.Plugins;

namespace NzbDrone.Core.ImportLists.Brainarr.Hosting;

/// <summary>
/// Registers Brainarr with Lidarr's "System → Plugins" UI.
///
/// Lidarr has TWO distinct <c>IPlugin</c> interfaces (which is easy to conflate):
///
/// <list type="bullet">
///   <item><c>NzbDrone.Core.Plugins.IPlugin</c> (from <c>Lidarr.Core.dll</c>) — the host's
///         interface, used by <c>PluginService.GetInstalledPlugins()</c> /
///         <c>/api/v1/system/plugins</c> for plugin management (UI listing, update checks,
///         uninstall).</item>
///   <item><c>Lidarr.Plugin.Abstractions.IPlugin</c> (Common, internalized via ILRepack) —
///         the cross-ALC sandbox contract, never read by the live host.</item>
/// </list>
///
/// Brainarr extends Common's <see cref="Lidarr.Plugin.Common.Hosting.StreamingPlugin{TModule,TSettings}"/>
/// for the bridge contract, and exposes an <c>ImportListBase</c> derivative for discovery.
/// Neither of those satisfies the host's <see cref="IPlugin"/>, so without this class the
/// plugin is fully functional (the ImportList schema lists it; you can configure and run it)
/// but invisible to the System → Plugins UI.
///
/// DryIoc's <c>RegisterMany</c> auto-discovers this class because brainarr's plugin assembly
/// is loaded into Lidarr's container; no manual registration is needed.
/// <c>InstalledVersion</c> is auto-derived by the base class from
/// <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>.
/// </summary>
public sealed class BrainarrInstalledPlugin : Plugin
{
    public override string Name => "Brainarr";
    public override string Owner => "RicherTunes";
    public override string GithubUrl => "https://github.com/RicherTunes/Brainarr";
}
