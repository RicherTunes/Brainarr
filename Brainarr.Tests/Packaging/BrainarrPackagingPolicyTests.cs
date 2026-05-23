using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Packaging
{
    public sealed class BrainarrPackagingPolicyTests
    {
        private static readonly string[] RequiredFiles =
        {
            "Lidarr.Plugin.Brainarr.dll",
            "plugin.json",
            "manifest.json"
        };

        // Previously this list also contained Lidarr.Plugin.Abstractions.dll as a sidecar.
        // It's now merged + internalized into Lidarr.Plugin.Brainarr.dll via ILRepack
        // (see ext/Lidarr.Plugin.Common/build/PluginPackaging.targets) so it MUST NOT ship
        // as a sidecar — doing so reintroduces the COR_E_INVALIDOPERATION cross-ALC conflict
        // the merge was meant to eliminate. The forbidden list below enforces that.
        private const long MergedDllMinimumBytes = 2_000_000;

        private static readonly string[] ForbiddenAssemblies =
        {
            // Host-provided contract assemblies — shipping them causes type-identity conflicts.
            "FluentValidation.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "Microsoft.Extensions.Caching.Abstractions.dll",
            "Microsoft.Extensions.Caching.Memory.dll",
            "Microsoft.Extensions.Options.dll",
            "Microsoft.Extensions.Primitives.dll",
            "System.Text.Json.dll",
            "Newtonsoft.Json.dll",
            "NLog.dll",
            // Lidarr host assemblies — host provides them; shipping triggers loader conflicts.
            "Lidarr.Core.dll",
            "Lidarr.Common.dll",
            "Lidarr.Host.dll",
            "Lidarr.Http.dll",
            "Lidarr.Api.V1.dll",
            "NzbDrone.Core.dll",
            "NzbDrone.Common.dll",
            // Plugin abstractions — merged + internalized by ILRepack, MUST NOT ship as sidecars.
            // (Were in RequiredTypeIdentityAssemblies before the May 2026 merge — now they're forbidden.)
            "Lidarr.Plugin.Abstractions.dll",
            "Lidarr.Plugin.Common.dll"
        };

        [PackagingFact]
        [Trait("Category", "Packaging")]
        public void Package_Should_Contain_Required_Files()
        {
            var zipPath = GetPackagePathOrThrow();
            var entries = ReadZipEntries(zipPath);

            entries.Should().Contain(RequiredFiles);
        }

        /// <summary>
        /// The merged plugin DLL must be at least ~2MB. A smaller DLL means ILRepack's
        /// RepackPlugin target didn't run and the Common+Abstractions types weren't
        /// internalized — at runtime Lidarr would then throw
        /// "Could not load file or assembly 'Lidarr.Plugin.Common, Version=...'" because
        /// the sidecar was correctly omitted by the packaging policy but the merge
        /// didn't actually merge anything.
        /// </summary>
        [PackagingFact]
        [Trait("Category", "Packaging")]
        public void Plugin_Dll_Should_Be_Merged_Size()
        {
            var zipPath = GetPackagePathOrThrow();
            using var archive = ZipFile.OpenRead(zipPath);

            var entry = archive.Entries.FirstOrDefault(e =>
                Path.GetFileName(e.FullName).Equals("Lidarr.Plugin.Brainarr.dll", StringComparison.OrdinalIgnoreCase));

            entry.Should().NotBeNull("Lidarr.Plugin.Brainarr.dll must be in the package");
            entry!.Length.Should().BeGreaterOrEqualTo(
                MergedDllMinimumBytes,
                "merged DLL should be ~3MB (includes internalized Common + Abstractions); " +
                "a smaller DLL means ILRepack didn't run and runtime will fail with " +
                "'Could not load file or assembly Lidarr.Plugin.Abstractions/Common'");
        }

        [PackagingFact]
        [Trait("Category", "Packaging")]
        public void Package_Should_Not_Contain_Forbidden_Assemblies()
        {
            var zipPath = GetPackagePathOrThrow();
            var entries = ReadZipEntries(zipPath);

            entries.Should().NotContain(ForbiddenAssemblies);

            var hostAssemblies = entries
                .Where(e => e.StartsWith("Lidarr.", StringComparison.OrdinalIgnoreCase))
                .Where(e => !e.StartsWith("Lidarr.Plugin.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            hostAssemblies.Should().BeEmpty("plugin packages must not ship Lidarr host assemblies");
        }

        /// <summary>
        /// Guard test: FluentValidation.dll must NOT be shipped.
        ///
        /// Shipping FluentValidation.dll causes type-identity mismatch across the plugin boundary:
        /// - Plugin's ValidationResult ≠ Host's ValidationResult (different ALCs)
        /// - Override signature for Test() method doesn't match
        /// - Lidarr error: "Method 'Test' in type '...' does not have an implementation"
        ///
        /// The plugin must use the host's FluentValidation assembly for type identity to match.
        /// </summary>
        [PackagingFact]
        [Trait("Category", "Packaging")]
        public void Package_Must_Not_Ship_FluentValidation()
        {
            var zipPath = GetPackagePathOrThrow();
            var entries = ReadZipEntries(zipPath);

            entries.Should().NotContain(
                "FluentValidation.dll",
                "FluentValidation.dll causes type-identity mismatch: " +
                "override signature for Test(List<ValidationFailure>) doesn't match host's signature " +
                "when ValidationResult resolves from different assembly load contexts");
        }

        private static string GetPackagePathOrThrow()
        {
            var zipPath = PackagingTestPaths.TryFindPackagePath();
            if (zipPath != null)
            {
                return zipPath;
            }

            if (PackagingTestPaths.IsStrictMode())
            {
                throw new InvalidOperationException("No plugin package found. Set PLUGIN_PACKAGE_PATH or run `./build.ps1 -Package`.");
            }

            throw new InvalidOperationException("PackagingFact should have skipped when package is missing.");
        }

        private static HashSet<string> ReadZipEntries(string zipPath)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.Entries
                .Select(e => e.FullName.Replace('\\', '/').Trim('/'))
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
