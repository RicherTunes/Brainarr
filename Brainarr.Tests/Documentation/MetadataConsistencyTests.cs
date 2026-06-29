using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
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

        [Fact]
        public void ProviderDefaultTables_Use_Current_Code_Defaults()
        {
            var root = FindRepositoryRoot();
            var readme = File.ReadAllText(Path.Combine(root, "README.md"));
            var providerGuide = File.ReadAllText(Path.Combine(root, "docs", "PROVIDER_GUIDE.md"));

            AssertDocumentedDefault(readme, "OpenAI", BrainarrConstants.DefaultOpenAIModel);
            AssertDocumentedDefault(readme, "Anthropic", BrainarrConstants.DefaultAnthropicModel);
            AssertDocumentedDefault(readme, "Gemini", BrainarrConstants.DefaultGeminiModel);
            AssertDocumentedDefault(readme, "DeepSeek", BrainarrConstants.DefaultDeepSeekModel);

            AssertDocumentedDefault(providerGuide, "OpenAI", BrainarrConstants.DefaultOpenAIModel);
            AssertDocumentedDefault(providerGuide, "Anthropic", BrainarrConstants.DefaultAnthropicModel);
            AssertDocumentedDefault(providerGuide, "Google Gemini", BrainarrConstants.DefaultGeminiModel);
            AssertDocumentedDefault(providerGuide, "DeepSeek", BrainarrConstants.DefaultDeepSeekModel);
        }

        private static void AssertDocumentedDefault(string markdown, string provider, string expectedDefault)
        {
            markdown.Should().Contain($"| **{provider}** |", $"the {provider} row must exist");

            var rowPattern = $@"\|\s*\*\*{Regex.Escape(provider)}\*\*\s*\|(?<cells>.*)\|";
            var match = Regex.Match(markdown, rowPattern);
            match.Success.Should().BeTrue($"the {provider} row must be parseable");
            match.Value.Should().Contain($"`{expectedDefault}`",
                $"{provider} docs should track BrainarrConstants rather than stale release-era defaults");
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
