using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Capabilities;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Sections;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public class LibraryPromptRenderer : IPromptRenderer
{
    private readonly IReadOnlyList<IPromptSection> _sections;

    public LibraryPromptRenderer()
        : this(new IPromptSection[]
        {
            new CollectionContextSection(),
            new MusicalDnaSection(),
            new CollectionPatternsSection()
        })
    {
    }

    internal LibraryPromptRenderer(IEnumerable<IPromptSection> sections)
    {
        _sections = sections?.OrderBy(s => s.Order).ToList()
            ?? throw new ArgumentNullException(nameof(sections));
    }

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
        var minimalFormatting = settings.PreferMinimalPromptFormatting || ProviderCapabilities.Get(settings.Provider).RequiresMinimalFormatting;

        // Keep planner header stable for determinism tests; actual cache-hit is exposed via metrics
        builder.AppendLine($"[PLANNER] version={PlannerBuild.ConfigVersion} cache_hit=false seed={plan.SampleSeed}");
        builder.AppendLine();

        string Heading(string emojiHeading, string asciiHeading) => minimalFormatting ? asciiHeading : emojiHeading;

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
            builder.AppendLine(Heading("\U0001f3a8 STYLE FILTERS (library-aligned):", "STYLE FILTERS (library-aligned):"));
            foreach (var entry in styles.Entries)
            {
                var aliasText = entry.Aliases != null && entry.Aliases.Any()
                    ? $" (aliases: {string.Join(", ", entry.Aliases.Take(5))})"
                    : string.Empty;
                var coverage = styles.Coverage.TryGetValue(entry.Slug, out var count) ? $" \u2022 coverage: {count}" : string.Empty;
                builder.AppendLine($"\u2022 {entry.Name}{aliasText}{coverage}");
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

        // Render composable sections (Collection Overview, Musical DNA, Collection Patterns)
        foreach (var section in _sections)
        {
            if (section.CanBuild(plan, profile))
            {
                builder.AppendLine(section.Build(plan, profile, minimalFormatting));
                builder.AppendLine();
            }
        }

        var artistLines = BuildArtistGroups(plan);
        builder.AppendLine(Heading($"\U0001f3b6 LIBRARY ARTISTS & KEY ALBUMS ({artistLines.Count} groups shown):", $"LIBRARY ARTISTS & KEY ALBUMS ({artistLines.Count} groups shown):"));
        foreach (var line in artistLines)
        {
            builder.AppendLine(line);
        }
        builder.AppendLine();

        builder.AppendLine(Heading("\U0001f3af RECOMMENDATION REQUIREMENTS:", "RECOMMENDATION REQUIREMENTS:"));
        if (plan.ShouldRecommendArtists)
        {
            builder.AppendLine("1. DO NOT recommend any artists already listed above (they represent a much larger library).");
            builder.AppendLine($"2. Return EXACTLY {settings.MaxRecommendations} NEW ARTIST recommendations as JSON.");
            builder.AppendLine("3. Each entry must include: artist, genre, confidence (0.0-1.0), adjacency_source, reason.");
            builder.AppendLine("4. Focus on artists \u2013 Lidarr will import their releases.");
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

        AppendResponseFormat(builder, template, plan.ShouldRecommendArtists);

        return builder.ToString();
    }

    private void AppendResponseFormat(StringBuilder builder, ModelPromptTemplate template, bool recommendArtists)
    {
        var sampleLines = BuildSampleJson(recommendArtists);

        if (template == ModelPromptTemplate.Anthropic)
        {
            builder.AppendLine("Respond with a single JSON array. Do not add commentary before or after the array. Use the structure below:");
        }
        else if (template == ModelPromptTemplate.Gemini)
        {
            builder.AppendLine("Respond using application/json only. Do not wrap the output in Markdown or add prose. Use the structure below:");
        }
        else
        {
            builder.AppendLine("JSON Response Format:");
        }

        foreach (var line in sampleLines)
        {
            builder.AppendLine(line);
        }
    }

    private static IReadOnlyList<string> BuildSampleJson(bool recommendArtists)
    {
        var lines = new List<string>
        {
            "[",
            "  {",
            "    \"artist\": \"Artist Name\","
        };

        if (!recommendArtists)
        {
            lines.Add("    \"album\": \"Album Title\",");
            lines.Add("    \"year\": 2024,");
        }

        lines.Add("    \"genre\": \"Primary Genre\",");
        lines.Add("    \"confidence\": 0.85,");
        lines.Add("    \"adjacency_source\": \"Shared producer with <existing artist>\",");
        lines.Add("    \"reason\": \"Explain the concrete connection to the user's library\"");
        lines.Add("  }");
        lines.Add("]");

        return lines;
    }

    private List<string> BuildArtistGroups(PromptPlan plan)
    {
        var lines = new List<string>();
        var ordered = plan.Sample.Artists
            .OrderByDescending(a => a.Weight)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            // deterministic tiebreak: ArtistId resolves identical weights and names
            .ThenBy(a => a.ArtistId)
            .Take(plan.Compression.MaxArtists)
            .ToList();

        foreach (var artist in ordered)
        {
            var albums = artist.Albums
                .OrderByDescending(a => NormalizeAdded(a.Added))
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                // deterministic tiebreak: AlbumId stabilizes rendering output
                .ThenBy(a => a.AlbumId)
                .ToList();
            var line = new StringBuilder();
            var styleText = artist.MatchedStyles.Length > 0 ? $" [{string.Join("/", artist.MatchedStyles)}]" : string.Empty;
            line.Append("\u2022 ").Append(artist.Name).Append(styleText);
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
        return $" \u2014 [{string.Join("; ", slice)}{suffix}]";
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

    private static string GetCollectionCharacter(LibraryProfile profile)
    {
        return ProfileMetadataHelper.GetString(profile, "CollectionFocus", "balanced");
    }

    private static string GetTemporalPreference(LibraryProfile profile)
    {
        var eras = ProfileMetadataHelper.GetTyped<List<string>>(profile, "PreferredEras");
        if (eras != null && eras.Any())
        {
            return string.Join("/", eras).ToLowerInvariant();
        }

        return "mixed era";
    }

    private static string GetDiscoveryTrend(LibraryProfile profile)
    {
        return ProfileMetadataHelper.GetString(profile, "DiscoveryTrend", "steady");
    }

    private static DateTime NormalizeAdded(DateTime? value)
    {
        if (!value.HasValue || value.Value == default)
        {
            return DateTime.MinValue;
        }

        return value.Value;
    }
}
