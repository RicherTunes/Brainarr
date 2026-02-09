using System.Text;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Sections;

/// <summary>
/// Renders the "COLLECTION PATTERNS" section with discovery trend, quality, and tracking info.
/// Only emits when at least one pattern is available.
/// </summary>
internal sealed class CollectionPatternsSection : IPromptSection
{
    public int Order => 30;

    public bool CanBuild(PromptPlan plan, LibraryProfile profile)
    {
        // Only render if there's at least one pattern to show
        return !string.IsNullOrEmpty(BuildContent(profile));
    }

    public string Build(PromptPlan plan, LibraryProfile profile, bool minimalFormatting)
    {
        var sb = new StringBuilder();
        sb.AppendLine(minimalFormatting ? "COLLECTION PATTERNS:" : "\U0001f4c8 COLLECTION PATTERNS:");
        sb.AppendLine(BuildContent(profile));
        return sb.ToString().TrimEnd();
    }

    private static string BuildContent(LibraryProfile profile)
    {
        var context = new StringBuilder();

        var discoveryTrend = ProfileMetadataHelper.GetString(profile, "DiscoveryTrend", "");
        if (!string.IsNullOrEmpty(discoveryTrend))
        {
            context.AppendLine($"\u2022 Discovery trend: {discoveryTrend}");
        }

        var completeness = ProfileMetadataHelper.GetDouble(profile, "CollectionCompleteness");
        if (completeness.HasValue)
        {
            var quality = completeness.Value > 0.8 ? "Very High" : completeness.Value > 0.6 ? "High" : completeness.Value > 0.4 ? "Moderate" : "Building";
            context.AppendLine($"\u2022 Collection quality: {quality} ({completeness.Value:P0} complete)");
        }

        var monitoredRatio = ProfileMetadataHelper.GetDouble(profile, "MonitoredRatio");
        if (monitoredRatio.HasValue)
        {
            context.AppendLine($"\u2022 Active tracking: {monitoredRatio.Value:P0} of collection");
        }

        var topCollected = ProfileMetadataHelper.GetTopN<int>(
            profile, "TopCollectedArtistNames", 5,
            kv => $"{kv.Key} ({kv.Value})");
        if (!string.IsNullOrEmpty(topCollected))
        {
            context.AppendLine($"\u2022 Top collected artists: {topCollected}");
        }

        return context.ToString().TrimEnd();
    }
}
