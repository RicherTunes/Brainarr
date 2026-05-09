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
}
