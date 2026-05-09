using System.IO;
using System.Linq;
using Lidarr.Plugin.Common.TestKit.Hosting;
using Xunit;

namespace Brainarr.Tests.Runtime;

/// <summary>
/// Brainarr-specific subclass that pre-fills the per-plugin
/// <see cref="LidarrContainerOptions"/> consumed by common's lifted
/// <see cref="Lidarr.Plugin.Common.TestKit.Hosting.LidarrContainerFixture"/>.
///
/// Wave 22b — orchestration logic (container lifecycle, healthcheck,
/// log capture, skip-when-no-Docker) lives in TestKit. This file keeps
/// only the per-plugin constants:
///   - container name           : brainarr-e2e
///   - host port                : 8693 (avoids tidalarr 8690 / applemusicarr 8691 / qobuzarr 8692)
///   - Docker image             : pinned net8 plugins-branch tag
///   - plugin mount path        : /config/plugins/RicherTunes/Brainarr
///   - plugin DLL filename      : Lidarr.Plugin.Brainarr.dll
///   - schema-entry substring   : "Brainarr"
///   - plugin DLL discovery     : Brainarr.Plugin/bin/{,Release,Debug}/Lidarr.Plugin.Brainarr.dll
///
/// Note: brainarr is an ImportList plugin (HasIndexer=false,
/// HasDownloadClient=false) so smoke tests assert against
/// /api/v1/importlist/{schema,test} rather than indexer/downloadclient.
/// </summary>
public sealed class BrainarrLidarrContainerFixture
    : Lidarr.Plugin.Common.TestKit.Hosting.LidarrContainerFixture
{
    public BrainarrLidarrContainerFixture()
        : base(BuildOptions())
    {
    }

    private static LidarrContainerOptions BuildOptions() => new(
        DockerImage: "ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913",
        ContainerName: "brainarr-e2e",
        LidarrPort: 8693,
        PluginMountPath: "/config/plugins/RicherTunes/Brainarr",
        PluginDllFileName: "Lidarr.Plugin.Brainarr.dll",
        FindPluginDll: FindBrainarrPluginDll,
        PluginEntrySubstring: "Brainarr",
        RepoRootMarkerFile: "Brainarr.sln");

    private static string? FindBrainarrPluginDll(string repoRoot)
    {
        string[] candidates =
        [
            Path.Combine(repoRoot, "Brainarr.Plugin", "bin", "Lidarr.Plugin.Brainarr.dll"),
            Path.Combine(repoRoot, "Brainarr.Plugin", "bin", "Release", "Lidarr.Plugin.Brainarr.dll"),
            Path.Combine(repoRoot, "Brainarr.Plugin", "bin", "Debug", "Lidarr.Plugin.Brainarr.dll"),
            Path.Combine(repoRoot, "_plugins", "Brainarr.Plugin", "Lidarr.Plugin.Brainarr.dll"),
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}

/// <summary>
/// xUnit collection definition that lets all E2E tests share the single
/// <see cref="BrainarrLidarrContainerFixture"/> instance.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LidarrContainerCollection : ICollectionFixture<BrainarrLidarrContainerFixture>
{
    public const string Name = "LidarrContainer";
}
