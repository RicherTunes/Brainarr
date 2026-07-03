using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

/// <summary>
/// Extracts the shared storage root (drive, UNC share, or mount point) from a track file's real
/// path so that per-file path failures under one offline mount can be grouped and coalesced.
/// Operates purely on the string; it performs no filesystem I/O.
/// </summary>
internal static class HealerStorageRoot
{
    private static readonly string[] MountContainers =
    {
        "mnt",
        "media",
        "volumes",
        "srv",
        "run",
    };

    /// <summary>
    /// Returns the storage root for the given path, or <c>null</c> when no meaningful shared root can
    /// be determined (empty/relative path). The returned value is the real (unredacted) root and must
    /// be redacted before it is persisted or projected.
    /// </summary>
    public static string? Extract(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim();

        // UNC share: \\server\share\...  -> \\server\share
        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            var normalized = trimmed.Replace('/', '\\');
            var afterPrefix = normalized.Substring(2);
            var firstSep = afterPrefix.IndexOf('\\');
            if (firstSep < 0)
            {
                return null;
            }

            var secondSep = afterPrefix.IndexOf('\\', firstSep + 1);
            var share = secondSep < 0 ? afterPrefix : afterPrefix.Substring(0, secondSep);
            return "\\\\" + share;
        }

        // Windows drive: X:\...  -> X:\
        if (trimmed.Length >= 3
            && char.IsLetter(trimmed[0])
            && trimmed[1] == ':'
            && (trimmed[2] == '\\' || trimmed[2] == '/'))
        {
            return trimmed.Substring(0, 2) + "\\";
        }

        // Unix absolute: /mount/name/...  -> /mount/name  (or /name for non-mount-container roots)
        if (trimmed[0] == '/')
        {
            var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            var first = segments[0];
            if (segments.Length >= 2 && Array.Exists(MountContainers, c => string.Equals(c, first, StringComparison.OrdinalIgnoreCase)))
            {
                return "/" + first + "/" + segments[1];
            }

            return "/" + first;
        }

        // Relative or otherwise unrooted paths have no shared storage root worth coalescing on.
        return null;
    }

    /// <summary>
    /// Grouping key for a real storage root. Windows drive and UNC roots are case-insensitive;
    /// POSIX roots are case-sensitive, so /mnt/Music and /mnt/music must not be coalesced.
    /// </summary>
    public static string Key(string root)
    {
        var trimmed = root.Trim();
        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return trimmed.Replace('/', '\\').ToLowerInvariant();
        }

        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return trimmed.Replace('/', '\\').ToLowerInvariant();
        }

        return trimmed.Replace('\\', '/');
    }
}
