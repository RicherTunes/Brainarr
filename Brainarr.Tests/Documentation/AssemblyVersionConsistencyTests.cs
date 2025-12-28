using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Documentation
{
    public sealed class AssemblyVersionConsistencyTests
    {
        [Fact]
        public void InformationalVersion_Should_Start_With_PluginJson_Version()
        {
            var root = FindRepositoryRoot();
            var pluginVersion = ReadPluginJsonVersion(Path.Combine(root, "plugin.json"));
            var pluginVersionPrefix = GetVersionPrefix(pluginVersion);

            var informationalVersion = GetPluginInformationalVersion();
            var fileVersion = GetPluginFileVersion();

            informationalVersion.Should().StartWith(
                pluginVersion,
                "InformationalVersion should start with plugin.json version to prevent drift and simplify diagnostics");

            fileVersion.Should().Be(
                $"{pluginVersionPrefix}.0",
                "AssemblyFileVersion should be MAJOR.MINOR.PATCH.0 derived from plugin.json (stable and Windows-friendly)");
        }

        private static string GetPluginInformationalVersion()
        {
            var assembly = typeof(NzbDrone.Core.ImportLists.Brainarr.Brainarr).Assembly;
            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            attribute.Should().NotBeNull("plugin assembly should define AssemblyInformationalVersion via MSBuild");
            attribute!.InformationalVersion.Should().NotBeNullOrWhiteSpace();
            return attribute.InformationalVersion;
        }

        private static string GetPluginFileVersion()
        {
            var assembly = typeof(NzbDrone.Core.ImportLists.Brainarr.Brainarr).Assembly;
            var attribute = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            attribute.Should().NotBeNull("plugin assembly should define AssemblyFileVersion via MSBuild");
            attribute!.Version.Should().NotBeNullOrWhiteSpace();
            return attribute.Version;
        }

        private static string ReadPluginJsonVersion(string path)
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.GetProperty("version").GetString() ?? string.Empty;
        }

        private static string GetVersionPrefix(string version)
        {
            var match = Regex.Match(version ?? string.Empty, @"^\d+\.\d+\.\d+");
            match.Success.Should().BeTrue("plugin.json version should start with MAJOR.MINOR.PATCH");
            return match.Value;
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
