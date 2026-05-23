using Lidarr.Plugin.Common.TestKit.Compliance;
using NzbDrone.Core.ImportLists.Brainarr.Hosting;
using Xunit;

namespace Brainarr.Tests.Contracts;

/// <summary>
/// Catches version drift between AssemblyVersion, plugin.json, manifest.json, and the
/// top-level VERSION file. The actual assertions live in <see cref="PluginVersionContract"/>
/// in Common.TestKit — this class is just per-plugin glue.
///
/// See <c>ext/Lidarr.Plugin.Common/testkit/Compliance/PluginVersionContract.cs</c> for
/// the background incidents this catches (brainarr's AssemblyInfo.cs literal "1.3.2.0"
/// stayed put through 1.4.x; tidalarr's TidalModule.Version "1.1.0" stayed through 1.1.1;
/// applemusicarr's manifest.json "0.3.0-beta.2" stayed through 0.4.0).
/// </summary>
public class VersionContractTests
{
    [Fact]
    public void AssemblyVersion_MatchesPluginJsonVersion() =>
        PluginVersionContract.AssertAssemblyVersionMatchesPluginJson(typeof(BrainarrInstalledPlugin));

    [Fact]
    public void ManifestJson_MatchesPluginJsonVersion() =>
        PluginVersionContract.AssertManifestMatchesPluginJson(typeof(BrainarrInstalledPlugin));

    [Fact]
    public void VersionFile_MatchesPluginJsonVersion() =>
        PluginVersionContract.AssertVersionFileMatchesPluginJson(typeof(BrainarrInstalledPlugin));
}
