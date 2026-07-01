namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

internal static class TagMetadataFields
{
    public const string Title = "title";
    public const string Artist = "artist";
    public const string Album = "album";
    public const string MusicBrainzId = "musicBrainzId";

    public static IReadOnlyList<string> GetMissingFields(TagMetadataEvidence? metadata)
    {
        if (metadata is null)
        {
            return Array.Empty<string>();
        }

        var titleMissing = !metadata.TitlePresent;
        var artistMissing = !metadata.ArtistPresent;
        var albumMissing = !metadata.AlbumPresent;
        var musicBrainzMissing = !metadata.AnyMusicBrainzIdPresent;

        if (!titleMissing && !artistMissing && !albumMissing && !musicBrainzMissing)
        {
            return Array.Empty<string>();
        }

        var missing = new List<string>(4);
        if (titleMissing)
        {
            missing.Add(Title);
        }

        if (artistMissing)
        {
            missing.Add(Artist);
        }

        if (albumMissing)
        {
            missing.Add(Album);
        }

        if (musicBrainzMissing)
        {
            missing.Add(MusicBrainzId);
        }

        return missing;
    }
}
