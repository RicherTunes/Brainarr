using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Brainarr.Tests.ReleaseE2E;

/// <summary>
/// Simulates Lidarr's <c>PluginService.GetRemotePlugin</c> filter against the LIVE
/// GitHub releases of this repo, then verifies the most-recent installable release's
/// zip satisfies the packaging policy (required files present, forbidden DLLs absent).
///
/// This is the regression backstop for the May 2026 install-failure class of bugs:
///   - Asset name didn't contain `net8.0.zip` → Lidarr UI Install spinner hangs forever
///   - Asset shipped sidecar host DLLs → install completed but plugin crashed with
///     "Could not load Lidarr.Plugin.Abstractions / Common" / TypeLoadException
///
/// Why we exercise this against GitHub instead of the local build artifact:
/// the local PackagingPolicyTests already cover the build output. This test catches
/// the case where the *publish* step diverges from the build — e.g. someone hand-uploads
/// a stale zip, or release.yml's `files:` list points at the wrong artifact.
///
/// [Trait("Category", "ReleaseE2E")] so the default test sweep skips it (the GitHub
/// call is rate-limited and slow). Run via:
///   dotnet test --filter "Category=ReleaseE2E"
///
/// Skips gracefully when there's no network / GitHub is unreachable / no releases yet.
/// </summary>
public class PublishedReleaseInstallabilityTests
{
    private const string Owner = "RicherTunes";
    private const string Repo = "Brainarr";
    private const string Framework = "net8.0";

    private static readonly string[] RequiredFiles =
    {
        "Lidarr.Plugin.Brainarr.dll",
        "plugin.json",
        "manifest.json"
    };

    private static readonly string[] ForbiddenAssemblies =
    {
        "FluentValidation.dll",
        "NLog.dll",
        "System.Text.Json.dll",
        "Newtonsoft.Json.dll",
        "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
        "Microsoft.Extensions.Logging.Abstractions.dll",
        "Microsoft.Extensions.Caching.Abstractions.dll",
        "Microsoft.Extensions.Caching.Memory.dll",
        "Microsoft.Extensions.Options.dll",
        "Microsoft.Extensions.Primitives.dll",
        "Lidarr.Core.dll",
        "Lidarr.Common.dll",
        "Lidarr.Http.dll",
        "Lidarr.Api.V1.dll",
        "NzbDrone.Core.dll",
        "NzbDrone.Common.dll",
        // Merged-and-internalized via ILRepack; sidecars regress multi-plugin co-existence.
        "Lidarr.Plugin.Abstractions.dll",
        "Lidarr.Plugin.Common.dll",
    };

    [SkippableFact]
    [Trait("Category", "ReleaseE2E")]
    public async Task LatestPublishedRelease_PassesLidarrInstallFilter()
    {
        using HttpClient http = CreateClient();

        var releases = await TryGetReleasesAsync(http);
        Skip.If(releases is null, "GitHub releases unavailable — network down or rate-limited");

        // Mirror PluginService.GetRemotePlugin filter (plugins branch, May 2026 source):
        //   - !Draft
        //   - target_commitish is main/master (case-insensitive)
        //   - asset name contains "net8.0.zip" (case-insensitive)
        //   - body's "Minimum Lidarr Version" (if present) ≤ host version (we don't enforce here)
        var installable = releases!.Value.EnumerateArray()
            .Where(r => !r.GetProperty("draft").GetBoolean())
            .Where(r => IsDefaultTree(r.GetProperty("target_commitish").GetString()))
            .Where(r => r.GetProperty("assets").EnumerateArray()
                .Any(a => a.GetProperty("name").GetString()?.Contains($"{Framework}.zip", StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        Assert.True(installable.Count > 0,
            $"No release passes Lidarr's PluginService filter. " +
            $"At least one non-draft release on main/master with a `*{Framework}.zip` asset is required. " +
            $"This means the UI Install button on https://github.com/{Owner}/{Repo} would silently fail.");
    }

    [SkippableFact]
    [Trait("Category", "ReleaseE2E")]
    public async Task LatestPublishedRelease_ZipContents_Match_PackagingPolicy()
    {
        using HttpClient http = CreateClient();

        var releases = await TryGetReleasesAsync(http);
        Skip.If(releases is null, "GitHub releases unavailable — network down or rate-limited");

        var topRelease = releases!.Value.EnumerateArray()
            .Where(r => !r.GetProperty("draft").GetBoolean())
            .Where(r => IsDefaultTree(r.GetProperty("target_commitish").GetString()))
            .FirstOrDefault(r => r.GetProperty("assets").EnumerateArray()
                .Any(a => a.GetProperty("name").GetString()?.Contains($"{Framework}.zip", StringComparison.OrdinalIgnoreCase) == true));

        Skip.If(topRelease.ValueKind == JsonValueKind.Undefined,
            "No installable release found (see LatestPublishedRelease_PassesLidarrInstallFilter for diagnosis)");

        var asset = topRelease.GetProperty("assets").EnumerateArray()
            .First(a => a.GetProperty("name").GetString()?.Contains($"{Framework}.zip", StringComparison.OrdinalIgnoreCase) == true);

        var downloadUrl = asset.GetProperty("browser_download_url").GetString()!;
        await using Stream zipStream = await http.GetStreamAsync(downloadUrl);
        using var ms = new MemoryStream();
        await zipStream.CopyToAsync(ms);
        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var fileNames = archive.Entries
            .Select(e => Path.GetFileName(e.FullName))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var assetName = asset.GetProperty("name").GetString();
        foreach (var required in RequiredFiles)
        {
            Assert.True(fileNames.Contains(required),
                $"Published release asset '{assetName}' is missing required file '{required}'. " +
                $"Contents: {string.Join(", ", fileNames)}");
        }

        foreach (var forbidden in ForbiddenAssemblies)
        {
            Assert.False(fileNames.Contains(forbidden),
                $"Published release asset '{assetName}' ships FORBIDDEN '{forbidden}' — " +
                $"this would cause type-identity conflicts or regress multi-plugin co-existence. " +
                $"Contents: {string.Join(", ", fileNames)}");
        }

        // Merged DLL sanity — ILRepack should have produced a ≥2MB DLL with internalized
        // Common + Abstractions. Sub-threshold means the merge didn't run and the plugin
        // will fail at runtime trying to load the (correctly-omitted) sidecars.
        var mainDllEntry = archive.Entries.FirstOrDefault(e =>
            Path.GetFileName(e.FullName).Equals("Lidarr.Plugin.Brainarr.dll", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mainDllEntry);
        Assert.True(mainDllEntry!.Length >= 2_000_000,
            $"Lidarr.Plugin.Brainarr.dll in '{assetName}' is only {mainDllEntry.Length} bytes. " +
            $"Expected ≥2MB (merged DLL with internalized Common + Abstractions). " +
            $"Sub-threshold means ILRepack didn't run — runtime will fail with " +
            $"'Could not load Lidarr.Plugin.Common / Abstractions'.");
    }

    private static bool IsDefaultTree(string? target) =>
        string.Equals(target, "main", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, "master", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{Repo}-tests/1.0");
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static async Task<JsonElement?> TryGetReleasesAsync(HttpClient http)
    {
        try
        {
            using var response = await http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=30");
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            // Clone the root element so it survives the using-scope.
            return doc.RootElement.Clone();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
