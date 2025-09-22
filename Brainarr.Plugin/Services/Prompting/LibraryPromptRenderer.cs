using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public class LibraryPromptRenderer : IPromptRenderer
{
    public string Render(PromptPlan plan, ModelPromptTemplate template, CancellationToken cancellationToken)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var builder = new StringBuilder();
        var settings = plan.Settings;
        var profile = plan.Profile;
        var styles = plan.StyleContext;

        var strategyPreamble = GetSamplingStrategyPreamble(settings.SamplingStrategy);
        if (!string.IsNullOrEmpty(strategyPreamble))
        {
            builder.AppendLine(strategyPreamble);
            builder.AppendLine();
            builder.AppendLine("Note: Items below are representative samples of a much larger library; avoid recommending duplicates even if not explicitly listed.");
            builder.AppendLine();
        }

        builder.AppendLine(GetDiscoveryModeTemplate(settings.DiscoveryMode, settings.MaxRecommendations, plan.ShouldRecommendArtists, styles.HasStyles));
        builder.AppendLine();

        if (styles.HasStyles)
        {
            builder.AppendLine("🎨 STYLE FILTERS (library-aligned):");
            foreach (var entry in styles.Entries)
            {
                var aliasText = entry.Aliases != null && entry.Aliases.Any()
                    ? $" (aliases: {string.Join(", ", entry.Aliases.Take(5))})"
                    : string.Empty;
                var coverage = styles.Coverage.TryGetValue(entry.Slug, out var count) ? $" • coverage: {count}" : string.Empty;
                builder.AppendLine($"• {entry.Name}{aliasText}{coverage}");
            }

            if (styles.AdjacentEntries.Any())
            {
                builder.AppendLine($"Adjacent context (only if needed): {string.Join(", ", styles.AdjacentEntries.Select(a => a.Name))}");
            }

            if (styles.Sparse)
            {
                builder.AppendLine("Sparse library coverage detected for these styles. Stay inside the cluster and prefer concrete connections (collaborators, side projects, shared labels).");
            }

            builder.AppendLine("Rule: Recommendations must live inside these styles and be grounded in the user's existing collection footprint.");
            builder.AppendLine();
        }

        builder.AppendLine("📊 COLLECTION OVERVIEW:");
        builder.AppendLine(BuildEnhancedCollectionContext(profile));
        builder.AppendLine();

        builder.AppendLine("🎵 MUSICAL DNA:");
        builder.AppendLine(BuildMusicalDnaContext(profile));
        builder.AppendLine();

        var patterns = BuildCollectionPatterns(profile);
        if (!string.IsNullOrEmpty(patterns))
        {
            builder.AppendLine("📈 COLLECTION PATTERNS:");
            builder.AppendLine(patterns);
            builder.AppendLine();
        }

        var artistLines = BuildArtistGroups(plan);
        builder.AppendLine($"🎶 LIBRARY ARTISTS & KEY ALBUMS ({artistLines.Count} groups shown):");
        foreach (var line in artistLines)
        {
            builder.AppendLine(line);
        }
        builder.AppendLine();

        builder.AppendLine("🎯 RECOMMENDATION REQUIREMENTS:");
        if (plan.ShouldRecommendArtists)
        {
            builder.AppendLine("1. DO NOT recommend any artists already listed above (they represent a much larger library).");
            builder.AppendLine($"2. Return EXACTLY {settings.MaxRecommendations} NEW ARTIST recommendations as JSON.");
            builder.AppendLine("3. Each entry must include: artist, genre, confidence (0.0-1.0), adjacency_source, reason.");
            builder.AppendLine("4. Focus on artists – Lidarr will import their releases.");
            builder.AppendLine("5. Highlight the concrete connection to the user's library (collaborations, side projects, shared producers, labelmates).");
        }
        else
        {
            builder.AppendLine("1. DO NOT recommend any albums already listed above (treat the list as representative).");
            builder.AppendLine($"2. Return EXACTLY {settings.MaxRecommendations} NEW ALBUM recommendations as JSON.");
            builder.AppendLine("3. Each entry must include: artist, album, genre, year, confidence (0.0-1.0), adjacency_source, reason.");
            builder.AppendLine("4. Prefer studio albums over live or compilation releases.");
        }

        builder.AppendLine("6. Keep every recommendation inside the style cluster defined above.");
        builder.AppendLine($"7. Match the collection's {GetCollectionCharacter(profile)} character.");
        builder.AppendLine($"8. Align with {GetTemporalPreference(profile)} temporal preferences.");
        builder.AppendLine($"9. Consider {GetDiscoveryTrend(profile)} discovery pattern.");
        builder.AppendLine();

        builder.AppendLine("JSON Response Format:");
        builder.AppendLine("[");
        builder.AppendLine("  {");
        builder.AppendLine("    \"artist\": \"Artist Name\",");
        if (!plan.ShouldRecommendArtists)
        {
            builder.AppendLine("    \"album\": \"Album Title\",");
            builder.AppendLine("    \"year\": 2024,");
        }

        builder.AppendLine("    \"genre\": \"Primary Genre\",");
        builder.AppendLine("    \"confidence\": 0.85,");
        builder.AppendLine("    \"adjacency_source\": \"Shared producer with <existing artist>\",");
        builder.AppendLine("    \"reason\": \"Explain the concrete connection to the user's library\"");
        builder.AppendLine("  }");
        builder.AppendLine("]");

        return builder.ToString();
    }

    private List<string> BuildArtistGroups(PromptPlan plan)
    {
        var lines = new List<string>();
        var ordered = plan.Sample.Artists
            .OrderByDescending(a => a.Weight)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Take(plan.Compression.MaxArtists)
            .ToList();

        foreach (var artist in ordered)
        {
            var albums = artist.Albums
                .OrderByDescending(a => a.Added ?? DateTime.MinValue)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var line = new StringBuilder();
            var styleText = artist.MatchedStyles.Length > 0 ? $" [{string.Join("/", artist.MatchedStyles)}]" : string.Empty;
            line.Append("• ").Append(artist.Name).Append(styleText);
            line.Append(BuildAlbumText(albums, plan));
            lines.Add(line.ToString());
        }

        if (lines.Count > plan.Compression.MaxAlbumGroups)
        {
            lines = lines.Take(plan.Compression.MaxAlbumGroups).ToList();
        }

        return lines;
    }

    private string BuildAlbumText(List<LibrarySampleAlbum> albums, PromptPlan plan)
    {
        if (albums.Count == 0)
        {
            return string.Empty;
        }

        var slice = albums
            .Take(plan.Compression.MaxAlbumsPerGroup)
            .Select(a => a.Year.HasValue ? $"{a.Title} ({a.Year.Value})" : a.Title);
        var more = albums.Count - plan.Compression.MaxAlbumsPerGroup;
        var suffix = more > 0 ? $"; +{more} more" : string.Empty;
        return $" — [{string.Join("; ", slice)}{suffix}]";
    }

    private string GetSamplingStrategyPreamble(SamplingStrategy strategy)
    {
        return strategy switch
        {
            SamplingStrategy.Minimal => "[STYLE_AWARE] Use quick-hit sampling (minimal context).",
            SamplingStrategy.Balanced => "[STYLE_AWARE] Use balanced sampling with key artists/albums.",
            SamplingStrategy.Comprehensive => "[STYLE_AWARE] Use comprehensive sampling. Prioritize depth and adjacency clusters.",
            _ => string.Empty
        };
    }

    private string GetDiscoveryModeTemplate(DiscoveryMode mode, int maxRecommendations, bool recommendArtists, bool hasStyles)
    {
        var focus = mode switch
        {
            DiscoveryMode.Similar => "Similar artists and albums deeply rooted in the collection's existing styles.",
            DiscoveryMode.Adjacent => "Adjacent discoveries with concrete ties to the collection (collaborators, labelmates, side projects).",
            DiscoveryMode.Exploratory => "Exploratory finds that expand the listener's horizons while staying grounded in real connections.",
            _ => "Collection-aligned recommendations with clear adjacency."
        };

        var anchor = hasStyles ? "Respect style anchors provided." : "Respect the listener's library even without explicit style anchors.";
        var subject = recommendArtists ? "artists" : "albums";
        return $"Recommend exactly {maxRecommendations} new {subject}. Focus: {focus} {anchor}";
    }

    private string BuildEnhancedCollectionContext(LibraryProfile profile)
    {
        var context = new StringBuilder();

        var collectionSize = profile.Metadata?.ContainsKey("CollectionSize") == true
            ? profile.Metadata["CollectionSize"].ToString()
            : "established";

        var collectionFocus = profile.Metadata?.ContainsKey("CollectionFocus") == true
            ? profile.Metadata["CollectionFocus"].ToString()
            : "general";

        context.AppendLine($"• Size: {collectionSize} ({profile.TotalArtists} artists, {profile.TotalAlbums} albums)");

        if (profile.Metadata?.ContainsKey("GenreDistribution") == true &&
            profile.Metadata["GenreDistribution"] is Dictionary<string, double> genreDistribution &&
            genreDistribution.Any())
        {
            var topGenres = string.Join(", ", genreDistribution
                .Where(kv => !kv.Key.EndsWith("_significance", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .Select(kv => $"{kv.Key} ({kv.Value:F1}%)"));
            context.AppendLine($"• Genres: {topGenres}");
        }
        else
        {
            context.AppendLine($"• Genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => g.Key))}");
        }

        if (profile.Metadata?.ContainsKey("CollectionStyle") == true)
        {
            var style = profile.Metadata["CollectionStyle"].ToString();
            var completion = profile.Metadata?.ContainsKey("CompletionistScore") == true
                ? Convert.ToDouble(profile.Metadata["CompletionistScore"])
                : (double?)null;
            if (completion.HasValue)
            {
                context.AppendLine($"• Collection style: {style} (completionist score: {completion.Value:F1}%)");
                context.AppendLine($"• Completionist score: {completion.Value:F1}%");
            }
            else
            {
                context.AppendLine($"• Collection style: {style}");
            }
        }
        else
        {
            context.AppendLine($"• Collection style: {collectionFocus}");
        }

        if (profile.Metadata?.ContainsKey("AverageAlbumsPerArtist") == true)
        {
            var avg = Convert.ToDouble(profile.Metadata["AverageAlbumsPerArtist"]);
            context.AppendLine($"• Collection depth: avg {avg:F1} albums per artist");
        }

        return context.ToString().TrimEnd();
    }

    private string BuildMusicalDnaContext(LibraryProfile profile)
    {
        var context = new StringBuilder();

        if (profile.Metadata?.ContainsKey("PreferredEras") == true &&
            profile.Metadata["PreferredEras"] is List<string> eras && eras.Any())
        {
            context.AppendLine($"• Era preference: {string.Join(", ", eras)}");
        }

        if (profile.Metadata?.ContainsKey("AlbumTypes") == true &&
            profile.Metadata["AlbumTypes"] is Dictionary<string, int> albumTypes && albumTypes.Any())
        {
            var topTypes = string.Join(", ", albumTypes
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => $"{kv.Key} ({kv.Value})"));
            context.AppendLine($"• Album types: {topTypes}");
        }

        if (profile.Metadata?.ContainsKey("NewReleaseRatio") == true)
        {
            var ratio = Convert.ToDouble(profile.Metadata["NewReleaseRatio"]);
            var interest = ratio > 0.3 ? "High" : ratio > 0.15 ? "Moderate" : "Low";
            context.AppendLine($"• New release interest: {interest} ({ratio:P0} recent)");
        }

        if (profile.RecentlyAdded != null && profile.RecentlyAdded.Any())
        {
            var recent = string.Join(", ", profile.RecentlyAdded.Take(10));
            context.AppendLine($"• Recently added artists: {recent}");
        }

        return context.ToString().TrimEnd();
    }

    private string BuildCollectionPatterns(LibraryProfile profile)
    {
        var context = new StringBuilder();

        if (profile.Metadata?.ContainsKey("DiscoveryTrend") == true)
        {
            context.AppendLine($"• Discovery trend: {profile.Metadata["DiscoveryTrend"]}");
        }

        if (profile.Metadata?.ContainsKey("CollectionCompleteness") == true)
        {
            var completeness = Convert.ToDouble(profile.Metadata["CollectionCompleteness"]);
            var quality = completeness > 0.8 ? "Very High" : completeness > 0.6 ? "High" : completeness > 0.4 ? "Moderate" : "Building";
            context.AppendLine($"• Collection quality: {quality} ({completeness:P0} complete)");
        }

        if (profile.Metadata?.ContainsKey("MonitoredRatio") == true)
        {
            var ratio = Convert.ToDouble(profile.Metadata["MonitoredRatio"]);
            context.AppendLine($"• Active tracking: {ratio:P0} of collection");
        }

        if (profile.Metadata?.ContainsKey("TopCollectedArtistNames") == true &&
            profile.Metadata["TopCollectedArtistNames"] is Dictionary<string, int> nameCounts && nameCounts.Any())
        {
            var line = string.Join(", ", nameCounts
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .Select(kv => $"{kv.Key} ({kv.Value})"));
            context.AppendLine($"• Top collected artists: {line}");
        }

        return context.ToString().TrimEnd();
    }

    private string GetCollectionCharacter(LibraryProfile profile)
    {
        if (profile.Metadata?.ContainsKey("CollectionFocus") == true)
        {
            return profile.Metadata["CollectionFocus"].ToString();
        }

        return "balanced";
    }

    private string GetTemporalPreference(LibraryProfile profile)
    {
        if (profile.Metadata?.ContainsKey("PreferredEras") == true &&
            profile.Metadata["PreferredEras"] is List<string> eras && eras.Any())
        {
            return string.Join("/", eras).ToLowerInvariant();
        }

        return "mixed era";
    }

    private string GetDiscoveryTrend(LibraryProfile profile)
    {
        if (profile.Metadata?.ContainsKey("DiscoveryTrend") == true)
        {
            return profile.Metadata["DiscoveryTrend"].ToString();
        }

        return "steady";
    }
}
