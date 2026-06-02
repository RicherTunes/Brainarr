using System.IO;
using System.Text.Json;
using Xunit;

namespace Brainarr.Tests.Contracts;

/// <summary>
/// Catches version drift between sources of truth: VERSION file, plugin.json,
/// manifest.json, and assembly metadata.
///
/// Background: Properties/AssemblyInfo.cs once carried a hardcoded
/// <c>[assembly: AssemblyVersion("1.3.2.0")]</c> literal that stayed put through
/// the 1.4.0 and 1.4.1 releases (because <c>&lt;GenerateAssemblyInfo&gt;false&lt;/GenerateAssemblyInfo&gt;</c>
/// made Directory.Build.props' VERSION-file-driven version inert). Result:
/// <c>/api/v1/system/plugins</c> reported installedVersion=1.3.2 while the actual
/// release was 1.4.1. The csproj has since been switched to
/// <c>&lt;GenerateAssemblyInfo&gt;true&lt;/GenerateAssemblyInfo&gt;</c>; this contract test
/// makes sure the four sources of truth never drift apart again.
/// </summary>
public class VersionContractTests
{
    [Fact]
    public void AssemblyVersion_MatchesPluginJsonVersion()
    {
        var pluginJsonPath = LocatePluginJson();
        Skip.If(pluginJsonPath is null, "plugin.json not found in baseDir or repo root");

        using var doc = JsonDocument.Parse(File.ReadAllText(pluginJsonPath!));
        var expected = doc.RootElement.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(expected), "plugin.json must declare a version");

        var asmVersion = typeof(NzbDrone.Core.ImportLists.Brainarr.Hosting.BrainarrInstalledPlugin)
            .Assembly.GetName().Version?.ToString(3);

        Assert.Equal(expected, asmVersion);
    }

    [Fact]
    public void ManifestJson_MatchesPluginJsonVersion()
    {
        var pluginJsonPath = LocatePluginJson();
        var manifestJsonPath = LocateRepoFile("manifest.json");
        Skip.If(pluginJsonPath is null || manifestJsonPath is null,
            "plugin.json or manifest.json not found in repo");

        using var pluginDoc = JsonDocument.Parse(File.ReadAllText(pluginJsonPath!));
        using var manifestDoc = JsonDocument.Parse(File.ReadAllText(manifestJsonPath!));

        var pluginVersion = pluginDoc.RootElement.GetProperty("version").GetString();
        var manifestVersion = manifestDoc.RootElement.GetProperty("version").GetString();

        Assert.Equal(pluginVersion, manifestVersion);
    }

    [Fact]
    public void Manifest_DoesNotReferenceLegacyLidarrPluginFile()
    {
        // The legacy pre-3.0 ".lidarr.plugin" discovery file was removed: the live host doesn't read
        // it (discovery is DLL-glob + the Plugin subclass), and it shipped frozen-stale metadata
        // (version 1.0.3, a net6.0 releaseUrl) because the release version-stamp never rewrote it.
        // Guard against re-introducing the file or a dangling manifest entry that points at it.
        var manifestJsonPath = LocateRepoFile("manifest.json");
        Skip.If(manifestJsonPath is null, "manifest.json not found in repo");

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestJsonPath!));
        if (doc.RootElement.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in files.EnumerateArray())
            {
                var path = f.TryGetProperty("path", out var p) ? p.GetString() : null;
                Assert.False(string.Equals(path, ".lidarr.plugin", System.StringComparison.OrdinalIgnoreCase),
                    "manifest.json must not list the removed legacy .lidarr.plugin file");
            }
        }

        var repoRoot = Path.GetDirectoryName(manifestJsonPath!);
        Assert.False(File.Exists(Path.Combine(repoRoot!, ".lidarr.plugin")),
            "the legacy .lidarr.plugin discovery file was removed and must not return");
    }

    /// <summary>
    /// Pins the recurring drift pattern: <c>plugin.json</c> gets <c>commonVersion</c>
    /// bumped by a release commit, but <c>manifest.json</c> doesn't. Apple hit this 3
    /// times in May 2026 (v0.5.5/v0.5.6, v0.5.7, v0.5.8) before adding the equivalent
    /// test (<c>AppleMusicarr.Core.Tests/Contracts/VersionContractTests.cs:59</c>).
    /// Ported here as part of the parity-mission wave to close the documentation gap
    /// the audit flagged: brainarr's <c>plugin.json</c> declared <c>commonVersion</c>
    /// but <c>manifest.json</c> didn't, so a future bump of one without the other would
    /// have been silent.
    /// </summary>
    [Fact]
    public void ManifestJson_MatchesPluginJsonCommonVersion()
    {
        var pluginJsonPath = LocatePluginJson();
        var manifestJsonPath = LocateRepoFile("manifest.json");
        Skip.If(pluginJsonPath is null || manifestJsonPath is null,
            "plugin.json or manifest.json not found in repo");

        using var pluginDoc = JsonDocument.Parse(File.ReadAllText(pluginJsonPath!));
        using var manifestDoc = JsonDocument.Parse(File.ReadAllText(manifestJsonPath!));

        var pluginCommonVersion = pluginDoc.RootElement.GetProperty("commonVersion").GetString();
        var manifestCommonVersion = manifestDoc.RootElement.GetProperty("commonVersion").GetString();

        Assert.Equal(pluginCommonVersion, manifestCommonVersion);
    }

    [Fact]
    public void VersionFile_MatchesPluginJsonVersion()
    {
        var versionPath = LocateRepoFile("VERSION");
        var pluginJsonPath = LocatePluginJson();
        Skip.If(versionPath is null || pluginJsonPath is null,
            "VERSION or plugin.json not found — only enforced for repo-rooted runs");

        var versionFile = File.ReadAllText(versionPath!).Trim();
        using var doc = JsonDocument.Parse(File.ReadAllText(pluginJsonPath!));
        var pluginJson = doc.RootElement.GetProperty("version").GetString();

        Assert.Equal(versionFile, pluginJson);
    }

    private static string? LocatePluginJson()
    {
        // AppContext.BaseDirectory — copied here by the SDK at build time
        var candidate = Path.Combine(AppContext.BaseDirectory, "plugin.json");
        if (File.Exists(candidate)) return candidate;
        return LocateRepoFile("plugin.json");
    }

    private static string? LocateRepoFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
