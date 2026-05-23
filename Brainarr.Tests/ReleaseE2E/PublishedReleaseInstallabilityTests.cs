using System.Threading.Tasks;
using Lidarr.Plugin.Common.TestKit.Compliance;
using Xunit;

namespace Brainarr.Tests.ReleaseE2E;

/// <summary>
/// Regression backstop against Lidarr's <c>PluginService.GetRemotePlugin</c> filter applied
/// to the LIVE GitHub releases of this repo. Actual assertions live in
/// <see cref="PublishedReleaseInstallabilityContract"/> in Common.TestKit — this class is
/// just per-plugin glue.
///
/// <para>Opt-in via [Trait("Category", "ReleaseE2E")]; skipped in the default test sweep.</para>
/// </summary>
public class PublishedReleaseInstallabilityTests
{
    private const string Owner = "RicherTunes";
    private const string Repo = "Brainarr";

    private static readonly PluginPackagePolicy Policy = PluginPackagingContract.MergedDllPolicy(
        mainAssemblyName: "Lidarr.Plugin.Brainarr",
        extraRequired: new[] { "manifest.json" });

    [SkippableFact]
    [Trait("Category", "ReleaseE2E")]
    public Task LatestPublishedRelease_PassesLidarrInstallFilter() =>
        PublishedReleaseInstallabilityContract
            .AssertLatestReleasePassesLidarrInstallFilterAsync(Owner, Repo);

    [SkippableFact]
    [Trait("Category", "ReleaseE2E")]
    public Task LatestPublishedRelease_ZipContents_Match_PackagingPolicy() =>
        PublishedReleaseInstallabilityContract
            .AssertLatestReleaseZipMatchesPolicyAsync(Owner, Repo, Policy);
}
