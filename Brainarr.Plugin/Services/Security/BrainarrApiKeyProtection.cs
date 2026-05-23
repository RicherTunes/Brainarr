using System;

using Lidarr.Plugin.Common.Extensions;
using Lidarr.Plugin.Common.Interfaces;

using Microsoft.Extensions.DependencyInjection;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security;

/// <summary>
/// Provides string protection for Brainarr LLM provider API keys using Common's
/// <see cref="IStringProtector"/>.
/// </summary>
/// <remarks>
/// Only the modern <c>lpc:ps:v1:</c> protected format is recognised by
/// <see cref="IStringProtector.IsProtected"/>; any other value is treated as plaintext
/// and returned as-is (back-compat with pre-BRN-001 installations).
/// </remarks>
internal static class BrainarrApiKeyProtection
{
    private static readonly Lazy<IServiceProvider> DefaultProvider = new(() =>
    {
        var services = new ServiceCollection();
        services.AddTokenProtection();
        return services.BuildServiceProvider();
    });

    /// <summary>
    /// Gets a default <see cref="IStringProtector"/> instance for use when DI is not available.
    /// Backed by <c>TokenProtectorFactory.CreateFromEnvironment()</c> which selects
    /// DPAPI (Windows), Keychain (macOS), or DataProtection+AES (Linux) automatically.
    /// </summary>
    public static IStringProtector GetDefaultStringProtector()
        => DefaultProvider.Value.GetRequiredService<IStringProtector>();

    /// <summary>
    /// Unprotects a string value if it matches the modern protector format;
    /// otherwise returns the value as-is (assumed plaintext legacy).
    /// </summary>
    public static string? UnprotectString(string? value, IStringProtector stringProtector)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (stringProtector.IsProtected(value))
        {
            return stringProtector.Unprotect(value);
        }

        return value;
    }
}
