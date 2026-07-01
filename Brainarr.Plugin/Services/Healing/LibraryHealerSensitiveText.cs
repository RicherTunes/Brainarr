using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

internal static class LibraryHealerSensitiveText
{
    private static readonly Regex MusicBrainzIdPattern = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant);

    private static readonly string[] MetadataKeywords =
    {
        "album",
        "artist",
        "mbid",
        "musicbrainz",
        "recording",
        "release",
        "title",
    };

    public static bool ContainsMetadataMaterial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (MusicBrainzIdPattern.IsMatch(value))
        {
            return true;
        }

        return MetadataKeywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
