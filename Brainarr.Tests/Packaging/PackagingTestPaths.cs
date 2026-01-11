using System;
using System.IO;
using System.Linq;

namespace Brainarr.Tests.Packaging
{
    public static class PackagingTestPaths
    {
        public static bool IsStrictMode()
        {
            return IsTruthy(Environment.GetEnvironmentVariable("REQUIRE_PACKAGE_TESTS"));
        }

        public static string? TryFindPackagePath()
        {
            var explicitPath =
                Environment.GetEnvironmentVariable("PLUGIN_PACKAGE_PATH") ??
                Environment.GetEnvironmentVariable("BRAINARR_PACKAGE_PATH");

            if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            var repoRoot = FindRepoRoot();
            if (repoRoot == null)
            {
                return null;
            }

            // Search in repo root and artifacts/packages/ (build.ps1 output location)
            var searchPaths = new[] { repoRoot, Path.Combine(repoRoot, "artifacts", "packages") };

            var candidates = searchPaths
                .Where(Directory.Exists)
                .SelectMany(dir => Directory.GetFiles(dir, "Brainarr-*.zip")
                    .Concat(Directory.GetFiles(dir, "Brainarr-*.net8.0.zip"))
                    .Concat(Directory.GetFiles(dir, "Brainarr-latest.zip")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            return candidates.FirstOrDefault()?.FullName;
        }

        public static string FindRepoRootOrThrow()
        {
            return FindRepoRoot() ?? throw new InvalidOperationException("Failed to locate Brainarr repo root (expected Brainarr.sln).");
        }

        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Brainarr.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }

        private static bool IsTruthy(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }
}

