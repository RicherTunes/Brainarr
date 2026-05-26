namespace Brainarr.Tests.Packaging;

/// <summary>
/// Plugin-specific packaging path helpers — thin wrapper over the shared
/// <see cref="Lidarr.Plugin.Common.TestKit.Packaging.PackagingTestPaths"/> factory.
/// </summary>
public static class PackagingTestPaths
{
    private static readonly Lidarr.Plugin.Common.TestKit.Packaging.PackagingTestPaths _paths =
        Lidarr.Plugin.Common.TestKit.Packaging.PackagingTestPaths.For("Brainarr");

    public static bool IsStrictMode() =>
        Lidarr.Plugin.Common.TestKit.Packaging.PackagingTestPaths.IsStrictMode();

    public static string? TryFindPackagePath() => _paths.TryFindPackagePath();

    public static string RequirePackagePath() => _paths.RequirePackagePath();

    public static string? TryFindRepoRoot() => _paths.TryFindRepoRoot();

    public static string FindRepoRootOrThrow() => _paths.FindRepoRootOrThrow();
}
