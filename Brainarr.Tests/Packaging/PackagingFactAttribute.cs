namespace Brainarr.Tests.Packaging;

/// <summary>
/// Skips the test if no Brainarr package zip can be located and packaging tests
/// are not required (see <see cref="Lidarr.Plugin.Common.TestKit.Packaging.PackagingTestPaths.IsStrictMode"/>).
/// </summary>
public sealed class PackagingFactAttribute()
    : Lidarr.Plugin.Common.TestKit.Packaging.PackagingFactAttribute("Brainarr") { }
