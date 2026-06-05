using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Brainarr.Parity.Tests;

/// <summary>
/// LOOP-001 drift gate: the pinned Common version is recorded in <c>ext-common-sha.txt</c> (the greppable,
/// reviewable sentinel), and the version actually compiled into the plugin is whatever the
/// <c>ext/Lidarr.Plugin.Common</c> submodule has checked out. Those MUST agree. When they diverge (the apple
/// failure mode: sentinel says one SHA, the submodule is another), reviews and the build disagree about which
/// Common is in the plugin.
///
/// We compare the sentinel against the submodule's actual checked-out HEAD (<c>git -C ext/... rev-parse HEAD</c>)
/// rather than the committed gitlink: that holds in a dirty local re-pin (sentinel + submodule updated together,
/// pre-commit) AND in CI — a clean checkout materializes the submodule at the committed gitlink, so a commit
/// that bumped the sentinel but forgot to stage the submodule still surfaces here.
/// </summary>
[Trait("Category", "Parity")]
public class CommonPinDriftTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void ExtCommonShaSentinel_IsAFortyHexSha()
    {
        var shaFile = Path.Combine(RepoRoot, "ext-common-sha.txt");
        Assert.True(File.Exists(shaFile), $"ext-common-sha.txt sentinel missing at {shaFile}");

        var declared = File.ReadAllText(shaFile).Trim();
        Assert.Matches("^[0-9a-f]{40}$", declared);
    }

    [Fact]
    public void ExtCommonSha_MatchesCheckedOutSubmodule()
    {
        var declared = File.ReadAllText(Path.Combine(RepoRoot, "ext-common-sha.txt")).Trim();

        var submoduleHead = TryRevParseHead(Path.Combine(RepoRoot, "ext", "Lidarr.Plugin.Common"));
        if (submoduleHead is null)
        {
            // git unavailable or submodule not initialized (e.g. an exported source snapshot) — inconclusive.
            return;
        }

        Assert.True(
            string.Equals(declared, submoduleHead, StringComparison.OrdinalIgnoreCase),
            $"Common pin drift: ext-common-sha.txt = {declared} but the checked-out submodule = {submoduleHead}. " +
            "Re-pin both together (update the submodule AND the sentinel) so reviews and the build agree.");
    }

    /// <summary>Returns the checked-out HEAD SHA of the git repo at <paramref name="repoDir"/>, or null when git
    /// is unavailable / the directory is not a git checkout.</summary>
    private static string? TryRevParseHead(string repoDir)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"-C \"{repoDir}\" rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return null;
            }

            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(5000) || p.ExitCode != 0)
            {
                return null;
            }

            var m = Regex.Match(output, "([0-9a-f]{40})");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}
