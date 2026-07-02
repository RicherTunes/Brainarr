using System;
using System.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

/// <summary>
/// Single source of truth for deciding whether a short "token"-shaped evidence value (finding id,
/// path hash, error type, probe container/codec, ...) carries material that must be redacted.
/// <para>
/// Persistence (<see cref="LibraryHealerFindingStore"/>) and API projection
/// (<see cref="LibraryHealerActionHandler"/>) MUST both call this predicate so the disk copy and the
/// API copy can never diverge. A divergence is a privacy risk: a value redacted on one surface but
/// persisted raw on the other leaks the exact material the healer is supposed to withhold.
/// </para>
/// </summary>
internal static class LibraryHealerTokenRedaction
{
    public static bool ShouldRedactTokenMaterial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ContainsLikelyPath(trimmed)
            || HasDriveDesignator(trimmed)
            || ContainsMediaExtension(trimmed)
            || LibraryHealerSensitiveText.ContainsMetadataMaterial(trimmed)
            || LibraryHealerSensitiveText.ContainsCommandMaterial(trimmed)
            || trimmed.Any(char.IsWhiteSpace);
    }

    private static bool ContainsLikelyPath(string value)
    {
        return HasWindowsRoot(value)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal)
            || HasUnixRoot(value);
    }

    private static bool HasWindowsRoot(string value)
    {
        for (var i = 0; i + 2 < value.Length; i++)
        {
            if (char.IsLetter(value[i])
                && value[i + 1] == ':'
                && (value[i + 2] == '\\' || value[i + 2] == '/'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDriveDesignator(string value)
    {
        for (var i = 0; i + 1 < value.Length; i++)
        {
            if (char.IsLetter(value[i]) && value[i + 1] == ':')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsMediaExtension(string value)
    {
        var mediaExtensions = new[]
        {
            ".aac",
            ".aif",
            ".aiff",
            ".alac",
            ".ape",
            ".flac",
            ".m4a",
            ".mka",
            ".mp3",
            ".mp4",
            ".ogg",
            ".opus",
            ".wav",
            ".wv",
        };

        return mediaExtensions.Any(extension => value.Contains(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasUnixRoot(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '/')
            {
                continue;
            }

            if (i == 0 || char.IsWhiteSpace(value[i - 1]) || value[i - 1] is '"' or '\'' or '(' or '[')
            {
                return true;
            }
        }

        return false;
    }
}
