using System;
using System.Linq;
using System.Text;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Sections;

/// <summary>
/// Renders the "COLLECTION OVERVIEW" section with size, genres, style, and depth.
/// </summary>
internal sealed class CollectionContextSection : IPromptSection
{
    public int Order => 10;

    public bool CanBuild(PromptPlan plan, LibraryProfile profile) => true;

    public string Build(PromptPlan plan, LibraryProfile profile, bool minimalFormatting)
    {
        var sb = new StringBuilder();

        sb.AppendLine(minimalFormatting ? "COLLECTION OVERVIEW:" : "\U0001f4ca COLLECTION OVERVIEW:");

        var collectionSize = ProfileMetadataHelper.GetString(profile, "CollectionSize", "established");
        var collectionFocus = ProfileMetadataHelper.GetString(profile, "CollectionFocus", "general");

        sb.AppendLine($"\u2022 Size: {collectionSize} ({profile.TotalArtists} artists, {profile.TotalAlbums} albums)");

        var topGenres = ProfileMetadataHelper.GetTopN<double>(
            profile, "GenreDistribution", 5,
            kv => $"{kv.Key} ({kv.Value:F1}%)",
            kv => !kv.Key.EndsWith("_significance", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(topGenres))
        {
            sb.AppendLine($"\u2022 Genres: {topGenres}");
        }
        else
        {
            sb.AppendLine($"\u2022 Genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => g.Key))}");
        }

        var collectionStyle = ProfileMetadataHelper.GetString(profile, "CollectionStyle", "");
        if (!string.IsNullOrEmpty(collectionStyle))
        {
            var completion = ProfileMetadataHelper.GetDouble(profile, "CompletionistScore");
            if (completion.HasValue)
            {
                sb.AppendLine($"\u2022 Collection style: {collectionStyle} (completionist score: {completion.Value:F1}%)");
            }
            else
            {
                sb.AppendLine($"\u2022 Collection style: {collectionStyle}");
            }
        }
        else
        {
            sb.AppendLine($"\u2022 Collection style: {collectionFocus}");
        }

        var avg = ProfileMetadataHelper.GetDouble(profile, "AverageAlbumsPerArtist");
        if (avg.HasValue)
        {
            sb.AppendLine($"\u2022 Collection depth: avg {avg.Value:F1} albums per artist");
        }

        return sb.ToString().TrimEnd();
    }
}
