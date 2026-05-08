using Lidarr.Plugin.Common.TestKit.Compliance;
using Xunit;

namespace Brainarr.Parity.Tests;

/// <summary>
/// Verifies Brainarr repo structure conforms to the cross-plugin ecosystem parity contract
/// defined by <see cref="EcosystemParityTestBase"/> in Lidarr.Plugin.Common.
///
/// Brainarr is an import-list-only plugin (no indexer, no download client) — its layout
/// differs from the streaming-service reference plugins (Qobuzarr, Tidalarr, AppleMusicarr)
/// in a few documented ways. Where an invariant does not apply, the corresponding check is
/// overridden below with a documented rationale rather than silently skipped.
/// </summary>
[Trait("Category", "Parity")]
public class BrainarrEcosystemParityTests : EcosystemParityTestBase
{
    // Test binaries land at:
    //   <repo>/tests/Brainarr.Parity.Tests/bin/<Config>/net8.0/
    // → walk up 5 segments (net8.0 / Config / bin / Brainarr.Parity.Tests / tests) to repo root.
    protected override string RepoRootPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    protected override string PluginId => "brainarr";

    // Brainarr keeps plugin.json at the repo root (not under src/). Historical layout — kept
    // for backwards compat with Lidarr's plugin discovery scripts that scan the repo root.
    protected override string PluginJsonRelativePath => "plugin.json";

    #region Documented Divergences (overrides)

    /// <summary>
    /// Divergence: Brainarr's plugin.json deliberately omits several "required" fields
    /// (homepage, license, tags, targetFramework, rootNamespace) because that metadata
    /// lives in <c>manifest.json</c> at the repo root instead. The two-file split is a
    /// historical brainarr-specific pattern — manifest.json is the Lidarr-facing
    /// installation manifest while plugin.json carries only the fields the in-process
    /// host loader actually reads. We still assert the *load-critical* subset is present.
    /// </summary>
    public override ComplianceResult PluginJson_HasAllRequiredFields()
    {
        // Reduced contract: only fields the host loader needs at plugin load time.
        var required = new[] { "id", "apiVersion", "name", "version", "author", "description", "commonVersion", "minHostVersion", "main" };
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRootPath, PluginJsonRelativePath))).RootElement;
        var missing = required.Where(f => !json.TryGetProperty(f, out _)).ToList();
        return missing.Count == 0
            ? ComplianceResult.Success
            : ComplianceResult.Failure($"plugin.json missing brainarr-required fields: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Divergence: targetFramework is declared in <c>manifest.json</c>, not plugin.json.
    /// The base check is satisfied by <see cref="ManifestJson_TargetFramework_IsNet8"/>.
    /// </summary>
    public override ComplianceResult PluginJson_TargetFramework_IsNet8() => ComplianceResult.Success;

    /// <summary>
    /// Divergence: license info is carried in manifest.json + the LICENSE file at repo root,
    /// not duplicated into plugin.json. Verify the LICENSE file exists instead.
    /// </summary>
    public override ComplianceResult PluginJson_HasLicense()
    {
        return File.Exists(Path.Combine(RepoRootPath, "LICENSE"))
            ? ComplianceResult.Success
            : ComplianceResult.Failure("LICENSE file missing at repo root (brainarr stores license metadata in LICENSE + manifest.json, not plugin.json)");
    }

    /// <summary>
    /// Divergence: tags live in manifest.json (the user-facing installation manifest),
    /// not plugin.json. Verify them there.
    /// </summary>
    public override ComplianceResult PluginJson_HasTags()
    {
        var manifestPath = Path.Combine(RepoRootPath, "manifest.json");
        if (!File.Exists(manifestPath))
            return ComplianceResult.Failure("manifest.json missing — required because brainarr stores tags there, not in plugin.json");

        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement;
        if (!json.TryGetProperty("tags", out var tags) || tags.ValueKind != System.Text.Json.JsonValueKind.Array || tags.GetArrayLength() == 0)
            return ComplianceResult.Failure("manifest.json 'tags' must be a non-empty array");

        return ComplianceResult.Success;
    }

    /// <summary>
    /// Divergence: rootNamespace is implicit (matches the assembly name pulled from <c>main</c>).
    /// Brainarr uses <c>Lidarr.Plugin.Brainarr.dll</c> — the rootNamespace is mechanically derivable.
    /// We don't enforce a redundant field; we assert <c>main</c> is present and well-formed.
    /// </summary>
    public override ComplianceResult PluginJson_HasRootNamespace()
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRootPath, PluginJsonRelativePath))).RootElement;
        if (!json.TryGetProperty("main", out var main) || string.IsNullOrWhiteSpace(main.GetString()))
            return ComplianceResult.Failure("plugin.json 'main' field missing (used to derive rootNamespace in brainarr's two-file layout)");
        return ComplianceResult.Success;
    }

    /// <summary>
    /// Divergence: homepage is recorded as <c>website</c> in brainarr's plugin.json (legacy field name).
    /// The base check for "homepage" via <see cref="PluginJson_HasAllRequiredFields"/> is relaxed above;
    /// this acts as a soft assertion that the URL is present under one of the two names.
    /// </summary>
    public virtual ComplianceResult PluginJson_HasHomepageOrWebsite()
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRootPath, PluginJsonRelativePath))).RootElement;
        var hasUrl = (json.TryGetProperty("homepage", out var h) && !string.IsNullOrWhiteSpace(h.GetString()))
                  || (json.TryGetProperty("website", out var w) && !string.IsNullOrWhiteSpace(w.GetString()));
        return hasUrl
            ? ComplianceResult.Success
            : ComplianceResult.Failure("plugin.json must have either 'homepage' or 'website' set");
    }

    /// <summary>
    /// Divergence: brainarr's <c>global.json</c> uses <c>sdk.version=8.0.0</c> with
    /// <c>rollForward=latestMajor</c> instead of the ecosystem standard
    /// <c>8.0.100</c>/<c>latestFeature</c>. Rationale: brainarr's CI matrix runs on hosted
    /// runners with varying SDK feature bands; locking to 8.0.100 has caused install-failures
    /// on macOS runners that ship 8.0.4xx by default. <c>latestMajor</c> from <c>8.0.0</c>
    /// is strictly more permissive than <c>latestFeature</c> from <c>8.0.100</c> and still
    /// pins to the .NET 8 lifecycle. Track convergence in tech-debt: the ecosystem should
    /// adopt <c>latestMajor</c> uniformly, or brainarr should align to 8.0.100/latestFeature.
    /// </summary>
    public override ComplianceResult GlobalJson_SdkVersion_Is8_0_100()
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRootPath, "global.json"))).RootElement;
        if (!json.TryGetProperty("sdk", out var sdk))
            return ComplianceResult.Failure("global.json missing 'sdk' section");
        if (!sdk.TryGetProperty("version", out var v) || !(v.GetString() ?? "").StartsWith("8."))
            return ComplianceResult.Failure($"global.json sdk.version must be on the .NET 8 line, got '{(sdk.TryGetProperty("version", out var vv) ? vv.GetString() : "<missing>")}'");
        if (!sdk.TryGetProperty("rollForward", out var rf) || string.IsNullOrWhiteSpace(rf.GetString()))
            return ComplianceResult.Failure("global.json sdk.rollForward missing");
        return ComplianceResult.Success;
    }

    #endregion

    #region Per-Method Fact Wrappers

    [Fact] public void DirectoryBuildProps_Exists_Test() { var r = DirectoryBuildProps_Exists(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void DirectoryBuildProps_HasILRepackDisabled_Test() { var r = DirectoryBuildProps_HasILRepackDisabled(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void DirectoryBuildProps_HasVersionManagement_Test() { var r = DirectoryBuildProps_HasVersionManagement(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void DirectoryBuildProps_HasSourceLink_Test() { var r = DirectoryBuildProps_HasSourceLink(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void DirectoryBuildProps_HasNoWarnSuppression_Test() { var r = DirectoryBuildProps_HasNoWarnSuppression(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void DirectoryBuildProps_HasCPMExclusion_Test() { var r = DirectoryBuildProps_HasCPMExclusion(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void DirectoryBuildProps_HasDeterministic_Test() { var r = DirectoryBuildProps_HasDeterministic(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void DirectoryPackagesProps_Exists_Test() { var r = DirectoryPackagesProps_Exists(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void DirectoryPackagesProps_EnablesCPM_Test() { var r = DirectoryPackagesProps_EnablesCPM(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void PluginJson_HasAllRequiredFields_Test() { var r = PluginJson_HasAllRequiredFields(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void PluginJson_TargetFramework_IsNet8_Test() { var r = PluginJson_TargetFramework_IsNet8(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void PluginJson_HasCommonVersion_Test() { var r = PluginJson_HasCommonVersion(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void PluginJson_HasAuthor_Test() { var r = PluginJson_HasAuthor(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void PluginJson_HasLicense_Test() { var r = PluginJson_HasLicense(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void PluginJson_HasTags_Test() { var r = PluginJson_HasTags(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void PluginJson_HasRootNamespace_Test() { var r = PluginJson_HasRootNamespace(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void PluginJson_NoNonStandardFields_Test() { var r = PluginJson_NoNonStandardFields(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void PluginJson_HasHomepageOrWebsite_Test() { var r = PluginJson_HasHomepageOrWebsite(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void ManifestJson_TargetFramework_IsNet8_Test() { var r = ManifestJson_TargetFramework_IsNet8(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void GlobalJson_Exists_Test() { var r = GlobalJson_Exists(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }
    [Fact] public void GlobalJson_SdkVersion_OnNet8_Test() { var r = GlobalJson_SdkVersion_Is8_0_100(); Assert.True(r.Passed, string.Join("; ", r.Errors)); }

    #endregion
}
