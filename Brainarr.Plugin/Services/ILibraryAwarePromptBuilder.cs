using System;
using System.Collections.Generic;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services;

/// <summary>
/// Interface for building library-aware prompts optimized for AI providers.
/// </summary>
public interface ILibraryAwarePromptBuilder
{
    /// <summary>
    /// Builds a library-aware prompt optimized for the specified AI provider.
    /// </summary>
    /// <param name="profile">User's library profile with genre and artist preferences</param>
    /// <param name="allArtists">Complete list of artists in the library</param>
    /// <param name="allAlbums">Complete list of albums for context</param>
    /// <param name="settings">Configuration including provider, discovery mode, and constraints</param>
    /// <param name="shouldRecommendArtists">Whether to recommend artists or specific albums</param>
    /// <returns>A token-optimized prompt with relevant library context</returns>
    string BuildLibraryAwarePrompt(
        LibraryProfile profile,
        List<Artist> allArtists,
        List<Album> allAlbums,
        BrainarrSettings settings,
        bool shouldRecommendArtists = false);

    /// <summary>
    /// Returns the effective token limit for the given sampling strategy and provider.
    /// </summary>
    int GetEffectiveTokenLimit(SamplingStrategy strategy, AIProvider provider);

    /// <summary>
    /// Estimates the token count of the provided text using the builder's heuristic.
    /// </summary>
    int EstimateTokens(string text);

    /// <summary>
    /// Builds a library-aware prompt and returns prompt + sampling metrics.
    /// </summary>
    LibraryPromptResult BuildLibraryAwarePromptWithMetrics(
        LibraryProfile profile,
        List<Artist> allArtists,
        List<Album> allAlbums,
        BrainarrSettings settings,
        bool shouldRecommendArtists = false);
}

public class LibraryPromptResult
{
    public string Prompt { get; set; } = string.Empty;
    public int SampledArtists { get; set; }
    public int SampledAlbums { get; set; }
    public int EstimatedTokens { get; set; }
    public int EstimatedTokensPreCompression { get; set; }
    public int PromptBudgetTokens { get; set; }
    public int ModelContextTokens { get; set; }
    public string BudgetModelKey { get; set; } = string.Empty;
    public bool Compressed { get; set; }
    public bool Trimmed { get; set; }
    public string FallbackReason { get; set; } = string.Empty;
    public string SampleSeed { get; set; } = string.Empty;
    public string SampleFingerprint { get; set; } = string.Empty;
    public bool RelaxedStyleMatching { get; set; }
    public List<string> AppliedStyleSlugs { get; set; } = new List<string>();
    public List<string> AppliedStyleNames { get; set; } = new List<string>();
    public List<string> TrimmedStyles { get; set; } = new List<string>();
    public List<string> InferredStyleSlugs { get; set; } = new List<string>();
    public Dictionary<string, int> StyleCoverage { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> MatchedStyleCounts { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public bool StyleCoverageSparse { get; set; }
}
