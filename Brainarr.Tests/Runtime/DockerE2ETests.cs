using System.Text.Json;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.TestKit.Hosting;
using Xunit;

namespace Brainarr.Tests.Runtime;

/// <summary>
/// End-to-end smoke tests that exercise the brainarr plugin inside a real
/// Lidarr container. Boots once via <see cref="BrainarrLidarrContainerFixture"/>
/// (which subclasses common's lifted
/// <see cref="Lidarr.Plugin.Common.TestKit.Hosting.LidarrContainerFixture"/>),
/// then runs ImportList smoke assertions to verify the plugin is actually
/// wired into the host.
///
/// Brainarr is an ImportList-only plugin (HasIndexer=false,
/// HasDownloadClient=false in BrainarrModule) — so the standard 4-test
/// indexer/downloadclient matrix collapses to 2 ImportList assertions:
///
///  1. ImportList schema lists a Brainarr entry
///  2. POST /api/v1/importlist/test with empty settings returns a sensible 4xx
///     (validation failure), not a 500 (plugin-internal error)
///
/// All tests are gated on <c>[Trait("Category","DockerE2E")]</c> and skip
/// gracefully when Docker isn't running or the plugin DLL isn't built.
///
/// Wave 22b — the orchestration + assertion logic lives in common's TestKit
/// (LidarrContainerFixture + LidarrContainerFixtureSmokeAssertions).
/// This file is just per-plugin glue.
/// </summary>
[Collection(LidarrContainerCollection.Name)]
public sealed class DockerE2ETests
{
    private readonly BrainarrLidarrContainerFixture _fixture;

    public DockerE2ETests(BrainarrLidarrContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    [Trait("Category", "DockerE2E")]
    public async Task Plugin_Loads_AppearsInImportListSchema()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);
        await _fixture.AssertPluginAppearsInImportListSchemaAsync();
    }

    [SkippableFact]
    [Trait("Category", "DockerE2E")]
    public async Task ImportList_Test_WithEmptySettings_ReturnsSensibleFailure()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);
        await _fixture.AssertImportListTestReturnsSensibleFailureAsync();
    }

    /// <summary>
    /// Asserts Brainarr appears in <c>GET /api/v1/system/plugins</c> — Lidarr's
    /// "System → Plugins" UI binds to <see cref="NzbDrone.Core.Plugins.IPlugin"/>
    /// implementations, not to ImportList/Indexer/DownloadClient schema entries.
    /// Without a concrete <c>BrainarrInstalledPlugin : NzbDrone.Core.Plugins.Plugin</c>
    /// class, the plugin is loaded and usable but invisible to the UI's plugin
    /// manager (and to the auto-update / uninstall flow that scans this list).
    /// Regression test for the May 2026 fix — see memory note
    /// `reference-lidarr-plugin-registration` for the full contract.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "DockerE2E")]
    public async Task Plugin_AppearsInInstalledPluginsApi()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);

        string url = $"{_fixture.BaseUrl}/api/v1/system/plugins?apikey={_fixture.ApiKey}";
        string json = await _fixture.Http.GetStringAsync(url).ConfigureAwait(false);

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        bool found = false;
        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("name", out JsonElement nameEl) &&
                nameEl.GetString()?.Contains("Brainarr", System.StringComparison.OrdinalIgnoreCase) == true)
            {
                found = true;
                break;
            }
        }

        Assert.True(found,
            $"Expected /api/v1/system/plugins to include a Brainarr entry. " +
            $"Response: {json}\n\n" +
            $"Container logs:\n{_fixture.GetContainerLogs()}");
    }
}
