using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Sections;

/// <summary>
/// Renders the "MUSICAL DNA" section with era preferences, album types, and recent activity.
/// </summary>
internal sealed class MusicalDnaSection : IPromptSection
{
    public int Order => 20;

    public bool CanBuild(PromptPlan plan, LibraryProfile profile) => true;

    public string Build(PromptPlan plan, LibraryProfile profile, bool minimalFormatting)
    {
        var sb = new StringBuilder();

        sb.AppendLine(minimalFormatting ? "MUSICAL DNA:" : "\U0001f3b5 MUSICAL DNA:");

        var eras = ProfileMetadataHelper.GetTyped<List<string>>(profile, "PreferredEras");
        if (eras != null && eras.Any())
        {
            sb.AppendLine($"\u2022 Era preference: {string.Join(", ", eras)}");
        }

        var topTypes = ProfileMetadataHelper.GetTopN<int>(
            profile, "AlbumTypes", 3,
            kv => $"{kv.Key} ({kv.Value})");
        if (!string.IsNullOrEmpty(topTypes))
        {
            sb.AppendLine($"\u2022 Album types: {topTypes}");
        }

        var ratio = ProfileMetadataHelper.GetDouble(profile, "NewReleaseRatio");
        if (ratio.HasValue)
        {
            var interest = ratio.Value > 0.3 ? "High" : ratio.Value > 0.15 ? "Moderate" : "Low";
            sb.AppendLine($"\u2022 New release interest: {interest} ({ratio.Value:P0} recent)");
        }

        if (profile.RecentlyAdded != null && profile.RecentlyAdded.Any())
        {
            var recent = string.Join(", ", profile.RecentlyAdded.Take(10));
            sb.AppendLine($"\u2022 Recently added artists: {recent}");
        }

        return sb.ToString().TrimEnd();
    }
}
