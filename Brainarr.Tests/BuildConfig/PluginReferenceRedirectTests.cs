using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.BuildConfig
{
    /// <summary>
    /// Regression guard for the Docker E2E `bin/` pollution class of bug.
    ///
    /// Every project that references Brainarr.Plugin with
    /// <c>PluginPackagingDisable=true</c> (i.e. wants the UN-merged assembly for tests) MUST
    /// also redirect that build to <c>bin-tests\</c> via <c>OutputPath=bin-tests\</c>. Without
    /// the redirect, the un-merged plugin DLL plus its sidecar contract assemblies
    /// (Lidarr.Plugin.Abstractions.dll, CliWrap.dll, Microsoft.Extensions.*.dll) land in
    /// Brainarr.Plugin/bin/ and pollute the merged artifact the Docker E2E harness mounts into
    /// the Lidarr container, causing COR_E_INVALIDOPERATION and an empty plugin schema.
    ///
    /// This is the brainarr-side enforcement of the qobuzarr/tidalarr build-config parity
    /// pattern.
    /// </summary>
    public class PluginReferenceRedirectTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void EveryUnmergedPluginReference_RedirectsOutputToBinTests()
        {
            var repoRoot = GetRepoRoot();
            var offenders = Directory
                .EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .SelectMany(GetUnmergedPluginRefsMissingRedirect)
                .ToList();

            offenders.Should().BeEmpty(
                "every ProjectReference to Brainarr.Plugin with PluginPackagingDisable=true must also " +
                "set OutputPath=bin-tests\\ so the un-merged build never pollutes the merged bin/ the " +
                "Docker E2E harness mounts. Offending references:\n" + string.Join("\n", offenders));
        }

        private static System.Collections.Generic.IEnumerable<string> GetUnmergedPluginRefsMissingRedirect(string csprojPath)
        {
            XDocument doc;
            try { doc = XDocument.Load(csprojPath); }
            catch { yield break; }

            foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                var include = pr.Attribute("Include")?.Value ?? string.Empty;
                if (!include.Replace('\\', '/').EndsWith("Brainarr.Plugin/Brainarr.Plugin.csproj", StringComparison.OrdinalIgnoreCase))
                    continue;

                var addProps = pr.Elements().FirstOrDefault(e => e.Name.LocalName == "AdditionalProperties")?.Value ?? string.Empty;
                if (addProps.IndexOf("PluginPackagingDisable=true", StringComparison.OrdinalIgnoreCase) < 0)
                    continue; // merged reference — does not pollute bin/

                if (addProps.IndexOf("OutputPath=bin-tests", StringComparison.OrdinalIgnoreCase) < 0)
                    yield return $"  {Path.GetFileName(csprojPath)}: ProjectReference '{include}' has PluginPackagingDisable=true but no OutputPath=bin-tests\\";
            }
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
