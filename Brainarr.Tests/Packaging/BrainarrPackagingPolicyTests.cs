using System;
using Lidarr.Plugin.Common.TestKit.Compliance;

namespace Brainarr.Tests.Packaging
{
    /// <summary>
    /// Validates Brainarr's release zip against the cross-plugin policy. Actual assertion
    /// logic lives in <see cref="PluginPackagingContract"/> in Common.TestKit — this class
    /// is just per-plugin glue that supplies the brainarr-specific shape.
    /// </summary>
    public sealed class BrainarrPackagingPolicyTests
    {
        private static readonly PluginPackagePolicy Policy = PluginPackagingContract.MergedDllPolicy(
            mainAssemblyName: "Lidarr.Plugin.Brainarr",
            extraRequired: new[] { "manifest.json" });

        [PackagingFact]
        [Trait("Category", "Packaging")]
        public void Package_Matches_Brainarr_Policy()
        {
            var zipPath = GetPackagePathOrThrow();
            PluginPackagingContract.AssertZipMatchesPolicy(zipPath, Policy);
        }

        private static string GetPackagePathOrThrow()
        {
            var zipPath = PackagingTestPaths.TryFindPackagePath();
            if (zipPath != null) return zipPath;

            if (PackagingTestPaths.IsStrictMode())
            {
                throw new InvalidOperationException(
                    "No plugin package found. Set PLUGIN_PACKAGE_PATH or run `./build.ps1 -Package`.");
            }

            throw new InvalidOperationException("PackagingFact should have skipped when package is missing.");
        }
    }
}
