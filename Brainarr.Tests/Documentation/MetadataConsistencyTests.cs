using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Documentation
{
    public class MetadataConsistencyTests
    {
        [Fact]
        public void MinimumVersion_Is_Consistent_Across_Readme_And_Manifests()
        {
            var root = FindRepositoryRoot();

            var readme = File.ReadAllText(Path.Combine(root, "README.md"));
            var match = Regex.Match(readme, @"Requires Lidarr\s+(\d+\.\d+\.\d+\.\d+)");
            match.Success.Should().BeTrue("README must declare the minimum Lidarr version");
            var readmeVersion = match.Groups[1].Value;

            var manifestVersion = ReadJsonVersion(Path.Combine(root, "manifest.json"));
            var pluginVersion = ReadJsonVersion(Path.Combine(root, "plugin.json"));

            manifestVersion.Should().Be(readmeVersion, "manifest minimumVersion should match README");
            pluginVersion.Should().Be(readmeVersion, "plugin minimumVersion should match README");
        }

        private static string ReadJsonVersion(string path)
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            // manifest.json uses "minimumVersion", plugin.json uses "minHostVersion"
            if (root.TryGetProperty("minimumVersion", out var v1))
                return v1.GetString() ?? string.Empty;
            if (root.TryGetProperty("minHostVersion", out var v2))
                return v2.GetString() ?? string.Empty;

            return string.Empty;
        }

        private static string FindRepositoryRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "Brainarr.sln")))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new InvalidOperationException($"Unable to locate repository root from {AppContext.BaseDirectory}");
        }
    }
}
