using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.BuildConfig
{
    /// <summary>
    /// Build-config guard that the Common Roslyn analyzer (LPC0003) is wired into the Brainarr
    /// plugin build, scoped to the search-term HtmlEncode ban only.
    ///
    /// Brainarr is the AI recommender and has no HtmlEncode-on-search-term sites today, so LPC0003
    /// flags nothing — but the analyzer must be WIRED + ACTIVE (not inert) so a future regression
    /// that HTML-encodes a query (the shipped "Beyoncé" 0-result bug class) is caught at build time.
    /// This mirrors tidalarr/qobuzarr/applemusicarr's scoped adoption:
    ///   * RunAnalyzersDuringBuild=true overrides the repo default (Directory.Build.props sets false),
    ///     so the referenced analyzer actually executes during the build.
    ///   * The analyzer is consumed via a ProjectReference with OutputItemType=Analyzer and
    ///     ReferenceOutputAssembly=false, so it runs as a Roslyn analyzer and never ships in the
    ///     merged plugin DLL.
    ///   * LPC0001 / LPC0002 (raw-HttpClient / resilience guidance — pre-existing tech debt on the
    ///     raw-HttpClient AI provider paths) are added to NoWarn so only LPC0003 stays active and the
    ///     full StyleCop/.NET analyzer suite stays off.
    /// </summary>
    public class AnalyzerWiringTests
    {
        private const string AnalyzerProjectFileName = "Lidarr.Plugin.Analyzers.csproj";

        [Fact]
        [Trait("Category", "Unit")]
        public void PluginCsproj_WiresLpc0003Analyzer_AsScopedRoslynAnalyzer()
        {
            var doc = LoadPluginCsproj();

            var analyzerRef = doc.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .FirstOrDefault(pr =>
                    (pr.Attribute("Include")?.Value ?? string.Empty)
                        .Replace('\\', '/')
                        .EndsWith(AnalyzerProjectFileName, StringComparison.OrdinalIgnoreCase));

            analyzerRef.Should().NotBeNull(
                "Brainarr.Plugin.csproj must reference the Common LPC analyzer project so LPC0003 " +
                "(search-term HtmlEncode ban) runs during the plugin build");

            analyzerRef!.Attribute("OutputItemType")?.Value
                .Should().Be("Analyzer",
                    "the analyzer must be consumed as a Roslyn analyzer (OutputItemType=Analyzer), " +
                    "not a runtime reference");

            (analyzerRef.Attribute("ReferenceOutputAssembly")?.Value ?? string.Empty)
                .Should().BeEquivalentTo("false",
                    "ReferenceOutputAssembly=false keeps the analyzer DLL out of the compile-time " +
                    "reference set and the merged plugin DLL");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void PluginCsproj_EnablesRunAnalyzersDuringBuild()
        {
            var doc = LoadPluginCsproj();

            var enabled = doc.Descendants()
                .Where(e => e.Name.LocalName == "RunAnalyzersDuringBuild")
                .Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

            enabled.Should().BeTrue(
                "the repo default (Directory.Build.props) is RunAnalyzersDuringBuild=false, so the " +
                "plugin csproj must override it to true or the referenced LPC0003 analyzer is inert");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void PluginCsproj_ScopesAnalyzerToLpc0003_BySuppressingLpc0001AndLpc0002()
        {
            var doc = LoadPluginCsproj();

            var noWarn = string.Join(
                ";",
                doc.Descendants().Where(e => e.Name.LocalName == "NoWarn").Select(e => e.Value));

            noWarn.Should().Contain("LPC0001",
                "LPC0001 (raw HttpClient) is pre-existing tech debt on the raw-HttpClient AI provider " +
                "paths and must be suppressed so only LPC0003 stays active");
            noWarn.Should().Contain("LPC0002",
                "LPC0002 (resilience overload) must be suppressed so only LPC0003 stays active");
            noWarn.Should().NotContain("LPC0003",
                "LPC0003 must remain ACTIVE — it is the whole point of wiring the analyzer");
        }

        private static XDocument LoadPluginCsproj()
        {
            var path = Path.Combine(GetRepoRoot(), "Brainarr.Plugin", "Brainarr.Plugin.csproj");
            File.Exists(path).Should().BeTrue($"plugin csproj should exist at {path}");
            return XDocument.Load(path);
        }

        private static string GetRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8; i++)
            {
                if (File.Exists(Path.Combine(dir, "Brainarr.sln"))) return dir;
                var parent = Directory.GetParent(dir)?.FullName;
                if (parent == null) break;
                dir = parent;
            }
            throw new DirectoryNotFoundException("Could not locate repo root containing Brainarr.sln");
        }
    }
}
