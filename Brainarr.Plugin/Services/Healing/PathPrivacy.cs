using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public static class PathPrivacy
{
    private const string MissingHash = "000000000000";

    public static string Redact(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "<missing>#" + MissingHash;
        }

        var trimmed = path.Trim();
        var separators = new[] { '/', '\\' };
        var withoutTrailingSeparators = trimmed.TrimEnd(separators);
        var lastSeparator = withoutTrailingSeparators.LastIndexOfAny(separators);
        var basename = lastSeparator >= 0
            ? withoutTrailingSeparators.Substring(lastSeparator + 1)
            : withoutTrailingSeparators;
        if (string.IsNullOrWhiteSpace(basename))
        {
            basename = "<missing>";
        }

        return basename + "#" + HashPath(path);
    }

    public static string RedactDisplayPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Redact(path);
        }

        var trimmed = path.Trim();
        if (TrySplitDisplayHash(trimmed, out var displayName, out var hash))
        {
            return SanitizeDisplayName(ExtractPathSegment(displayName)) + "#" + hash;
        }

        return SanitizeDisplayName(ExtractPathSegment(trimmed)) + "#" + HashPath(path);
    }

    public static string HashPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return MissingHash;
        }

        var normalized = path.Trim().Replace('\\', '/');
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 12);
    }

    public static string? RedactMessage(string? message, string? knownPath = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var redacted = message;
        if (!string.IsNullOrWhiteSpace(knownPath))
        {
            redacted = redacted.Replace(knownPath, Redact(knownPath), StringComparison.OrdinalIgnoreCase);
        }

        redacted = Regex.Replace(redacted, "[A-Za-z]:[\\\\/][^\\r\\n\\t\\\"<>|]+", "<path>");
        redacted = Regex.Replace(redacted, "/[^\\r\\n\\t\\\"<>|]+", "<path>");
        return redacted;
    }

    private static string ExtractPathSegment(string value)
    {
        var separators = new[] { '/', '\\' };
        var withoutTrailingSeparators = value.TrimEnd(separators);
        var lastSeparator = withoutTrailingSeparators.LastIndexOfAny(separators);
        return lastSeparator >= 0
            ? withoutTrailingSeparators.Substring(lastSeparator + 1)
            : withoutTrailingSeparators;
    }

    private static string SanitizeDisplayName(string value)
    {
        var displayName = value.Trim();
        var marker = " - ";
        var lastMarker = displayName.LastIndexOf(marker, StringComparison.Ordinal);
        if (lastMarker >= 0 && lastMarker + marker.Length < displayName.Length)
        {
            displayName = displayName.Substring(lastMarker + marker.Length).Trim();
        }

        return string.IsNullOrWhiteSpace(displayName) ? "<missing>" : displayName;
    }

    private static bool TrySplitDisplayHash(string value, out string displayName, out string hash)
    {
        displayName = string.Empty;
        hash = string.Empty;

        var separator = value.LastIndexOf('#');
        if (separator <= 0 || separator + 13 != value.Length)
        {
            return false;
        }

        var candidateHash = value.Substring(separator + 1);
        for (var i = 0; i < candidateHash.Length; i++)
        {
            var ch = candidateHash[i];
            if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f')))
            {
                return false;
            }
        }

        displayName = value.Substring(0, separator);
        hash = candidateHash;
        return true;
    }
}
