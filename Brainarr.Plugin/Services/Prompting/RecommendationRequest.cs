using System;
using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

/// <summary>
/// Encapsulates the raw inputs required to plan a provider-specific prompt.
/// </summary>
public sealed class RecommendationRequest
{
    public RecommendationRequest(
        IReadOnlyList<Artist> artists,
        IReadOnlyList<Album> albums,
        BrainarrSettings settings,
        LibraryStyleContext styleContext,
        bool recommendArtists,
        int targetTokens,
        int availableSamplingTokens,
        string modelKey,
        int contextWindow)
    {
        Artists = artists ?? throw new ArgumentNullException(nameof(artists));
        Albums = albums ?? throw new ArgumentNullException(nameof(albums));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        StyleContext = styleContext ?? new LibraryStyleContext();
        RecommendArtists = recommendArtists;
        TargetTokens = targetTokens;
        AvailableSamplingTokens = availableSamplingTokens;
        ModelKey = modelKey ?? string.Empty;
        ContextWindow = contextWindow;
    }

    public IReadOnlyList<Artist> Artists { get; }

    public IReadOnlyList<Album> Albums { get; }

    public BrainarrSettings Settings { get; }

    public LibraryStyleContext StyleContext { get; }

    public bool RecommendArtists { get; }

    public int TargetTokens { get; }

    public int AvailableSamplingTokens { get; }

    public string ModelKey { get; }

    public int ContextWindow { get; }
}
