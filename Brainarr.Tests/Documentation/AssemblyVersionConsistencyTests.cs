using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
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

            var informationalVersion = GetPluginInformationalVersion();

            informationalVersion.Should().StartWith(
                pluginVersion,
                "InformationalVersion should start with plugin.json version to prevent drift and simplify diagnostics");
        }

        private static string GetPluginInformationalVersion()
        {
            var assembly = typeof(NzbDrone.Core.ImportLists.Brainarr.Brainarr).Assembly;
            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            attribute.Should().NotBeNull("plugin assembly should define AssemblyInformationalVersion via MSBuild");
            attribute!.InformationalVersion.Should().NotBeNullOrWhiteSpace();
            return attribute.InformationalVersion;
        }

        private static string ReadPluginJsonVersion(string path)
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.GetProperty("version").GetString() ?? string.Empty;
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

