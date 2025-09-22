using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public sealed record PromptPlan(
    LibrarySample Sample,
    IReadOnlyList<string> StylesUsed)
{
    public int ContextWindow { get; init; }

    public int TargetTokens { get; init; }

    public int HeadroomTokens { get; init; }

    public int EstimatedTokensPreCompression { get; init; }

    public int ActualPromptTokens { get; init; }

    public bool TrimmedForBudget { get; init; }

    public bool Compressed { get; init; }

    public double? CompressionRatio { get; init; }

    public double DriftRatio { get; init; }

    public string LibraryFingerprint { get; init; } = string.Empty;

    public string PlanCacheKey { get; init; } = string.Empty;

    public bool FromCache { get; init; }

    public LibraryProfile Profile { get; init; } = new();

    public BrainarrSettings Settings { get; init; } = new();

    public StylePlanContext StyleContext { get; init; } = StylePlanContext.Empty;

    public bool ShouldRecommendArtists { get; init; }

    public PromptCompressionState Compression { get; init; } = PromptCompressionState.Empty;

    public string SampleFingerprint { get; init; } = string.Empty;

    public string SampleSeed { get; init; } = string.Empty;

    public bool RelaxedStyleMatching { get; init; }

    public bool StyleCoverageSparse { get; init; }

    public IReadOnlyList<string> TrimmedStyles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> InferredStyleSlugs { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, int> StyleCoverage { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> MatchedStyleCounts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public sealed class PromptCompressionState
{
    public static PromptCompressionState Empty { get; } = new PromptCompressionState(0, 0, 0);

    private int _maxArtists;
    private int _maxAlbumGroups;
    private int _maxAlbumsPerGroup;
    private bool _compressed;
    private bool _trimmed;

    public PromptCompressionState(int maxArtists, int maxAlbumGroups, int maxAlbumsPerGroup)
    {
        _maxArtists = Math.Max(0, maxArtists);
        _maxAlbumGroups = Math.Max(0, maxAlbumGroups);
        _maxAlbumsPerGroup = Math.Max(1, maxAlbumsPerGroup);
    }

    public int MaxArtists => _maxArtists;

    public int MaxAlbumGroups => _maxAlbumGroups;

    public int MaxAlbumsPerGroup => _maxAlbumsPerGroup;

    public bool IsCompressed => _compressed;

    public bool IsTrimmed => _trimmed;

    public bool TryCompress(LibrarySample sample)
    {
        if (sample == null)
        {
            throw new ArgumentNullException(nameof(sample));
        }

        if (_maxAlbumsPerGroup > 3)
        {
            _maxAlbumsPerGroup--;
            _compressed = true;
            return true;
        }

        if (_maxAlbumGroups > Math.Min(12, sample.Artists.Count))
        {
            _maxAlbumGroups = Math.Max(Math.Min(12, sample.Artists.Count), _maxAlbumGroups - 3);
            _compressed = true;
            _trimmed = true;
            return true;
        }

        if (_maxArtists > Math.Min(15, sample.Artists.Count))
        {
            _maxArtists = Math.Max(Math.Min(15, sample.Artists.Count), _maxArtists - 3);
            _compressed = true;
            _trimmed = true;
            return true;
        }

        return false;
    }

    public void MarkTrimmed()
    {
        _trimmed = true;
        _compressed = true;
    }

    public PromptCompressionState Clone()
    {
        var clone = new PromptCompressionState(_maxArtists, _maxAlbumGroups, _maxAlbumsPerGroup);
        if (_compressed)
        {
            clone._compressed = true;
        }

        if (_trimmed)
        {
            clone._trimmed = true;
        }

        return clone;
    }
}

public sealed class StylePlanContext
{
    public static StylePlanContext Empty { get; } = new StylePlanContext(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new List<StyleEntry>(),
        new List<StyleEntry>(),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        relaxed: false,
        threshold: 1.0,
        new List<string>(),
        new List<string>());

    public StylePlanContext(
        ISet<string> selected,
        ISet<string> expanded,
        List<StyleEntry> entries,
        List<StyleEntry> adjacent,
        Dictionary<string, int> coverage,
        bool relaxed,
        double threshold,
        List<string> trimmed,
        List<string> inferred)
    {
        if (selected is null)
        {
            throw new ArgumentNullException(nameof(selected));
        }

        if (expanded is null)
        {
            throw new ArgumentNullException(nameof(expanded));
        }

        SelectedSlugs = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        ExpandedSlugs = new HashSet<string>(expanded, StringComparer.OrdinalIgnoreCase);

        foreach (var slug in SelectedSlugs)
        {
            ExpandedSlugs.Add(slug);
        }

        Entries = entries ?? new List<StyleEntry>();
        AdjacentEntries = adjacent ?? new List<StyleEntry>();
        Coverage = coverage ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Relaxed = relaxed;
        Threshold = threshold;
        TrimmedSlugs = trimmed ?? new List<string>();
        InferredSlugs = inferred ?? new List<string>();
    }

    public ISet<string> SelectedSlugs { get; }

    public ISet<string> ExpandedSlugs { get; }

    public List<StyleEntry> Entries { get; }

    public List<StyleEntry> AdjacentEntries { get; }

    public Dictionary<string, int> Coverage { get; }

    public bool Relaxed { get; }

    public double Threshold { get; }

    public List<string> TrimmedSlugs { get; }

    public List<string> InferredSlugs { get; }

    public Dictionary<string, int> MatchedCounts { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public bool Sparse { get; set; }

    public bool HasStyles => SelectedSlugs.Count > 0;

    public bool ShouldUseRelaxedMatches => Relaxed && ExpandedSlugs.Any(slug => !SelectedSlugs.Contains(slug));

    public void RegisterMatch(IEnumerable<string> slugs)
    {
        if (slugs == null)
        {
            return;
        }

        foreach (var slug in slugs)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            MatchedCounts.TryGetValue(slug, out var count);
            MatchedCounts[slug] = count + 1;
        }
    }
}
