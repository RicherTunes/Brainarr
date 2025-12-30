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

        private static readonly string[] RequiredTypeIdentityAssemblies =
        {
            "Lidarr.Plugin.Abstractions.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll"
        };

        private static readonly string[] ForbiddenAssemblies =
        {
            "FluentValidation.dll",
            "System.Text.Json.dll",
            "NLog.dll",
            "Lidarr.Core.dll",
            "Lidarr.Common.dll",
            "Lidarr.Host.dll",
            "Lidarr.Http.dll",
            "NzbDrone.Core.dll",
            "NzbDrone.Common.dll"
        };

        [PackagingFact]
        [Trait("Category", "Packaging")]
        public void Package_Should_Contain_Required_Files()
        {
            var zipPath = GetPackagePathOrThrow();
            var entries = ReadZipEntries(zipPath);

            entries.Should().Contain(RequiredFiles);
            entries.Should().Contain(RequiredTypeIdentityAssemblies);
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
