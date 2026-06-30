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
}
