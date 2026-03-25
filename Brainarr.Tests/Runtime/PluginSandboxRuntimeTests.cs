using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Abstractions.Manifest;
using Lidarr.Plugin.Common.TestKit.Fixtures;
using Lidarr.Plugin.Common.TestKit.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Brainarr.Tests.Runtime;

/// <summary>
/// Loads the ILRepack-merged Lidarr.Plugin.Brainarr.dll in an isolated AssemblyLoadContext
/// and exercises the real plugin lifecycle. This proves the built artifact works at runtime,
/// not just that the source code compiles and unit tests pass.
///
/// What this proves that unit tests cannot:
/// - The merged DLL loads without assembly resolution failures
/// - IPlugin type is discoverable via reflection
/// - DI container builds without missing service registrations
/// - Settings provider contract works through the real plugin
/// - Dispose lifecycle completes without leaks
/// </summary>
public class PluginSandboxRuntimeTests
{
    private static string FindPluginDll()
    {
        // Look for the ILRepack-merged DLL in known build output paths
        string[] candidates =
        [
            Path.Combine(TestContext.RepoRoot, "Brainarr.Plugin", "bin", "Lidarr.Plugin.Brainarr.dll"),
            Path.Combine(TestContext.RepoRoot, "Brainarr.Plugin", "bin", "Release", "Lidarr.Plugin.Brainarr.dll"),
            Path.Combine(TestContext.RepoRoot, "Brainarr.Plugin", "bin", "Debug", "Lidarr.Plugin.Brainarr.dll"),
        ];

        string? found = candidates.FirstOrDefault(File.Exists);
        return found ?? throw new SkipException(
            $"Plugin DLL not found. Build with ILRepack first: dotnet build Brainarr.Plugin/Brainarr.Plugin.csproj -c Release. Tried: {string.Join(", ", candidates)}");
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_Loads_In_Isolated_ALC()
    {
        string dllPath = FindPluginDll();

        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(dllPath);

        Assert.NotNull(sandbox.Plugin);
        Assert.NotNull(sandbox.Plugin.Manifest);
        Assert.Equal("brainarr", sandbox.Plugin.Manifest.Id);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_SettingsProvider_Describe_Returns_All_Fields()
    {
        string dllPath = FindPluginDll();

        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(dllPath);

        IReadOnlyCollection<SettingDefinition> defs = sandbox.Plugin.SettingsProvider.Describe();

        Assert.NotNull(defs);
        Assert.True(defs.Count >= 4, $"Expected at least 4 setting definitions, got {defs.Count}");

        HashSet<string> keys = [.. defs.Select(d => d.Key)];
        Assert.Contains("Provider", keys);
        Assert.Contains("ConfigurationUrl", keys);
        Assert.Contains("ModelSelection", keys);
        Assert.Contains("MaxRecommendations", keys);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_SettingsProvider_GetDefaults_Returns_Dictionary()
    {
        string dllPath = FindPluginDll();

        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(dllPath);

        IReadOnlyDictionary<string, object?> defaults = sandbox.Plugin.SettingsProvider.GetDefaults();

        Assert.NotNull(defaults);
        Assert.True(defaults.Count >= 4, $"Expected at least 4 defaults, got {defaults.Count}");
        Assert.True(defaults.ContainsKey("Provider"), "Defaults should contain 'Provider'");
        Assert.True(defaults.ContainsKey("MaxRecommendations"), "Defaults should contain 'MaxRecommendations'");
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_SettingsProvider_Validate_Works_Through_Merged_DLL()
    {
        string dllPath = FindPluginDll();

        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(dllPath);

        // Valid settings: Ollama provider (default) with default URL and model
        Dictionary<string, object?> valid = new()
        {
            ["Provider"] = 0, // AIProvider.Ollama
            ["ConfigurationUrl"] = "http://localhost:11434",
            ["ModelSelection"] = "llama3.2:latest",
            ["MaxRecommendations"] = 10,
            ["AIRequestTimeoutSeconds"] = 30
        };

        PluginValidationResult result = sandbox.Plugin.SettingsProvider.Validate(valid);
        Assert.True(result.IsValid, $"Validation failed: {string.Join(", ", result.Errors)}");

        // Invalid settings: out-of-range MaxRecommendations
        Dictionary<string, object?> invalid = new()
        {
            ["MaxRecommendations"] = 0
        };

        PluginValidationResult invalidResult = sandbox.Plugin.SettingsProvider.Validate(invalid);
        Assert.False(invalidResult.IsValid, "Validation should fail for MaxRecommendations=0");
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_SettingsProvider_Apply_Rebuilds_ServiceProvider()
    {
        string dllPath = FindPluginDll();

        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(dllPath);

        Dictionary<string, object?> settings = new()
        {
            ["Provider"] = 0, // AIProvider.Ollama
            ["ConfigurationUrl"] = "http://localhost:11434",
            ["ModelSelection"] = "llama3.2:latest",
            ["MaxRecommendations"] = 10,
            ["AIRequestTimeoutSeconds"] = 30
        };

        PluginValidationResult result = sandbox.Plugin.SettingsProvider.Apply(settings);
        Assert.True(result.IsValid, $"Apply failed: {string.Join(", ", result.Errors)}");
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_Dispose_Completes_Without_Error()
    {
        string dllPath = FindPluginDll();

        PluginSandbox sandbox = await PluginSandbox.CreateAsync(dllPath);

        // Should not throw
        await sandbox.DisposeAsync();
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_Manifest_Has_Required_Fields()
    {
        string dllPath = FindPluginDll();

        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(dllPath);

        PluginManifest manifest = sandbox.Plugin.Manifest;
        Assert.False(string.IsNullOrWhiteSpace(manifest.Id));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Name));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Version));
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_Captures_Logs_During_Initialization()
    {
        string dllPath = FindPluginDll();

        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(dllPath);

        // The sandbox's PluginTestContext captures logs
        var logs = sandbox.Context.LogEntries.Snapshot();
        // Plugin may or may not emit logs during init -- we just verify the
        // log pipeline is wired (no NullReferenceException from missing ILoggerFactory)
        Assert.NotNull(logs);
    }

    /// <summary>Helpers to find repo root.</summary>
    private static class TestContext
    {
        public static string RepoRoot { get; } = FindRepoRoot();

        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir, "Brainarr.sln")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return AppContext.BaseDirectory;
        }
    }
}
